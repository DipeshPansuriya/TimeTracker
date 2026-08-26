using TimeTracker.Core;

namespace TimeTracker.Core.Tests;

/// <summary>
/// The worked examples in the requirements brief are executable acceptance tests.
/// Where a test corresponds to a numbered section of the brief, it says so — if one of
/// these fails, the product disagrees with the document it was commissioned from.
/// </summary>
public class WorkDayCalculatorTests
{
    /// <summary>Standard policy from the brief: half day 04:15, full day 08:30.</summary>
    private static readonly WorkingHoursPolicy Policy = new(
        HalfDay: new TimeSpan(4, 15, 0),
        FullDay: new TimeSpan(8, 30, 0),
        WorkingDays: [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                      DayOfWeek.Thursday, DayOfWeek.Friday],
        DefaultStart: new TimeOnly(9, 0));

    private static DateTime At(int hour, int minute) => new(2026, 8, 26, hour, minute, 0);

    // ── brief §3 ────────────────────────────────────────────────────────────────

    [Fact]
    public void Brief_Section3_MidAfternoon_MatchesTheDocumentedExample()
    {
        // Time In 09:15, current time 15:00 → worked 05:45, remaining 02:45,
        // expected end 17:45, status "Half Day Completed".
        var day = WorkDay.Started(At(9, 15));

        var status = WorkDayCalculator.Calculate(day, now: At(15, 0), Policy);

        Assert.Equal(new TimeSpan(5, 45, 0), status.Worked);
        Assert.Equal(new TimeSpan(8, 30, 0), status.Required);
        Assert.Equal(new TimeSpan(2, 45, 0), status.Remaining);
        Assert.Equal(At(17, 45), status.ExpectedEnd);
        Assert.Equal(DayCompletion.HalfDayComplete, status.Completion);
    }

    // ── brief §4 ────────────────────────────────────────────────────────────────

    [Fact]
    public void Brief_Section4_LeavingEarly_ReportsTheShortfall()
    {
        // Required full day 08:30, actual worked 08:24 → shortfall 00:06.
        // 09:10 → 18:04 is 08:54; a 30 minute break brings it to 08:24.
        var day = WorkDay.Started(At(9, 10))
                         .WithBreak(At(13, 30), At(14, 0))
                         .Completed(At(18, 4));

        var status = WorkDayCalculator.Calculate(day, now: At(18, 4), Policy);

        Assert.Equal(new TimeSpan(8, 24, 0), status.Worked);
        Assert.Equal(new TimeSpan(0, 6, 0), status.Shortfall);
        Assert.Equal(TimeSpan.Zero, status.Overtime);
    }

    // ── brief §10 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Brief_Section10_FullDayWithOvertime_MatchesTheDocumentedSummary()
    {
        // Time In 09:10, Time Out 18:00, required 08:30, worked 08:50, overtime 00:20.
        var day = WorkDay.Started(At(9, 10)).Completed(At(18, 0));

        var status = WorkDayCalculator.Calculate(day, now: At(18, 0), Policy);

        Assert.Equal(new TimeSpan(8, 50, 0), status.Worked);
        Assert.Equal(new TimeSpan(0, 20, 0), status.Overtime);
        Assert.Equal(TimeSpan.Zero, status.Shortfall);
        Assert.Equal(DayCompletion.FullDayComplete, status.Completion);
    }

    // ── breaks ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Breaks_AreExcludedFromWorkedTime()
    {
        var day = WorkDay.Started(At(9, 0)).WithBreak(At(13, 0), At(13, 45));

        var status = WorkDayCalculator.Calculate(day, now: At(18, 0), Policy);

        // 09:00 → 18:00 is 9h; less a 45 minute break = 8h15.
        Assert.Equal(new TimeSpan(8, 15, 0), status.Worked);
    }

