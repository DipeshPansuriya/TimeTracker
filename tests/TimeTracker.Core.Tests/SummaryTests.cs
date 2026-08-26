using TimeTracker.Core;

namespace TimeTracker.Core.Tests;

public class SummaryTests
{
    private static readonly WorkingHoursPolicy Policy = new(
        HalfDay: new TimeSpan(4, 15, 0),
        FullDay: new TimeSpan(8, 30, 0),
        WorkingDays: [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                      DayOfWeek.Thursday, DayOfWeek.Friday],
        DefaultStart: new TimeOnly(9, 0));

    private static DateTime On(int day, int h, int m) => new(2026, 8, day, h, m, 0);

    // ── §5 location spells ────────────────────────────────────────────────────

    [Fact]
    public void ADayCanSpanTwoLocations_ClientThenOffice()
    {
        // Brief §5: client location 09:00–13:30, office from 14:00.
        var day = WorkDay.Started(On(26, 9, 0))
                         .AtLocation(WorkLocation.Client, On(26, 9, 0))
                         .WithBreak(On(26, 13, 30), On(26, 14, 0))
                         .AtLocation(WorkLocation.Office, On(26, 14, 0))
                         .WithNote("Client visit in the first half; reached office at 2:00 PM.")
                         .Completed(On(26, 18, 30));

        Assert.Equal(WorkLocation.Client, day.LocationAt(On(26, 11, 0)));
        Assert.Equal(WorkLocation.Office, day.LocationAt(On(26, 15, 0)));
        Assert.True(day.HadClientVisit);
        Assert.NotNull(day.Note);
    }

    [Fact]
    public void ADayWithNoRecordedLocation_ReportsNone()
        => Assert.Null(WorkDay.Started(On(26, 9, 0)).LocationAt(On(26, 11, 0)));

    [Fact]
    public void SecondHalfArrival_IsFlaggedSoTheWidgetKnowsToAskForANote()
    {
        // Brief §5: "If the employee's office Time In is in the second half, the widget
        // should ask for a note/reason."
        var day = WorkDay.Started(On(26, 9, 0))
                         .AtLocation(WorkLocation.Client, On(26, 9, 0))
                         .AtLocation(WorkLocation.Office, On(26, 14, 0));

        Assert.True(day.NeedsLocationNote(middayBoundary: new TimeOnly(13, 0)));
    }

    [Fact]
    public void OnceTheNoteIsGiven_TheWidgetStopsAsking()
    {
        var day = WorkDay.Started(On(26, 9, 0))
                         .AtLocation(WorkLocation.Client, On(26, 9, 0))
                         .AtLocation(WorkLocation.Office, On(26, 14, 0))
                         .WithNote("Client visit in the first half.");

        Assert.False(day.NeedsLocationNote(middayBoundary: new TimeOnly(13, 0)));
    }

    // ── §10 daily summary ─────────────────────────────────────────────────────

    [Fact]
    public void Brief_Section10_DailySummary_MatchesTheDocumentedExample()
    {
        // Time In 09:10, Time Out 18:00, required 08:30, worked 08:50, overtime 00:20,
        // status Full Day, 3 tasks of which 2 complete, 2 customers.
        var day = WorkDay.Started(On(26, 9, 10))
                         .AtLocation(WorkLocation.Office, On(26, 9, 10))
                         .Completed(On(26, 18, 0));

        var log = ActivityLog.Empty(new DateOnly(2026, 8, 26))
            .Start("API performance optimization", "Galaxy", On(26, 10, 0))
            .Start("Sprint review", "Galaxy", On(26, 12, 30))
            .Start("Database migration", "ABC Logistics", On(26, 14, 0));

        var summary = DaySummary.Build(day, log, Policy, now: On(26, 18, 0));

        Assert.Equal(On(26, 9, 10), summary.TimeIn);
        Assert.Equal(On(26, 18, 0), summary.TimeOut);
        Assert.Equal(new TimeSpan(8, 50, 0), summary.Worked);
        Assert.Equal(new TimeSpan(0, 20, 0), summary.Overtime);
        Assert.Equal(DayCompletion.FullDayComplete, summary.Completion);
        Assert.Equal(3, summary.TotalTasks);
        Assert.Equal(2, summary.CompletedTasks);
        Assert.Equal(2, summary.Customers);
        Assert.Equal(WorkLocation.Office, summary.PrimaryLocation);
        Assert.False(summary.ClientVisit);
    }

    // ── §11 weekly summary ────────────────────────────────────────────────────

