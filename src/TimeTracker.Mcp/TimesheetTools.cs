using System.ComponentModel;
using ModelContextProtocol.Server;
using TimeTracker.Core;
using TimeTracker.Storage;

namespace TimeTracker.Mcp;

/// <summary>
/// The tools an AI agent may call (brief §15–§18).
/// </summary>
/// <remarks>
/// <para>
/// Two rules shape this surface. First, <b>the agent works from context rather than
/// interrogating the user</b>: <see cref="GetCurrentContext"/> exists so "continue this for
/// another two hours" resolves without asking who the customer is. Second, <b>nothing here
/// can reach HRMS</b> — there is no submit tool, deliberately. The most an agent can do is
/// prepare the local record; a human presses the button.
/// </para>
/// <para>
/// Every write is idempotent on the activity id, so an agent that retries a call updates the
/// same record rather than creating a second one.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class TimesheetTools(TimesheetRepository repo)
{
    /// <summary>
    /// Serialises whole read-modify-write tool calls.
    /// </summary>
    /// <remarks>
    /// The repository already guards individual statements, but that is not enough here: each
    /// mutating tool loads the log, derives the next activity id from it, and saves it back.
    /// The server dispatches tool calls concurrently, so two overlapping calls would both read
    /// the same log and both allocate the same sequence — and because the activity id is the
    /// primary key, the second write would silently overwrite the first rather than fail.
    /// Observed, not theorised: two calls issued together both returned ACT-20260826-000001.
    /// </remarks>
    private static readonly Lock Gate = new();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    [McpServerTool(Name = "get_current_context")]
    [Description("What the user is working on right now: current task, customer and status, " +
                 "plus whether the day has been started. Call this before asking the user " +
                 "anything — it usually already contains the answer.")]
    public string GetCurrentContext()
    {
        var day = repo.LoadDay(Today);
        var log = repo.LoadActivities(Today);

        if (day?.StartedAt is null)
            return "The day has not been started. The user must set their start time in the " +
                   "widget before activities can be recorded.";

        var current = log.Current;
        var lines = new List<string>
        {
            $"Date: {Today:yyyy-MM-dd}",
            $"Started at: {day.StartedAt:HH\\:mm}",
            $"Day closed: {(day.CompletedAt is { } c ? c.ToString("HH\\:mm") : "no, still open")}",
            current is null
                ? "Current task: none running"
                : $"Current task: {current.Title} ({current.Id})",
            $"Current customer: {log.CurrentCustomer ?? "not set"}",
            $"Activities today: {log.Activities.Count} ({log.CompletedCount} complete)"
        };

        return string.Join('\n', lines);
    }

    [McpServerTool(Name = "get_day_summary")]
    [Description("Hours and task figures for a date (defaults to today): worked, required, " +
                 "overtime or shortfall, half/full day status, and customer-wise totals.")]
    public string GetDaySummary(
        [Description("Date as yyyy-MM-dd. Omit for today.")] string? date = null)
    {
        if (!TryDate(date, out var on, out var error)) return error;

        var day = repo.LoadDay(on);
        if (day is null) return $"No record for {on:yyyy-MM-dd}.";

        var summary = DaySummary.Build(day, repo.LoadActivities(on), Policy(), DateTime.Now);

        var customers = summary.ByCustomer.Count == 0
            ? "none recorded"
            : string.Join(", ", summary.ByCustomer.Select(kv => $"{kv.Key} {Hhmm(kv.Value)}"));

        return string.Join('\n',
            $"Date: {summary.Date:yyyy-MM-dd}",
            $"Time in: {summary.TimeIn:HH\\:mm}   Time out: " +
                $"{(summary.TimeOut is { } o ? o.ToString("HH\\:mm") : "still open")}",
            $"Worked: {Hhmm(summary.Worked)}   Required: {Hhmm(summary.Required)}",
            summary.Overtime > TimeSpan.Zero ? $"Overtime: +{Hhmm(summary.Overtime)}"
                : summary.Shortfall > TimeSpan.Zero ? $"Shortfall: -{Hhmm(summary.Shortfall)}"
                : "Balance: 00:00",
            $"Status: {summary.Completion}",
            $"Location: {summary.PrimaryLocation?.ToString() ?? "not recorded"}" +
                (summary.ClientVisit ? " (client visit)" : ""),
            $"Tasks: {summary.TotalTasks} total, {summary.CompletedTasks} complete",
            $"Customers: {customers}");
    }

    [McpServerTool(Name = "log_activity")]
    [Description("Record a completed activity with explicit start and end times. Use for " +
                 "work already finished, e.g. 'I worked on the API issue from 10 to 12:30'.")]
    public string LogActivity(
        [Description("Short task title.")] string title,
        [Description("Start time, e.g. 10:00 or 1000.")] string start,
        [Description("End time, e.g. 12:30 or 1230.")] string end,
        [Description("Customer or project. Omit to reuse the current one.")] string? customer = null,
        [Description("Optional longer description.")] string? description = null)
    {
        if (!TimeEntry.TryParse(start, out var from)) return $"'{start}' is not a time.";
        if (!TimeEntry.TryParse(end, out var to)) return $"'{end}' is not a time.";
        if (to <= from) return "End time must be after start time.";

        lock (Gate)
        {
            if (repo.LoadDay(Today)?.StartedAt is null)
                return "Cannot record: the day has not been started in the widget yet.";

            var log = repo.LoadActivities(Today);

            // Record, not Start: this describes work already done. Starting it would close
            // whatever is currently running at the historical time and leave that activity
            // ending before it began.
            log = log.Record(
                title,
                customer,
                DateTime.Today.Add(from.ToTimeSpan()),
                DateTime.Today.Add(to.ToTimeSpan()),
                description);

            var created = log.Activities[^1];
            repo.SaveActivities(log);

            var duration = to.ToTimeSpan() - from.ToTimeSpan();
            return $"Recorded {created.Id}: {title} for " +
                   $"{created.Customer ?? "no customer"}, {from:HH\\:mm}-{to:HH\\:mm} " +
                   $"({Hhmm(duration)}). Stored locally only.";
        }
    }

    [McpServerTool(Name = "start_activity")]
    [Description("Start a new task now, closing whatever was running. Omit the customer to " +
                 "carry the current one forward.")]
    public string StartActivity(
        [Description("Short task title.")] string title,
        [Description("Customer or project. Omit to keep the current one.")] string? customer = null,
        [Description("Optional longer description.")] string? description = null)
    {
        lock (Gate)
        {
            if (repo.LoadDay(Today)?.StartedAt is null)
                return "Cannot start: the day has not been started in the widget yet.";

            var log = repo.LoadActivities(Today);
            var previous = log.Current;

            log = log.Start(title, customer, DateTime.Now);
            var created = log.Current!;

            if (description is not null)
                log = ActivityLog.Rehydrate(log.Date, [.. log.Activities.Select(a =>
                    a.Id == created.Id ? a with { Description = description } : a)]);

            repo.SaveActivities(log);

            var closed = previous is null ? "" : $"Closed '{previous.Title}'. ";
            return $"{closed}Started {created.Id}: {title} for " +
                   $"{created.Customer ?? "no customer"} at {DateTime.Now:HH\\:mm}.";
        }
    }

    [McpServerTool(Name = "complete_current_activity")]
    [Description("Close the running task. Status may be Completed, OnHold, Blocked or Cancelled.")]
    public string CompleteCurrentActivity(
        [Description("Completed, OnHold, Blocked or Cancelled. Defaults to Completed.")]
        string status = "Completed")
    {
        var log = repo.LoadActivities(Today);
        if (log.Current is not { } current) return "Nothing is running.";

        if (!Enum.TryParse<ActivityStatus>(status, ignoreCase: true, out var parsed))
            return $"'{status}' is not a status. Use Completed, OnHold, Blocked or Cancelled.";

        repo.SaveActivities(log.CompleteCurrent(DateTime.Now, parsed));
        return $"Closed {current.Id}: {current.Title} as {parsed} at {DateTime.Now:HH\\:mm}.";
    }

    [McpServerTool(Name = "amend_activity")]
    [Description("Correct an already-recorded activity: its title, customer, times or " +
                 "status. Omit anything you do not want to change. Use list_activities " +
                 "first to get the activity id.")]
    public string AmendActivity(
        [Description("Activity id, e.g. ACT-20260826-000001.")] string activityId,
        [Description("New task title. Omit to leave it.")] string? title = null,
        [Description("New customer. Omit to leave it.")] string? customer = null,
        [Description("New start time, e.g. 10:00. Omit to leave it.")] string? start = null,
        [Description("New end time, e.g. 12:30. Omit to leave it.")] string? end = null,
        [Description("Completed, InProgress, OnHold, Blocked or Cancelled. Omit to leave it.")]
        string? status = null)
    {
        if (!ActivityId.TryParse(activityId, out var id))
            return $"'{activityId}' is not an activity id. They look like ACT-20260826-000001.";

        DateTime? newStart = null, newEnd = null;
        if (start is not null)
        {
            if (!TimeEntry.TryParse(start, out var s)) return $"'{start}' is not a time.";
            newStart = DateTime.Today.Add(s.ToTimeSpan());
        }
        if (end is not null)
        {
            if (!TimeEntry.TryParse(end, out var e)) return $"'{end}' is not a time.";
            newEnd = DateTime.Today.Add(e.ToTimeSpan());
        }

        ActivityStatus? newStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<ActivityStatus>(status, ignoreCase: true, out var parsed))
                return $"'{status}' is not a status. Use Completed, InProgress, OnHold, " +
                       "Blocked or Cancelled.";
            newStatus = parsed;
        }

        lock (Gate)
        {
            var log = repo.LoadActivities(Today);

            try
            {
                log = log.Amend(id, title, customer, newStart, newEnd, newStatus);
            }
            catch (ArgumentException ex)
            {
                // Surfaced as text, not an exception: the agent should be able to explain
                // the problem to the user and retry, not fail the whole conversation.
                return $"Could not amend {activityId}: {ex.Message}";
            }

            repo.SaveActivities(log);
            var amended = log.Activities.First(a => a.Id == id);

            return $"Amended {id}: {amended.Title} for " +
                   $"{amended.Customer ?? "no customer"}, {amended.Start:HH\\:mm}-" +
                   $"{(amended.End is { } e2 ? e2.ToString("HH\\:mm") : "running")}, " +
                   $"{amended.Status}. The id is unchanged, so this updates the existing " +
                   "record rather than adding another.";
        }
    }

    [McpServerTool(Name = "get_period_summary")]
    [Description("Weekly or monthly totals: required and worked hours, balance, full/half " +
                 "days, WFH and client-visit days, and customer-wise hours.")]
    public string GetPeriodSummary(
        [Description("'week' or 'month'. Defaults to week.")] string period = "week")
    {
        var today = Today;
        var (from, label) = period.Equals("month", StringComparison.OrdinalIgnoreCase)
            ? (new DateOnly(today.Year, today.Month, 1), "Month")
            : (today.AddDays(-(((int)DateTime.Now.DayOfWeek + 6) % 7)), "Week");

        var policy = Policy();
        var days = repo.DatesBetween(from, today)
            .Select(d => repo.LoadDay(d))
            .Where(d => d is not null)
            .Select(d => DaySummary.Build(d!, repo.LoadActivities(d!.Date), policy,
                                          d.CompletedAt ?? DateTime.Now))
            .ToList();

        if (days.Count == 0) return $"No days recorded from {from:yyyy-MM-dd}.";

        var p = PeriodSummary.Build(days, policy);
        var customers = p.ByCustomer.Count == 0
            ? "none"
            : string.Join(", ", p.ByCustomer.OrderByDescending(kv => kv.Value)
                                            .Select(kv => $"{kv.Key} {Hhmm(kv.Value)}"));

        return string.Join('\n',
            $"{label}: {p.From:yyyy-MM-dd} to {p.To:yyyy-MM-dd} ({p.WorkingDays} days)",
            $"Required: {Hhmm(p.RequiredHours)}   Worked: {Hhmm(p.WorkedHours)}",
            $"Balance: {(p.Balance < TimeSpan.Zero ? "-" : "+")}{Hhmm(p.Balance)}",
            $"Full days: {p.FullDays}   Half days: {p.HalfDays}",
            $"Office: {p.OfficeDays}   Home: {p.WfhDays}   Client visits: {p.ClientVisitDays}",
            $"Tasks: {p.TotalTasks} ({p.CompletedTasks} complete)",
            $"Customers: {customers}");
    }

    [McpServerTool(Name = "list_activities")]
    [Description("All activities for a date, with ids, times, durations and status. Use to " +
                 "check what is already recorded before adding more.")]
    public string ListActivities(
        [Description("Date as yyyy-MM-dd. Omit for today.")] string? date = null)
    {
        if (!TryDate(date, out var on, out var error)) return error;

        var log = repo.LoadActivities(on);
        if (log.Activities.Count == 0) return $"No activities recorded for {on:yyyy-MM-dd}.";

        var now = DateTime.Now;
        var rows = log.Activities.Select(a =>
            $"{a.Id}  {a.Start:HH\\:mm}-" +
            $"{(a.End is { } e ? e.ToString("HH\\:mm") : "running")}  " +
            $"{Hhmm(a.DurationAt(now))}  {a.Status,-11} {a.Title} " +
            $"[{a.Customer ?? "NO CUSTOMER"}]");

        var gaps = log.MissingInformation().ToList();
        var footer = gaps.Count == 0
            ? ""
            : $"\n\nMissing customer on: {string.Join(", ", gaps.Select(g => g.Title))}";

        return string.Join('\n', rows) + footer;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the same configuration the widget uses, so the two can never disagree about what
    /// a full day is.
    /// </summary>
    private WorkingHoursPolicy Policy() => new(
        HalfDay: TimeSpan.Parse(repo.GetConfig("half_day") ?? "04:15"),
        FullDay: TimeSpan.Parse(repo.GetConfig("full_day") ?? "08:30"),
        WorkingDays: [.. (repo.GetConfig("working_days")
                          ?? "Monday,Tuesday,Wednesday,Thursday,Friday")
                         .Split(',').Select(Enum.Parse<DayOfWeek>)],
        DefaultStart: TimeOnly.Parse(repo.GetConfig("default_start") ?? "09:00"));

    private static bool TryDate(string? input, out DateOnly date, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) { date = Today; return true; }

        if (DateOnly.TryParse(input, out date)) return true;

        error = $"'{input}' is not a date. Use yyyy-MM-dd.";
        return false;
    }

    private static string Hhmm(TimeSpan v)
        => $"{Math.Abs((int)v.TotalHours):00}:{Math.Abs(v.Minutes):00}";
}
