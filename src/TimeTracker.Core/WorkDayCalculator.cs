namespace TimeTracker.Core;

/// <summary>Everything the widget displays about the current day, derived in one pass.</summary>
public sealed record WorkDayStatus(
    TimeSpan Worked,
    TimeSpan Required,
    TimeSpan Remaining,
    TimeSpan Overtime,
    TimeSpan Shortfall,
    DayCompletion Completion,
    DateTime? ExpectedEnd,
    bool IsOnBreak,
    bool NeedsReview);

/// <summary>
/// The time engine. Worked time is <c>now − timeIn − breaks</c>: it is <i>derived</i>, never
/// <i>counted</i>.
/// </summary>
/// <remarks>
/// <para>
/// That distinction is the whole design. A stopwatch has running/paused state that must be
/// persisted, restored after a crash and repaired when wrong. A subtraction has none: if the
/// laptop sleeps for two hours, crashes, or loses power, reopening the widget still yields
/// the correct figure, because nothing was ever being accumulated.
/// </para>
/// <para>
/// The clock is deliberately wall-clock (<c>DateTime.Now</c>), not monotonic. A timesheet is
/// a claim about wall-clock times that HRMS will reconcile against a wall-clock punch, so the
/// endpoints must stay consistent with each other. The cost is that moving the system clock
/// moves the hours — detected here and surfaced via <see cref="WorkDayStatus.NeedsReview"/>
/// rather than silently absorbed.
/// </para>
/// <para>Pure and UI-agnostic by design, so it survives a change of desktop framework.</para>
/// </remarks>
public static class WorkDayCalculator
{
    public static WorkDayStatus Calculate(WorkDay day, DateTime now, WorkingHoursPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(policy);

        if (day.StartedAt is not { } startedAt)
            return NotStarted(policy);

        // A completed day is frozen: reopening the widget at 22:00 must not keep accruing
        // a day that was closed at 18:00.
        var upTo = day.CompletedAt ?? now;

        var elapsed = upTo - startedAt;
        var clockWentBackwards = elapsed < TimeSpan.Zero;

        var worked = clockWentBackwards
            ? TimeSpan.Zero
            : Max(elapsed - TotalBreaks(day.Breaks, startedAt, upTo), TimeSpan.Zero);

        var completion = worked >= policy.FullDay ? DayCompletion.FullDayComplete
                       : worked >= policy.HalfDay ? DayCompletion.HalfDayComplete
                       : DayCompletion.InProgress;

        var remaining = Max(policy.FullDay - worked, TimeSpan.Zero);
        var overtime = Max(worked - policy.FullDay, TimeSpan.Zero);

        // Shortfall is only meaningful once the day is closed — mid-afternoon you are not
        // "short", you are simply not finished.
        var shortfall = day.CompletedAt is not null ? remaining : TimeSpan.Zero;

        return new WorkDayStatus(
            Worked: worked,
            Required: policy.Required,
            Remaining: remaining,
            Overtime: overtime,
            Shortfall: shortfall,
            Completion: completion,
            ExpectedEnd: day.CompletedAt ?? now + remaining,
            IsOnBreak: day.CompletedAt is null && IsOnBreak(day.Breaks, now),
            NeedsReview: clockWentBackwards);
    }

    private static WorkDayStatus NotStarted(WorkingHoursPolicy policy) => new(
        Worked: TimeSpan.Zero,
        Required: policy.Required,
        Remaining: policy.Required,
        Overtime: TimeSpan.Zero,
        Shortfall: TimeSpan.Zero,
        Completion: DayCompletion.NotStarted,
        ExpectedEnd: null,
        IsOnBreak: false,
        NeedsReview: false);

    /// <summary>
    /// Total break time between <paramref name="from"/> and <paramref name="to"/>, counting
    /// overlapping breaks once. Overlaps are not hypothetical — a repaired record or a
    /// retried MCP write can produce two breaks covering the same minutes, and subtracting
    /// both would silently understate the day.
    /// </summary>
    private static TimeSpan TotalBreaks(
        IReadOnlyList<BreakInterval> breaks, DateTime from, DateTime to)
    {
        if (breaks.Count == 0) return TimeSpan.Zero;

        var clipped = breaks
            .Select(b => b.ClipTo(to))
            .Select(b => (Start: Later(b.Start, from), End: Earlier(b.End, to)))
            .Where(b => b.End > b.Start)
            .OrderBy(b => b.Start)
            .ToList();

        var total = TimeSpan.Zero;
        DateTime? mergedStart = null, mergedEnd = null;

        foreach (var (start, end) in clipped)
        {
            if (mergedEnd is { } me && start <= me)
            {
                if (end > me) mergedEnd = end;   // extend the run
                continue;
            }

            if (mergedStart is { } ms && mergedEnd is { } prevEnd) total += prevEnd - ms;
            (mergedStart, mergedEnd) = (start, end);
        }

        if (mergedStart is { } s && mergedEnd is { } e) total += e - s;
        return total;
    }

    private static bool IsOnBreak(IReadOnlyList<BreakInterval> breaks, DateTime now)
        => breaks.Any(b => b.Start <= now && (b.End is null || now < b.End));

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Earlier(DateTime a, DateTime b) => a < b ? a : b;
}