    [Fact]
    public void Brief_Section11_WeeklySummary_MatchesTheDocumentedExample()
    {
        // Required 42:30, worked 42:55, balance +00:25, 5 full days, 0 half days,
        // 2 WFH, 1 client visit.
        var days = new[]
        {
            Day(24, WorkLocation.Home,   new TimeSpan(8, 30, 0)),
            Day(25, WorkLocation.Office, new TimeSpan(8, 30, 0)),
            Day(26, WorkLocation.Client, new TimeSpan(8, 55, 0)),
            Day(27, WorkLocation.Home,   new TimeSpan(8, 30, 0)),
            Day(28, WorkLocation.Office, new TimeSpan(8, 30, 0)),
        };

        var week = PeriodSummary.Build(days, Policy);

        Assert.Equal(new TimeSpan(42, 30, 0), week.RequiredHours);
        Assert.Equal(new TimeSpan(42, 55, 0), week.WorkedHours);
        Assert.Equal(new TimeSpan(0, 25, 0), week.Balance);
        Assert.Equal(5, week.FullDays);
        Assert.Equal(0, week.HalfDays);
        Assert.Equal(2, week.WfhDays);
        Assert.Equal(2, week.OfficeDays);
        Assert.Equal(1, week.ClientVisitDays);
    }

    [Fact]
    public void Balance_IsNegativeWhenTheWeekIsShort()
    {
        var days = new[]
        {
            Day(24, WorkLocation.Office, new TimeSpan(8, 0, 0)),
            Day(25, WorkLocation.Office, new TimeSpan(8, 0, 0)),
        };

        var week = PeriodSummary.Build(days, Policy);

        Assert.Equal(new TimeSpan(17, 0, 0), week.RequiredHours);
        Assert.Equal(new TimeSpan(-1, 0, 0), week.Balance);
        Assert.Equal(new TimeSpan(1, 0, 0), week.Shortfall);
        Assert.Equal(TimeSpan.Zero, week.Overtime);
    }

    [Fact]
    public void OvertimeAndShortfallAreTrackedSeparately_NotJustNetted()
    {
        // A week that is +2h one day and −2h another nets to zero, but the user still
        // worked 2h over and 2h under. HRMS cares about both figures, not the net.
        var days = new[]
        {
            Day(24, WorkLocation.Office, new TimeSpan(10, 30, 0)),
            Day(25, WorkLocation.Office, new TimeSpan(6, 30, 0)),
        };

        var week = PeriodSummary.Build(days, Policy);

        Assert.Equal(new TimeSpan(2, 0, 0), week.Overtime);
        Assert.Equal(new TimeSpan(2, 0, 0), week.Shortfall);
        Assert.Equal(TimeSpan.Zero, week.Balance);
    }

    [Fact]
    public void HalfDaysAreCountedSeparatelyFromFullDays()
    {
        var days = new[]
        {
            Day(24, WorkLocation.Office, new TimeSpan(8, 30, 0)),
            Day(25, WorkLocation.Office, new TimeSpan(5, 0, 0)),
        };

        var week = PeriodSummary.Build(days, Policy);

        Assert.Equal(1, week.FullDays);
        Assert.Equal(1, week.HalfDays);
    }

    [Fact]
    public void CustomerHoursRollUpAcrossThePeriod()
    {
        var days = new[]
        {
            Day(24, WorkLocation.Office, new TimeSpan(8, 30, 0),
                ("Galaxy", new TimeSpan(5, 0, 0)), ("LPMS", new TimeSpan(3, 30, 0))),
            Day(25, WorkLocation.Office, new TimeSpan(8, 30, 0),
                ("Galaxy", new TimeSpan(8, 30, 0))),
        };

        var period = PeriodSummary.Build(days, Policy);

        Assert.Equal(new TimeSpan(13, 30, 0), period.ByCustomer["Galaxy"]);
        Assert.Equal(new TimeSpan(3, 30, 0), period.ByCustomer["LPMS"]);
    }

    [Fact]
    public void AnEmptyPeriodIsAllZeroes_NotAnError()
    {
        var period = PeriodSummary.Build([], Policy);

        Assert.Equal(TimeSpan.Zero, period.WorkedHours);
        Assert.Equal(0, period.WorkingDays);
        Assert.Empty(period.ByCustomer);
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private static DaySummary Day(
        int date, WorkLocation location, TimeSpan worked,
        params (string Customer, TimeSpan Time)[] byCustomer)
    {
        var d = new DateOnly(2026, 8, date);
        return new DaySummary(
            Date: d,
            TimeIn: new DateTime(d, new TimeOnly(9, 0)),
            TimeOut: new DateTime(d, new TimeOnly(9, 0)).Add(worked),
            Worked: worked,
            Required: Policy.FullDay,
            Remaining: worked >= Policy.FullDay ? TimeSpan.Zero : Policy.FullDay - worked,
            Overtime: worked > Policy.FullDay ? worked - Policy.FullDay : TimeSpan.Zero,
            Shortfall: worked < Policy.FullDay ? Policy.FullDay - worked : TimeSpan.Zero,
            Completion: worked >= Policy.FullDay ? DayCompletion.FullDayComplete
                      : worked >= Policy.HalfDay ? DayCompletion.HalfDayComplete
                      : DayCompletion.InProgress,
            PrimaryLocation: location,
            ClientVisit: location == WorkLocation.Client,
            TotalTasks: byCustomer.Length,
            CompletedTasks: byCustomer.Length,
            PendingTasks: 0,
            ByCustomer: byCustomer.ToDictionary(x => x.Customer, x => x.Time),
            ByTask: new Dictionary<string, TimeSpan>(),
            Note: null);
    }
}
