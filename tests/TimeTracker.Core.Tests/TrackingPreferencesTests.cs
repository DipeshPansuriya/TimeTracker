using TimeTracker.Core;

namespace TimeTracker.Core.Tests;

public class TrackingPreferencesTests
{
    private static TrackingPreferences From(Dictionary<string, string> values)
        => TrackingPreferences.FromConfig(k => values.GetValueOrDefault(k));

    [Fact]
    public void AnEmptyConfigGivesTheDocumentedDefaults()
    {
        var prefs = From([]);

        Assert.Equal(6, prefs.Statuses.Count);           // the six statuses in §9
        Assert.Equal(TimeSpan.FromHours(1), prefs.NudgeInterval);
        Assert.True(prefs.NotifyThresholds);
    }

    // ── §9: configurable statuses ─────────────────────────────────────────────

    [Fact]
    public void StatusesCanBeNarrowed()
    {
        var prefs = From(new() { ["task_statuses"] = "InProgress,Completed,Blocked" });

        Assert.Equal(3, prefs.Statuses.Count);
        Assert.DoesNotContain(ActivityStatus.Cancelled, prefs.Statuses);
    }

    [Fact]
    public void InProgressIsAlwaysAvailable_EvenIfSomeoneRemovesIt()
    {
        // A running task has to be describable. Without this the widget could not render
        // the task in front of you.
        var prefs = From(new() { ["task_statuses"] = "Completed,Cancelled" });

        Assert.Contains(ActivityStatus.InProgress, prefs.Statuses);
    }

    [Fact]
    public void UnknownStatusNamesAreIgnoredRatherThanFatal()
    {
        var prefs = From(new() { ["task_statuses"] = "Completed,Banana,Blocked" });

        Assert.Equal([ActivityStatus.InProgress, ActivityStatus.Completed, ActivityStatus.Blocked],
                     prefs.Statuses);
    }

    [Fact]
    public void DuplicatesAreCollapsed()
        => Assert.Equal(2, From(new() { ["task_statuses"] = "Completed,Completed,InProgress" })
                            .Statuses.Count);

    // ── §8: the nudge is optional and configurable ────────────────────────────

    [Theory]
    [InlineData("0")]
    [InlineData("-30")]
    public void ANudgeOfZeroOrLessMeansOff(string stored)
    {
        var prefs = From(new() { ["nudge_minutes"] = stored });

        Assert.Null(prefs.NudgeInterval);
        Assert.False(prefs.NudgeEnabled);
    }

    [Fact]
    public void ANudgeIntervalIsHonoured()
        => Assert.Equal(TimeSpan.FromMinutes(90),
                        From(new() { ["nudge_minutes"] = "90" }).NudgeInterval);

    [Fact]
    public void AnAbsurdlyShortNudgeIsClampedRatherThanHonoured()
    {
        // One minute is not a reminder, it is harassment, and it is the sort of value that
        // gets typed by accident.
        var prefs = From(new() { ["nudge_minutes"] = "1" });

        Assert.Equal(TimeSpan.FromMinutes(5), prefs.NudgeInterval);
    }

    [Fact]
    public void GarbageNudgeFallsBackToTheDefault()
        => Assert.Equal(TimeSpan.FromHours(1),
                        From(new() { ["nudge_minutes"] = "soon" }).NudgeInterval);

    // ── §22: notifications are configurable ───────────────────────────────────

    [Fact]
    public void NotificationsCanBeTurnedOff()
    {
        var prefs = From(new() { ["notify_thresholds"] = "0", ["notify_review"] = "0" });

        Assert.False(prefs.NotifyThresholds);
        Assert.False(prefs.NotifyReviewAtEnd);
    }

    // ── round trip ────────────────────────────────────────────────────────────

    [Fact]
    public void PreferencesSurviveASaveAndLoad()
    {
        var original = new TrackingPreferences(
            Statuses: [ActivityStatus.InProgress, ActivityStatus.Completed],
            NudgeInterval: TimeSpan.FromMinutes(45),
            NotifyThresholds: false,
            NotifyReviewAtEnd: true);

        var stored = original.ToConfig().ToDictionary(x => x.Key, x => x.Value);
        var loaded = From(stored);

        Assert.Equal(original.Statuses, loaded.Statuses);
        Assert.Equal(original.NudgeInterval, loaded.NudgeInterval);
        Assert.Equal(original.NotifyThresholds, loaded.NotifyThresholds);
        Assert.Equal(original.NotifyReviewAtEnd, loaded.NotifyReviewAtEnd);
    }

    [Fact]
    public void TurningTheNudgeOffSurvivesARoundTrip()
    {
        var stored = TrackingPreferences.Default with { NudgeInterval = null };

        var loaded = From(stored.ToConfig().ToDictionary(x => x.Key, x => x.Value));

        Assert.Null(loaded.NudgeInterval);
    }

    [Fact]
    public void WorkingHoursSurviveASaveAndLoad()
    {
        // The other half of the settings screen, checked the same way.
        var original = new WorkingHoursPolicy(
            HalfDay: new TimeSpan(4, 0, 0),
            FullDay: new TimeSpan(9, 0, 0),
            WorkingDays: [DayOfWeek.Monday, DayOfWeek.Saturday],
            DefaultStart: new TimeOnly(8, 45));

        var stored = original.ToConfig().ToDictionary(x => x.Key, x => x.Value);
        var loaded = WorkingHoursPolicy.FromConfig(k => stored.GetValueOrDefault(k));

        Assert.Equal(original.HalfDay, loaded.HalfDay);
        Assert.Equal(original.FullDay, loaded.FullDay);
        Assert.Equal(original.DefaultStart, loaded.DefaultStart);
        Assert.Equal(2, loaded.WorkingDays.Count);
    }
}
