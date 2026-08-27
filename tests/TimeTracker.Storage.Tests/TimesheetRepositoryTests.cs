using TimeTracker.Core;
using TimeTracker.Storage;

namespace TimeTracker.Storage.Tests;

/// <summary>
/// Round-trip tests against a real encrypted SQLite file in a temp directory. Deliberately
/// not mocked — the things that break storage are SQL, schema and serialisation, none of
/// which a fake would exercise.
/// </summary>
public sealed class TimesheetRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly TimeTrackerDatabase _db;
    private readonly TimesheetRepository _repo;

    public TimesheetRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tt-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var key = new DatabaseKey(Path.Combine(_dir, "db.key")).GetOrCreate();
        _db = new TimeTrackerDatabase(Path.Combine(_dir, "test.db"), key);
        _db.Migrate();
        _repo = new TimesheetRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* temp dir */ }
    }

    private static DateTime On(int day, int h, int m) => new(2026, 8, day, h, m, 0);
    private static readonly DateOnly Date = new(2026, 8, 26);

    // ── days ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AnUnknownDay_LoadsAsNull()
        => Assert.Null(_repo.LoadDay(new DateOnly(1999, 1, 1)));

    [Fact]
    public void AStartedDay_RoundTrips()
    {
        _repo.SaveDay(WorkDay.Started(On(26, 9, 10)));

        var loaded = _repo.LoadDay(Date);

        Assert.NotNull(loaded);
        Assert.Equal(On(26, 9, 10), loaded!.StartedAt);
        Assert.True(loaded.IsOpen);
    }

    [Fact]
    public void BreaksLocationsAndNote_AllSurviveARoundTrip()
    {
        var day = WorkDay.Started(On(26, 9, 0))
                         .AtLocation(WorkLocation.Client, On(26, 9, 0))
                         .WithBreak(On(26, 13, 30), On(26, 14, 0))
                         .AtLocation(WorkLocation.Office, On(26, 14, 0))
                         .WithNote("Client visit in the first half.")
                         .Completed(On(26, 18, 30));

        _repo.SaveDay(day);
        var loaded = _repo.LoadDay(Date)!;

        Assert.Equal(On(26, 18, 30), loaded.CompletedAt);
        Assert.Single(loaded.Breaks);
        Assert.Equal(2, loaded.Locations.Count);
        Assert.True(loaded.HadClientVisit);
        Assert.Equal("Client visit in the first half.", loaded.Note);
    }

    [Fact]
    public void AnOpenBreak_SurvivesARoundTripStillOpen()
    {
        _repo.SaveDay(WorkDay.Started(On(26, 9, 0)).WithOpenBreak(On(26, 13, 0)));

        var loaded = _repo.LoadDay(Date)!;

        Assert.Single(loaded.Breaks);
        Assert.Null(loaded.Breaks[0].End);
    }

    [Fact]
    public void SavingTheSameDayTwice_DoesNotDuplicateChildRows()
    {
        // The widget saves on every change. Without a replace this would accumulate a
        // break row per keystroke and quietly wreck the day's arithmetic.
        var day = WorkDay.Started(On(26, 9, 0)).WithBreak(On(26, 13, 0), On(26, 13, 30));

        _repo.SaveDay(day);
        _repo.SaveDay(day);
        _repo.SaveDay(day);

        Assert.Single(_repo.LoadDay(Date)!.Breaks);
    }

    [Fact]
    public void TheStoredDay_StillComputesTheBriefsNumbers()
    {
        // Storage must not perturb the engine's result. Brief §4: 08:24 worked, 00:06 short.
        var policy = new WorkingHoursPolicy(
            new TimeSpan(4, 15, 0), new TimeSpan(8, 30, 0),
            [DayOfWeek.Wednesday], new TimeOnly(9, 0));

        _repo.SaveDay(WorkDay.Started(On(26, 9, 10))
                             .WithBreak(On(26, 13, 30), On(26, 14, 0))
                             .Completed(On(26, 18, 4)));

        var status = WorkDayCalculator.Calculate(_repo.LoadDay(Date)!, On(26, 18, 4), policy);

        Assert.Equal(new TimeSpan(8, 24, 0), status.Worked);
        Assert.Equal(new TimeSpan(0, 6, 0), status.Shortfall);
    }

    // ── the unclosed-day prompt ───────────────────────────────────────────────

    [Fact]
    public void ADayLeftOpen_IsFoundSoTheWidgetCanAskAboutIt()
    {
        _repo.SaveDay(WorkDay.Started(On(25, 9, 0)));                       // left open
        _repo.SaveDay(WorkDay.Started(On(24, 9, 0)).Completed(On(24, 18, 0)));

        Assert.Equal(new DateOnly(2026, 8, 25), _repo.FindUnclosedDayBefore(Date));
    }

    [Fact]
    public void WhenEveryPastDayIsClosed_ThereIsNothingToAskAbout()
    {
        _repo.SaveDay(WorkDay.Started(On(25, 9, 0)).Completed(On(25, 18, 0)));

        Assert.Null(_repo.FindUnclosedDayBefore(Date));
    }

    [Fact]
    public void TodayBeingOpen_IsNotTreatedAsAnOutstandingDay()
    {
        _repo.SaveDay(WorkDay.Started(On(26, 9, 0)));

        Assert.Null(_repo.FindUnclosedDayBefore(Date));
    }

    // ── activities ────────────────────────────────────────────────────────────

    [Fact]
    public void ActivitiesRoundTripWithTheirIdsIntact()
    {
        var log = ActivityLog.Empty(Date)
            .Start("API performance optimization", "Galaxy", On(26, 10, 0))
            .Start("Database migration", "ABC Logistics", On(26, 12, 30));

        _repo.SaveActivities(log);
        var loaded = _repo.LoadActivities(Date);

        Assert.Equal(2, loaded.Activities.Count);
        Assert.Equal(log.Activities[0].Id, loaded.Activities[0].Id);
        Assert.Equal("Galaxy", loaded.Activities[0].Customer);
        Assert.Equal(ActivityStatus.Completed, loaded.Activities[0].Status);
        Assert.NotNull(loaded.Current);
    }

    [Fact]
    public void ReplayingTheSameWrite_UpdatesRatherThanDuplicating()
    {
        // Brief §20 — the whole point of the activity id. A retried MCP call or a retried
        // sync must not produce a second row.
        var log = ActivityLog.Empty(Date).Start("API work", "Galaxy", On(26, 10, 0));

        _repo.SaveActivities(log);
        _repo.SaveActivities(log);
        _repo.SaveActivities(log.CompleteCurrent(On(26, 12, 30), ActivityStatus.Completed));

        var loaded = _repo.LoadActivities(Date);

        Assert.Single(loaded.Activities);
        Assert.Equal(On(26, 12, 30), loaded.Activities[0].End);
    }

    [Fact]
    public void ContextSurvivesARestart_SoTheWidgetDoesNotReAskForTheCustomer()
    {
        // This is §24 across a process boundary: close the widget, reopen it, and it must
        // still know who you were working for.
        _repo.SaveActivities(ActivityLog.Empty(Date).Start("API work", "Galaxy", On(26, 10, 0)));

        var reopened = _repo.LoadActivities(Date);

        Assert.Equal("Galaxy", reopened.CurrentCustomer);
        Assert.Equal("Galaxy", reopened.Start("Something else", null, On(26, 12, 0)).Current!.Customer);
    }

    [Fact]
    public void AnActivityWithNoCustomer_IsStillReportedAsAGapAfterReload()
    {
        _repo.SaveActivities(ActivityLog.Empty(Date).StartWithoutCustomer("Mystery task", On(26, 10, 0)));

        Assert.Single(_repo.LoadActivities(Date).MissingInformation());
    }

    // ── configuration ─────────────────────────────────────────────────────────

    [Fact]
    public void ConfigValues_RoundTripAndOverwrite()
    {
        _repo.SetConfig("full_day", "08:30");
        _repo.SetConfig("full_day", "09:00");

        Assert.Equal("09:00", _repo.GetConfig("full_day"));
        Assert.Null(_repo.GetConfig("never_set"));
    }

    // ── encryption ────────────────────────────────────────────────────────────

    [Fact]
    public void TheDatabaseFileIsNotReadableWithoutTheKey()
    {
        _repo.SaveActivities(ActivityLog.Empty(Date)
            .Start("Commercially sensitive project name", "ACME", On(26, 10, 0)));

        // Opened with FileShare.ReadWrite: the application holds the database open for its
        // whole life by design, so an exclusive read would fail for reasons unrelated to
        // what this test is actually asserting.
        using var stream = new FileStream(Path.Combine(_dir, "test.db"), FileMode.Open,
                                          FileAccess.Read, FileShare.ReadWrite);
        var raw = new byte[stream.Length];
        stream.ReadExactly(raw);
        var asText = System.Text.Encoding.UTF8.GetString(raw);

        // An unencrypted SQLite file starts with "SQLite format 3" and stores text verbatim.
        Assert.DoesNotContain("Commercially sensitive", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("SQLite format 3", asText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameKeyIsReusedAcrossRuns_SoDataSurvivesARestart()
    {
        var keyStore = new DatabaseKey(Path.Combine(_dir, "db.key"));

        Assert.Equal(keyStore.GetOrCreate(), keyStore.GetOrCreate());
    }

    // ── settings round trip (the path the settings screen actually takes) ──────

    [Fact]
    public void WorkingHoursSurviveARealSaveAndReload()
    {
        var policy = new WorkingHoursPolicy(
            HalfDay: new TimeSpan(4, 0, 0),
            FullDay: new TimeSpan(9, 15, 0),
            WorkingDays: [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Saturday],
            DefaultStart: new TimeOnly(8, 45));

        foreach (var (key, value) in policy.ToConfig()) _repo.SetConfig(key, value);

        var loaded = WorkingHoursPolicy.FromConfig(_repo.GetConfig);

        Assert.Equal(policy.HalfDay, loaded.HalfDay);
        Assert.Equal(policy.FullDay, loaded.FullDay);
        Assert.Equal(policy.DefaultStart, loaded.DefaultStart);
        Assert.Equal(3, loaded.WorkingDays.Count);
        Assert.Contains(DayOfWeek.Saturday, loaded.WorkingDays);
    }

    [Fact]
    public void PreferencesSurviveARealSaveAndReload()
    {
        var prefs = new TrackingPreferences(
            Statuses: [ActivityStatus.InProgress, ActivityStatus.Completed, ActivityStatus.Blocked],
            NudgeInterval: TimeSpan.FromMinutes(30),
            NotifyThresholds: false,
            NotifyReviewAtEnd: true);

        foreach (var (key, value) in prefs.ToConfig()) _repo.SetConfig(key, value);

        var loaded = TrackingPreferences.FromConfig(_repo.GetConfig);

        Assert.Equal(3, loaded.Statuses.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), loaded.NudgeInterval);
        Assert.False(loaded.NotifyThresholds);
        Assert.True(loaded.NotifyReviewAtEnd);
    }

    [Fact]
    public void ChangingTheFullDayChangesWhatTheEngineReports()
    {
        // The whole point of the settings screen: the number the user types has to reach
        // the arithmetic. 09:00 to 18:00 is nine hours - a full day under an 8:30 rule,
        // and half an hour short under a 9:30 one.
        _repo.SetConfig("full_day", "09:30");
        _repo.SetConfig("half_day", "04:45");

        var policy = WorkingHoursPolicy.FromConfig(_repo.GetConfig);
        var day = WorkDay.Started(On(26, 9, 0)).Completed(On(26, 18, 0));

        var status = WorkDayCalculator.Calculate(day, On(26, 18, 0), policy);

        Assert.Equal(new TimeSpan(9, 0, 0), status.Worked);
        Assert.Equal(new TimeSpan(0, 30, 0), status.Shortfall);
        Assert.Equal(DayCompletion.HalfDayComplete, status.Completion);
    }

    [Fact]
    public void SettingsSurviveTheDatabaseBeingReopened()
    {
        _repo.SetConfig("full_day", "07:45");

        // A second repository over the same file, as a restart would produce.
        var reopened = new TimesheetRepository(_db);

        Assert.Equal(new TimeSpan(7, 45, 0),
                     WorkingHoursPolicy.FromConfig(reopened.GetConfig).FullDay);
    }
}
