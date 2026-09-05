using TimeTracker.Core;

namespace TimeTracker.Core.Tests;

/// <summary>
/// Correcting what was recorded (brief §23 — the "[Edit]" the review screen offers).
/// People mistype the start time, forget to switch task, and name the wrong customer;
/// a timesheet that cannot be corrected gets abandoned or falsified.
/// </summary>
public class AmendTests
{
    private static DateTime At(int h, int m) => new(2026, 8, 26, h, m, 0);
    private static ActivityLog Empty => ActivityLog.Empty(new DateOnly(2026, 8, 26));

    private static ActivityLog WithOneClosed() => Empty
        .Start("API performance optimization", "Contoso", At(10, 0))
        .CompleteCurrent(At(12, 30), ActivityStatus.Completed);

    // ── activity details ──────────────────────────────────────────────────────

    [Fact]
    public void AmendingTheTitle_LeavesEverythingElseAlone()
    {
        var log = WithOneClosed();
        var id = log.Activities[0].Id;

        var after = log.Amend(id, title: "API latency investigation");

        Assert.Equal("API latency investigation", after.Activities[0].Title);
        Assert.Equal("Contoso", after.Activities[0].Customer);
        Assert.Equal(At(10, 0), after.Activities[0].Start);
        Assert.Equal(At(12, 30), after.Activities[0].End);
    }

    [Fact]
    public void AmendingTheCustomer_Works()
    {
        var log = WithOneClosed();

        var after = log.Amend(log.Activities[0].Id, customer: "ABC Logistics");

        Assert.Equal("ABC Logistics", after.Activities[0].Customer);
    }

    [Fact]
    public void FillingInAMissingCustomer_ClearsTheReviewGap()
    {
        // The §23 flow end to end: review flags it, user fixes it, gap disappears.
        var log = Empty.StartWithoutCustomer("Database migration", At(14, 0));
        Assert.Single(log.MissingInformation());

        var after = log.Amend(log.Activities[0].Id, customer: "ABC Logistics");

        Assert.Empty(after.MissingInformation());
    }

    [Fact]
    public void AmendingNothing_ChangesNothing()
    {
        var log = WithOneClosed();

        var after = log.Amend(log.Activities[0].Id);

        Assert.Equal(log.Activities[0], after.Activities[0]);
    }

    [Fact]
    public void AnUnknownActivityId_IsRejected()
        => Assert.Throws<ArgumentException>(() =>
            WithOneClosed().Amend(ActivityId.Create(new DateOnly(2026, 8, 26), 99), title: "x"));

    // ── times ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AmendingTheTimes_RecalculatesTheDuration()
    {
        var log = WithOneClosed();

        var after = log.Amend(log.Activities[0].Id, start: At(9, 30), end: At(11, 0));

        Assert.Equal(new TimeSpan(1, 30, 0), after.Activities[0].DurationAt(At(18, 0)));
    }

    [Fact]
    public void AmendingOnlyTheStart_KeepsTheExistingEnd()
    {
        var log = WithOneClosed();

        var after = log.Amend(log.Activities[0].Id, start: At(9, 0));

        Assert.Equal(At(9, 0), after.Activities[0].Start);
        Assert.Equal(At(12, 30), after.Activities[0].End);
    }

    [Fact]
    public void ABackwardsRange_IsRejectedRatherThanStored()
    {
        var log = WithOneClosed();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => log.Amend(log.Activities[0].Id, start: At(14, 0)));
    }

    [Fact]
    public void ARunningActivityKeepsRunningWhenItsStartIsCorrected()
    {
        // Correcting the start of the task in front of you must not accidentally close it.
        var log = Empty.Start("Database migration", "ABC Logistics", At(14, 0));

        var after = log.Amend(log.Activities[0].Id, start: At(13, 30));

        Assert.Equal(At(13, 30), after.Activities[0].Start);
        Assert.True(after.Activities[0].IsRunning);
        Assert.NotNull(after.Current);
    }

    [Fact]
    public void AmendingKeepsTheActivityId_SoSyncStillDeduplicates()
    {
        // The id is the idempotency key. A correction must update the HRMS record, not
        // create a second one alongside it.
        var log = WithOneClosed();
        var id = log.Activities[0].Id;

        var after = log.Amend(id, title: "Renamed", start: At(9, 0));

        Assert.Equal(id, after.Activities[0].Id);
    }

    [Fact]
    public void AmendingReordersByStartTime_SoTheDayStillReadsInOrder()
    {
        var log = Empty
            .Start("First", "Contoso", At(9, 0))
            .Start("Second", "Contoso", At(11, 0));

        // Move the second one to before the first.
        var after = log.Amend(log.Activities[1].Id, start: At(8, 0), end: At(8, 30));

        Assert.Equal("Second", after.Activities[0].Title);
    }

    [Fact]
    public void StatusCanBeCorrected()
    {
        var log = WithOneClosed();

        var after = log.Amend(log.Activities[0].Id, status: ActivityStatus.Blocked);

        Assert.Equal(ActivityStatus.Blocked, after.Activities[0].Status);
    }

    // ── the day's own times ───────────────────────────────────────────────────

    [Fact]
    public void TimeInCanBeCorrectedAfterTheFact()
    {
        // You typed 09:00 and actually started at 08:30. Every figure must follow.
        var policy = new WorkingHoursPolicy(
            new TimeSpan(4, 15, 0), new TimeSpan(8, 30, 0),
            [DayOfWeek.Wednesday], new TimeOnly(9, 0));

        var day = WorkDay.Started(At(9, 0)).AmendTimes(startedAt: At(8, 30));

        Assert.Equal(At(8, 30), day.StartedAt);
        Assert.Equal(new TimeSpan(9, 30, 0),
            WorkDayCalculator.Calculate(day, At(18, 0), policy).Worked);
    }

    [Fact]
    public void TimeOutCanBeCorrectedWithoutReopeningTheDay()
    {
        var day = WorkDay.Started(At(9, 0)).Completed(At(18, 0))
                         .AmendTimes(completedAt: At(17, 30));

        Assert.Equal(At(17, 30), day.CompletedAt);
        Assert.False(day.IsOpen);
    }

    [Fact]
    public void AmendingTheDayKeepsBreaksLocationsAndNote()
    {
        var day = WorkDay.Started(At(9, 0))
                         .AtLocation(WorkLocation.Client, At(9, 0))
                         .WithBreak(At(13, 0), At(13, 30))
                         .WithNote("Client visit.")
                         .Completed(At(18, 0))
                         .AmendTimes(startedAt: At(8, 45));

        Assert.Equal(At(8, 45), day.StartedAt);
        Assert.Single(day.Breaks);
        Assert.True(day.HadClientVisit);
        Assert.Equal("Client visit.", day.Note);
    }

    [Fact]
    public void ADayCannotBeMadeToEndBeforeItStarts()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => WorkDay.Started(At(9, 0)).Completed(At(18, 0))
                         .AmendTimes(startedAt: At(19, 0)));
}
