namespace TimeTracker.Core;

/// <summary>How far through the day's required hours the user has got.</summary>
public enum DayCompletion
{
    /// <summary>The user has not answered the start prompt. Nothing is being counted.</summary>
    NotStarted,
    InProgress,
    HalfDayComplete,
    FullDayComplete
}

/// <summary>
/// A stretch of time excluded from worked hours. <paramref name="End"/> is null while the
/// user is still on the break — the widget deducts only up to the current moment, never a
/// notional full break.
/// </summary>
public sealed record BreakInterval(DateTime Start, DateTime? End)
{
    /// <summary>The break clipped to <paramref name="upTo"/>, for partial deduction.</summary>
    internal (DateTime Start, DateTime End) ClipTo(DateTime upTo)
        => (Start, End is { } e && e < upTo ? e : upTo);
}

/// <summary>
/// One day's record. Immutable: every mutation returns a new instance, so a day can be
/// recomputed from any point without worrying about who mutated it last.
/// </summary>
/// <remarks>
/// <see cref="StartedAt"/> and <see cref="CompletedAt"/> are full <see cref="DateTime"/>
/// values rather than times-of-day. That is deliberate — it makes a shift running past
/// midnight arithmetic rather than a special case.
/// </remarks>
public sealed class WorkDay
{
    public DateOnly Date { get; }

    /// <summary>Time In. Null until the user answers the morning prompt.</summary>
    public DateTime? StartedAt { get; }

    /// <summary>Time Out. Set by "Complete day"; null while the day is open.</summary>
    public DateTime? CompletedAt { get; }

    public IReadOnlyList<BreakInterval> Breaks { get; }

    private WorkDay(DateOnly date, DateTime? startedAt, DateTime? completedAt,
                    IReadOnlyList<BreakInterval> breaks)
        => (Date, StartedAt, CompletedAt, Breaks) = (date, startedAt, completedAt, breaks);

    /// <summary>A configured working day the user has not started yet.</summary>
    public static WorkDay NotStarted(DateOnly date) => new(date, null, null, []);

    /// <summary>The user answered the morning prompt with <paramref name="at"/>.</summary>
    public static WorkDay Started(DateTime at) => new(DateOnly.FromDateTime(at), at, null, []);

    public WorkDay WithBreak(DateTime start, DateTime end)
        => new(Date, StartedAt, CompletedAt, [.. Breaks, new BreakInterval(start, end)]);

    /// <summary>User pressed Break and has not returned.</summary>
    public WorkDay WithOpenBreak(DateTime start)
        => new(Date, StartedAt, CompletedAt, [.. Breaks, new BreakInterval(start, null)]);

    /// <summary>User pressed Complete day. Hours stop accruing from this point.</summary>
    public WorkDay Completed(DateTime at) => new(Date, StartedAt, at, Breaks);

    public bool IsOpen => StartedAt is not null && CompletedAt is null;
}
