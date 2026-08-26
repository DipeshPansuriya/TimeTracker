namespace TimeTracker.Core;

/// <summary>
/// Task states from brief §9. The brief asks for these to be configurable; for the pilot the
/// six documented values are fixed, and widening this to a user-defined set is tracked as
/// deferred rather than pretended away.
/// </summary>
public enum ActivityStatus
{
    NotStarted,
    InProgress,
    Completed,
    OnHold,
    Blocked,
    Cancelled
}

/// <summary>One entry in the day's timesheet (brief §6).</summary>
public sealed record Activity(
    ActivityId Id,
    string Title,
    string? Customer,
    DateTime Start,
    DateTime? End,
    ActivityStatus Status,
    string? Description = null,
    string? Notes = null)
{
    /// <summary>
    /// Duration is always derived from Start and End (brief §6), never stored — a stored
    /// duration is one more field that can disagree with its own endpoints.
    /// </summary>
    public TimeSpan DurationAt(DateTime now)
    {
        var end = End ?? now;
        return end > Start ? end - Start : TimeSpan.Zero;
    }

    public bool IsRunning => End is null;
}

/// <summary>
/// The day's activities and, crucially, the <i>context</i> carried between them.
/// </summary>
/// <remarks>
/// <para>
/// This is where brief §24 — "capture once, remember context, and ask only when required" —
/// actually lives. It is deliberately not in the UI: the widget, the notification scheduler
/// and the MCP tools all need the same answer to "what am I working on right now", and any
/// copy of that logic would drift from the others.
/// </para>
/// <para>Immutable, like <see cref="WorkDay"/>: every operation returns a new log.</para>
/// </remarks>
public sealed class ActivityLog
{
    public DateOnly Date { get; }
    public IReadOnlyList<Activity> Activities { get; }

    private ActivityLog(DateOnly date, IReadOnlyList<Activity> activities)
        => (Date, Activities) = (date, activities);

    public static ActivityLog Empty(DateOnly date) => new(date, []);

    /// <summary>The activity currently running, if any.</summary>
    public Activity? Current => Activities.LastOrDefault(a => a.IsRunning);

    /// <summary>
    /// The customer to assume when the user does not state one. This is the single value that
    /// stops the widget asking "which customer?" every hour.
    /// </summary>
    public string? CurrentCustomer => Activities.LastOrDefault(a => a.Customer is not null)?.Customer;

    /// <summary>
    /// Starts a task, closing whatever was running at the same instant.
    /// </summary>
    /// <param name="customer">
    /// Null means "unchanged" — the current customer is carried forward (§7). To record a task
    /// whose customer is genuinely unknown, use <see cref="StartWithoutCustomer"/>, so the
    /// end-of-day review can tell the two cases apart.
    /// </param>
    public ActivityLog Start(string title, string? customer, DateTime at)
        => StartInternal(title, customer ?? CurrentCustomer, at);

    /// <summary>
    /// Starts a task whose customer is genuinely not known yet. It will be reported by
    /// <see cref="MissingInformation"/> at the end of the day (§23).
    /// </summary>
    public ActivityLog StartWithoutCustomer(string title, DateTime at)
        => StartInternal(title, null, at);

    private ActivityLog StartInternal(string title, string? customer, DateTime at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var closed = CloseRunning(at, ActivityStatus.Completed);
        var next = new Activity(
            Id: ActivityId.Create(Date, closed.Count + 1),
            Title: title,
            Customer: customer,
            Start: at,
            End: null,
            Status: ActivityStatus.InProgress);

        return new ActivityLog(Date, [.. closed, next]);
    }

    /// <summary>
    /// The answer to the hourly nudge when nothing has changed. Deliberately a no-op that
    /// returns the same state — "continue" must never write a record.
    /// </summary>
    public ActivityLog ContinueCurrent() => this;

    /// <summary>Closes the running activity. Harmless when nothing is running.</summary>
    public ActivityLog CompleteCurrent(DateTime at, ActivityStatus status)
        => new(Date, CloseRunning(at, status));

    private List<Activity> CloseRunning(DateTime at, ActivityStatus status)
        => [.. Activities.Select(a => a.IsRunning ? a with { End = at, Status = status } : a)];

    /// <summary>
    /// Activities the end-of-day review must chase the user about (§23). Today that is a
    /// missing customer; the shape allows more checks without changing callers.
    /// </summary>
    public IEnumerable<Activity> MissingInformation()
        => Activities.Where(a => string.IsNullOrWhiteSpace(a.Customer));

    /// <summary>Customer-wise hours for the daily, weekly and monthly summaries (§10-§12).</summary>
    public IReadOnlyDictionary<string, TimeSpan> TotalsByCustomer(DateTime now)
        => Activities
            .Where(a => a.Customer is not null)
            .GroupBy(a => a.Customer!)
            .ToDictionary(g => g.Key, g => g.Aggregate(TimeSpan.Zero, (t, a) => t + a.DurationAt(now)));

    /// <summary>Task-wise hours for the same summaries.</summary>
    public IReadOnlyDictionary<string, TimeSpan> TotalsByTask(DateTime now)
        => Activities
            .GroupBy(a => a.Title)
            .ToDictionary(g => g.Key, g => g.Aggregate(TimeSpan.Zero, (t, a) => t + a.DurationAt(now)));

    public int CompletedCount => Activities.Count(a => a.Status == ActivityStatus.Completed);
    public int PendingCount => Activities.Count(a => a.Status is ActivityStatus.InProgress
                                                            or ActivityStatus.NotStarted
                                                            or ActivityStatus.OnHold);
}
