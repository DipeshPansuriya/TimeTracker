using TimeTracker.Core;

namespace TimeTracker.Core.Tests;

/// <summary>
/// The "capture once, remember context, ask only when required" rule (brief §24) is not a UI
/// behaviour — it is this class. These tests pin the behaviour that stops the widget becoming
/// the manual timesheet the brief explicitly rules out.
/// </summary>
public class ActivityLogTests
{
    private static DateTime At(int h, int m) => new(2026, 8, 26, h, m, 0);
    private static ActivityLog Empty => ActivityLog.Empty(new DateOnly(2026, 8, 26));

    // ── activity identity (§20) ────────────────────────────────────────────────

    [Fact]
    public void ActivityId_UsesTheDocumentedFormat()
    {
        var id = ActivityId.Create(new DateOnly(2026, 8, 26), 123);

        Assert.Equal("ACT-20260826-000123", id.Value);
    }

    [Fact]
    public void ActivityId_RoundTripsThroughParsing()
    {
        var id = ActivityId.Create(new DateOnly(2026, 8, 26), 123);

        Assert.Equal(id, ActivityId.Parse(id.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ACT-20260826")]
    [InlineData("ACT-202608-000123")]
    [InlineData("XYZ-20260826-000123")]
    [InlineData("ACT-20260826-12")]
    public void ActivityId_RejectsMalformedValues(string bad)
        => Assert.False(ActivityId.TryParse(bad, out _));

    [Fact]
    public void ActivityIds_AreUniqueWithinADay()
    {
        var log = Empty
            .Start("First", "Galaxy", At(9, 30))
            .Start("Second", "Galaxy", At(10, 30))
            .Start("Third", "Galaxy", At(11, 30));

        Assert.Equal(3, log.Activities.Select(a => a.Id).Distinct().Count());
    }

    // ── §7: continuation without re-asking ────────────────────────────────────

    [Fact]
    public void StartingATask_MakesItCurrent()
    {
        var log = Empty.Start("API performance optimization", "Galaxy", At(10, 0));

        Assert.NotNull(log.Current);
        Assert.Equal("API performance optimization", log.Current!.Title);
        Assert.Equal("Galaxy", log.Current.Customer);
        Assert.Equal(ActivityStatus.InProgress, log.Current.Status);
    }

    [Fact]
    public void SwitchingTask_WithoutNamingACustomer_CarriesTheCurrentOneForward()
    {
        // Brief §7: on a new task ask for title and description, but "Customer, only if
        // it has changed". Passing null must mean "same customer", not "no customer".
        var log = Empty
            .Start("API performance optimization", "Galaxy", At(10, 0))
            .Start("Database migration", customer: null, At(12, 30));

        Assert.Equal("Galaxy", log.Current!.Customer);
    }

    [Fact]
    public void SwitchingTask_WithADifferentCustomer_UsesTheNewOne()
    {
        var log = Empty
            .Start("API performance optimization", "Galaxy", At(10, 0))
            .Start("Onsite workshop", "ABC Logistics", At(12, 30));

        Assert.Equal("ABC Logistics", log.Current!.Customer);
    }

    [Fact]
    public void SwitchingTask_ClosesThePreviousOneAtTheSameMoment()
    {
        // No gap and no overlap: the previous task ends exactly when the next begins,
        // otherwise the day's task durations stop reconciling with worked time.
        var log = Empty
            .Start("API performance optimization", "Galaxy", At(10, 0))
            .Start("Database migration", null, At(12, 30));

        var previous = log.Activities[0];
        Assert.Equal(At(12, 30), previous.End);
        Assert.Equal(ActivityStatus.Completed, previous.Status);
        Assert.Equal(At(12, 30), log.Current!.Start);
    }

    [Fact]
    public void ContinuingTheCurrentTask_CreatesNothingNew()
    {
        // The hourly nudge answered with "yes, continue" must be a no-op on the record.
        var log = Empty.Start("API performance optimization", "Galaxy", At(10, 0));

        var after = log.ContinueCurrent();

        Assert.Single(after.Activities);
        Assert.Equal(log.Current!.Id, after.Current!.Id);
    }

    // ── durations ─────────────────────────────────────────────────────────────

    [Fact]
    public void Duration_OfAClosedActivity_IsEndMinusStart()
    {
        var log = Empty
            .Start("API performance optimization", "Galaxy", At(10, 0))
            .Start("Next thing", null, At(12, 30));

        Assert.Equal(new TimeSpan(2, 30, 0), log.Activities[0].DurationAt(At(18, 0)));
    }

    [Fact]
    public void Duration_OfTheRunningActivity_GrowsWithTheClock()
    {
        var log = Empty.Start("Database migration", "ABC Logistics", At(14, 0));

        Assert.Equal(new TimeSpan(2, 15, 0), log.Current!.DurationAt(At(16, 15)));
    }

    // ── completion ────────────────────────────────────────────────────────────

    [Fact]
    public void CompletingTheCurrentTask_LeavesNothingRunning()
    {
        var log = Empty
            .Start("API performance optimization", "Galaxy", At(10, 0))
            .CompleteCurrent(At(12, 30), ActivityStatus.Completed);

        Assert.Null(log.Current);
        Assert.Equal(ActivityStatus.Completed, log.Activities[0].Status);
    }

    [Fact]
    public void CompletingWhenNothingIsRunning_IsHarmless()
    {
        // The MCP surface and the UI can both send this; it must not throw.
        var log = Empty.CompleteCurrent(At(12, 30), ActivityStatus.Completed);

        Assert.Empty(log.Activities);
    }

    [Fact]
    public void ATaskCanBePutOnHoldRatherThanCompleted()
    {
        var log = Empty
            .Start("Blocked on vendor", "Galaxy", At(10, 0))
            .CompleteCurrent(At(11, 0), ActivityStatus.OnHold);

        Assert.Equal(ActivityStatus.OnHold, log.Activities[0].Status);
    }

    // ── end-of-day review (§23) ───────────────────────────────────────────────

    [Fact]
    public void ActivitiesMissingACustomer_AreReportedForReview()
    {
        // Brief §23 shows exactly this: "Missing Information: Customer for Task 3".
        var log = Empty
            .Start("Galaxy API optimization", "Galaxy", At(10, 0))
            .Start("Customer meeting", null, At(12, 0))
            .StartWithoutCustomer("Database migration", At(14, 0));

        var gaps = log.MissingInformation().ToList();

        Assert.Single(gaps);
        Assert.Equal("Database migration", gaps[0].Title);
    }

    [Fact]
    public void ACleanDayReportsNoGaps()
    {
        var log = Empty.Start("Galaxy API optimization", "Galaxy", At(10, 0));

        Assert.Empty(log.MissingInformation());
    }

    // ── rollups for the summaries (§10-§12) ───────────────────────────────────

    [Fact]
    public void CustomerTotals_AggregateAcrossTasks()
    {
        var log = Empty
            .Start("Task A", "Galaxy", At(9, 0))
            .Start("Task B", "ABC Logistics", At(10, 0))
            .Start("Task C", "Galaxy", At(11, 0))
            .CompleteCurrent(At(12, 0), ActivityStatus.Completed);

        var totals = log.TotalsByCustomer(At(12, 0));

        Assert.Equal(new TimeSpan(2, 0, 0), totals["Galaxy"]);
        Assert.Equal(new TimeSpan(1, 0, 0), totals["ABC Logistics"]);
    }

    // ── recording past work (found in a live MCP session) ─────────────────────

    [Fact]
    public void RecordingPastWork_DoesNotCloseTheRunningTask()
    {
        // Telling an agent "I worked on the API issue from 10 to 12:30" at 18:28 describes
        // the morning. Routing it through Start() closed the running task at 10:00 and
        // produced a record whose end preceded its own start.
        var log = Empty
            .Start("Database migration", "ABC Logistics", At(18, 28))
            .Record("API performance issue", "Galaxy", At(10, 0), At(12, 30));

        Assert.Equal("Database migration", log.Current!.Title);
        Assert.True(log.Current.IsRunning);
        Assert.Equal(2, log.Activities.Count);
    }

    [Fact]
    public void ARecordedActivityIsCompleteWithItsOwnTimes()
    {
        var log = Empty.Record("API performance issue", "Galaxy", At(10, 0), At(12, 30));

        var entry = log.Activities[0];
        Assert.Equal(ActivityStatus.Completed, entry.Status);
        Assert.Equal(new TimeSpan(2, 30, 0), entry.DurationAt(At(18, 0)));
    }

    [Fact]
    public void RecordingCarriesTheCurrentCustomerWhenNoneIsGiven()
    {
        var log = Empty
            .Start("Something", "Galaxy", At(9, 0))
            .Record("Earlier thing", null, At(8, 0), At(8, 30));

        Assert.Equal("Galaxy", log.Activities[1].Customer);
    }

    [Fact]
    public void RecordingABackwardsRangeIsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => Empty.Record("Nope", "Galaxy", At(12, 0), At(10, 0)));

    [Fact]
    public void ClosingATaskEarlierThanItsStart_ClampsRatherThanCorrupting()
    {
        // Defensive: whatever the caller passes, an activity must never end before it began.
        var log = Empty
            .Start("Database migration", "ABC Logistics", At(18, 28))
            .CompleteCurrent(At(10, 0), ActivityStatus.Completed);

        Assert.Equal(At(18, 28), log.Activities[0].End);
        Assert.Equal(TimeSpan.Zero, log.Activities[0].DurationAt(At(19, 0)));
    }
}
