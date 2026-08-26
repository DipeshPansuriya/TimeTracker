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

    /// <summary>
    /// Rebuilds a log from stored rows. Used by the repository only — it bypasses the
    /// <see cref="Start"/> rules on purpose, because persisted activities have already been
    /// through them and re-applying customer carry-forward on load would rewrite history.
    /// </summary>
    public static ActivityLog Rehydrate(DateOnly date, IReadOnlyList<Activity> activities)
        => new(date, [.. activities.OrderBy(a => a.Start)]);

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

    /// <summary>
    /// Corrects an already-recorded activity (brief §23's "[Edit]"). Any argument left null
    /// means "leave it alone"; pass an empty string to clear a customer or description.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The activity id is preserved deliberately. It is the idempotency key, so a correction
    /// must <i>update</i> the record HRMS already has rather than arrive as a second one
    /// beside it.
    /// </para>
    /// <para>
    /// People mistype start times, forget to switch task and pick the wrong customer. A
    /// timesheet that cannot be corrected is one that gets abandoned or, worse, guessed at
    /// on the last day of the month.
    /// </para>
    /// </remarks>
    public ActivityLog Amend(
        ActivityId id,
        string? title = null,
        string? customer = null,
        DateTime? start = null,
        DateTime? end = null,
        ActivityStatus? status = null,
        string? description = null,
        string? notes = null)
    {
        var index = IndexOf(id);
        var existing = Activities[index];

        var newStart = start ?? existing.Start;
        var newEnd = end ?? existing.End;

        if (newEnd is { } e && e <= newStart)
            throw new ArgumentOutOfRangeException(nameof(end),
                $"An activity cannot end at {e:HH:mm} having started at {newStart:HH:mm}.");

        if (title is not null && string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("An activity needs a title.", nameof(title));

        var amended = existing with
        {
            Title = title ?? existing.Title,
            Customer = Blank(customer) ? null : customer ?? existing.Customer,
            Start = newStart,
            End = newEnd,
            Status = status ?? existing.Status,
            Description = Blank(description) ? null : description ?? existing.Description,
            Notes = Blank(notes) ? null : notes ?? existing.Notes
        };

        var updated = Activities.ToList();
        updated[index] = amended;

        // Re-sorted: correcting a start time can move an activity earlier in the day, and a
        // list that no longer reads in order makes the review screen confusing.
        return new ActivityLog(Date, [.. updated.OrderBy(a => a.Start)]);
    }

    /// <summary>Empty string means "clear this"; null means "leave it".</summary>
    private static bool Blank(string? value) => value is not null && value.Length == 0;

    private int IndexOf(ActivityId id)
    {
        for (var i = 0; i < Activities.Count; i++)
            if (Activities[i].Id == id) return i;

        throw new ArgumentException($"No activity {id} on {Date:yyyy-MM-dd}.", nameof(id));
    }

    /// <summary>
    /// Inserts an already-finished activity without disturbing whatever is running.
    /// </summary>
    /// <remarks>
    /// This exists because recording past work and switching task are different intentions.
    /// An agent told "I worked on the API issue from 10 to 12:30" at half past six is
    /// describing the morning, not ending the task in front of them — and routing that
    /// through <see cref="Start"/> closed the running activity at 10:00, producing a record
    /// whose end preceded its own start. Observed in a live MCP session, not theorised.
    /// </remarks>
    public ActivityLog Record(
        string title, string? customer, DateTime start, DateTime end, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (end <= start)
            throw new ArgumentOutOfRangeException(nameof(end), "End must be after start.");

        var entry = new Activity(
            Id: ActivityId.Create(Date, Activities.Count + 1),
            Title: title,
            Customer: customer ?? CurrentCustomer,
            Start: start,
            End: end,
            Status: ActivityStatus.Completed,
            Description: description);

        return new ActivityLog(Date, [.. Activities, entry]);
    }

    /// <summary>
    /// Closes running activities at <paramref name="at"/>, never before their own start —
    /// a backwards end is corrupt data that every duration and rollup then has to defend
    /// against.
    /// </summary>
    private List<Activity> CloseRunning(DateTime at, ActivityStatus status)
        => [.. Activities.Select(a => a.IsRunning
            ? a with { End = at < a.Start ? a.Start : at, Status = status }
            : a)];

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