    [Fact]
    public void AnOpenBreak_IsDeductedUpToNow_NotBeyondIt()
    {
        // User pressed Break at 13:00 and has not returned. At 13:20 exactly
        // 20 minutes should be deducted — not the whole notional break.
        var day = WorkDay.Started(At(9, 0)).WithOpenBreak(At(13, 0));

        var status = WorkDayCalculator.Calculate(day, now: At(13, 20), Policy);

        Assert.Equal(new TimeSpan(4, 0, 0), status.Worked);
        Assert.True(status.IsOnBreak);
    }

    [Fact]
    public void OverlappingBreaks_AreNotDoubleCounted()
    {
        // Defensive: a repaired or duplicated record must not subtract twice.
        var day = WorkDay.Started(At(9, 0))
                         .WithBreak(At(13, 0), At(13, 30))
                         .WithBreak(At(13, 15), At(13, 45));

        var status = WorkDayCalculator.Calculate(day, now: At(18, 0), Policy);

        // Union of the two breaks is 13:00–13:45 = 45 minutes, not 60.
        Assert.Equal(new TimeSpan(8, 15, 0), status.Worked);
    }

    // ── day not started ─────────────────────────────────────────────────────────

    [Fact]
    public void BeforeTheUserAnswersTheStartPrompt_NothingIsCounted()
    {
        // The widget never invents a start time. No answer means no day.
        var day = WorkDay.NotStarted(new DateOnly(2026, 8, 26));

        var status = WorkDayCalculator.Calculate(day, now: At(11, 0), Policy);

        Assert.Equal(DayCompletion.NotStarted, status.Completion);
        Assert.Equal(TimeSpan.Zero, status.Worked);
        Assert.Null(status.ExpectedEnd);
    }

    // ── clock safety (failure path from the workflow diagram) ───────────────────

    [Fact]
    public void IfTheSystemClockMovesBackwards_WorkedIsClampedToZero_NotNegative()
    {
        var day = WorkDay.Started(At(9, 0));

        var status = WorkDayCalculator.Calculate(day, now: At(8, 0), Policy);

        Assert.Equal(TimeSpan.Zero, status.Worked);
        Assert.True(status.NeedsReview);
    }

    [Fact]
    public void ACompletedDay_IgnoresTheClockAdvancingPastTimeOut()
    {
        // Reopening the widget at 22:00 must not keep accruing a day closed at 18:00.
        var day = WorkDay.Started(At(9, 10)).Completed(At(18, 0));

        var status = WorkDayCalculator.Calculate(day, now: At(22, 0), Policy);

        Assert.Equal(new TimeSpan(8, 50, 0), status.Worked);
    }

    // ── progression through the day ─────────────────────────────────────────────

    [Theory]
    [InlineData(10, 0, DayCompletion.InProgress)]      // 1h00 worked
    [InlineData(13, 15, DayCompletion.HalfDayComplete)] // 4h15 — exactly half day
    [InlineData(17, 30, DayCompletion.FullDayComplete)] // 8h30 — exactly full day
    public void CompletionCrossesAtTheConfiguredThresholds(int h, int m, DayCompletion expected)
    {
        var day = WorkDay.Started(At(9, 0));

        var status = WorkDayCalculator.Calculate(day, now: At(h, m), Policy);

        Assert.Equal(expected, status.Completion);
    }

    [Fact]
    public void RemainingReachesZeroAndDoesNotGoNegative()
    {
        var day = WorkDay.Started(At(9, 0));

        var status = WorkDayCalculator.Calculate(day, now: At(19, 0), Policy);

        Assert.Equal(TimeSpan.Zero, status.Remaining);
        Assert.Equal(new TimeSpan(1, 30, 0), status.Overtime);
    }

    // ── working days (§1) ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-08-26", true)]  // Wednesday
    [InlineData("2026-08-29", false)] // Saturday
    [InlineData("2026-08-30", false)] // Sunday
    public void WorkingDaysComeFromConfiguration_NotFromHardCodedWeekends(string date, bool expected)
        => Assert.Equal(expected, Policy.IsWorkingDay(DateOnly.Parse(date)));
}
