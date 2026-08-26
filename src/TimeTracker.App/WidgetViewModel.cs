using System.ComponentModel;
using System.Runtime.CompilerServices;
using TimeTracker.Core;
using TimeTracker.Storage;

namespace TimeTracker.App;

/// <summary>
/// Everything the widget shows, recomputed from the engine on each tick.
/// </summary>
/// <remarks>
/// Holds no elapsed-time state of its own. Each <see cref="Refresh"/> asks
/// <see cref="WorkDayCalculator"/> for the answer given the current clock, so a missed tick,
/// a sleeping laptop or a restarted process all resolve correctly on the next pass.
/// </remarks>
public sealed class WidgetViewModel : INotifyPropertyChanged
{
    private readonly TimesheetRepository _repo;
    private readonly Func<DateTime> _clock;

    private WorkDay _day;
    private ActivityLog _log;
    private WorkingHoursPolicy _policy;

    private bool _announcedHalfDay;
    private bool _announcedFullDay;

    public WidgetViewModel(TimesheetRepository repo, Func<DateTime>? clock = null)
    {
        _repo = repo;
        _clock = clock ?? (() => DateTime.Now);

        var today = DateOnly.FromDateTime(_clock());
        _policy = LoadPolicy();
        _day = _repo.LoadDay(today) ?? WorkDay.NotStarted(today);
        _log = _repo.LoadActivities(today);

        Refresh();
    }

    /// <summary>Raised when a threshold is first crossed, so the shell can notify (§22).</summary>
    public event Action<string, string>? Notify;

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── state the view binds to ───────────────────────────────────────────────

    public string StateText { get; private set; } = "Not started";
    public string StateColour { get; private set; } = "#78909C";
    public string LocationText { get; private set; } = "—";

    public string TimeIn { get; private set; } = "—";
    public string Worked { get; private set; } = "00:00";
    public string Required { get; private set; } = "08:30";
    public string Remaining { get; private set; } = "08:30";
    public string ExpectedEnd { get; private set; } = "—";
    public string BalanceLabel { get; private set; } = "Remaining";
    public string BalanceValue { get; private set; } = "08:30";

    public double ProgressPercent { get; private set; }
    public double HalfDayPercent => _policy.FullDay.TotalMinutes == 0
        ? 0 : _policy.HalfDay.TotalMinutes / _policy.FullDay.TotalMinutes * 100;

    public string CurrentTaskTitle { get; private set; } = "No task started";
    public string CurrentTaskCustomer { get; private set; } = "—";
    public string CurrentTaskDuration { get; private set; } = "—";

    public string TodayTotal { get; private set; } = "00:00";
    public string WeekTotal { get; private set; } = "00:00";
    public string MonthTotal { get; private set; } = "00:00";

    public bool DayStarted => _day.StartedAt is not null;
    public bool DayComplete => _day.CompletedAt is not null;
    public bool CanTrack => DayStarted && !DayComplete;

    public WorkDay Day => _day;
    public ActivityLog Log => _log;
    public WorkingHoursPolicy Policy => _policy;

    // ── commands the shell calls ──────────────────────────────────────────────

    /// <summary>Answers the one morning question (§2).</summary>
    public void StartDay(DateTime at, WorkLocation location)
    {
        _day = WorkDay.Started(at).AtLocation(location, at);
        _repo.SaveDay(_day);
        Refresh();
    }

    public void StartTask(string title, string? customer, string? description)
    {
        var now = _clock();
        _log = _log.Start(title, customer, now);

        if (description is not null && _log.Current is { } current)
            _log = ActivityLog.Rehydrate(_log.Date,
                [.. _log.Activities.Select(a =>
                    a.Id == current.Id ? a with { Description = description } : a)]);

        _repo.SaveActivities(_log);
        Refresh();
    }

    public void ToggleBreak()
    {
        var now = _clock();
        var open = _day.Breaks.FirstOrDefault(b => b.End is null);

        // Rebuilt rather than mutated: WorkDay is immutable, and rebuilding keeps the
        // "one way to construct a day" rule that makes the engine easy to reason about.
        _day = open is null
            ? _day.WithOpenBreak(now)
            : Rebuild(_day, b => b.End is null ? b with { End = now } : b);

        _repo.SaveDay(_day);
        Refresh();
    }

    public void CompleteDay()
    {
        var now = _clock();
        if (_log.Current is not null)
            _log = _log.CompleteCurrent(now, ActivityStatus.Completed);

        _day = _day.Completed(now);
        _repo.SaveDay(_day);
        _repo.SaveActivities(_log);
        Refresh();
    }

    /// <summary>The unclosed-day prompt: nothing is ever guessed, the user states the time.</summary>
    public DateOnly? UnclosedDay() => _repo.FindUnclosedDayBefore(DateOnly.FromDateTime(_clock()));

    public void CloseUnclosedDay(DateOnly date, DateTime leftAt)
    {
        if (_repo.LoadDay(date) is { } stale) _repo.SaveDay(stale.Completed(leftAt));
    }

    // ── the tick ──────────────────────────────────────────────────────────────

    public void Refresh()
    {
        var now = _clock();
        var status = WorkDayCalculator.Calculate(_day, now, _policy);

        StateText = status.Completion == DayCompletion.NotStarted ? "Not started"
                  : DayComplete ? "Day complete"
                  : status.IsOnBreak ? "On break"
                  : "Working";

        StateColour = status.Completion == DayCompletion.NotStarted || DayComplete ? "#78909C"
                    : status.IsOnBreak ? "#FFB300"
                    : "#4CAF50";

        LocationText = (_day.PrimaryLocation(now) switch
        {
            WorkLocation.Office => "Office",
            WorkLocation.Home => "Home",
            WorkLocation.Client => "Client",
            _ => "—"
        });

        TimeIn = _day.StartedAt is { } s ? s.ToString("hh:mm tt") : "—";
        Worked = Hhmm(status.Worked);
        Required = Hhmm(status.Required);
        Remaining = Hhmm(status.Remaining);
        ExpectedEnd = status.ExpectedEnd is { } e ? e.ToString("hh:mm tt") : "—";

        // One row that changes meaning with the day. While there are hours left it counts
        // down to the finish; once they are made it switches to overtime, running or not.
        // Showing "expected end: now" to someone already past a full day says nothing.
        (BalanceLabel, BalanceValue) =
            status.Overtime > TimeSpan.Zero ? ("Overtime", "+" + Hhmm(status.Overtime))
            : DayComplete && status.Shortfall > TimeSpan.Zero ? ("Shortfall", "-" + Hhmm(status.Shortfall))
            : DayComplete ? ("Balance", "00:00")
            : ("Expected end", ExpectedEnd);

        ProgressPercent = _policy.FullDay.TotalMinutes == 0
            ? 0
            : Math.Min(100, status.Worked.TotalMinutes / _policy.FullDay.TotalMinutes * 100);

        var task = _log.Current ?? _log.Activities.LastOrDefault();
        CurrentTaskTitle = task?.Title ?? "No task started";
        CurrentTaskCustomer = task?.Customer ?? "—";
        CurrentTaskDuration = task is null ? "—" : Hhmm(task.DurationAt(now));

        TodayTotal = Hhmm(status.Worked);
        (WeekTotal, MonthTotal) = PeriodTotals(now, status.Worked);

        AnnounceThresholds(status);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <summary>
    /// Fires the §22 notifications, once each per day. The flags matter: the tick runs every
    /// 30 seconds, and without them crossing half day would notify 600 times an afternoon.
    /// </summary>
    private void AnnounceThresholds(WorkDayStatus status)
    {
        if (!_announcedHalfDay && status.Completion is DayCompletion.HalfDayComplete
                                                    or DayCompletion.FullDayComplete)
        {
            _announcedHalfDay = true;
            Notify?.Invoke("Half day complete",
                $"You need {Hhmm(status.Remaining)} more to complete full day.");
        }

        if (!_announcedFullDay && status.Completion == DayCompletion.FullDayComplete)
        {
            _announcedFullDay = true;
            Notify?.Invoke("Full day complete", "Anything beyond this counts as overtime.");
        }
    }

    private (string Week, string Month) PeriodTotals(DateTime now, TimeSpan todayWorked)
    {
        var today = DateOnly.FromDateTime(now);
        var weekStart = today.AddDays(-(((int)now.DayOfWeek + 6) % 7));   // week starts Monday
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        return (Hhmm(SumRange(weekStart, today, todayWorked)),
                Hhmm(SumRange(monthStart, today, todayWorked)));
    }

    private TimeSpan SumRange(DateOnly from, DateOnly today, TimeSpan todayWorked)
    {
        var total = todayWorked;
        foreach (var date in _repo.DatesBetween(from, today).Where(d => d != today))
            if (_repo.LoadDay(date) is { } d)
                total += WorkDayCalculator
                    .Calculate(d, d.CompletedAt ?? _clock(), _policy).Worked;
        return total;
    }

    private static WorkDay Rebuild(WorkDay day, Func<BreakInterval, BreakInterval> map)
    {
        var rebuilt = day.StartedAt is { } s ? WorkDay.Started(s) : WorkDay.NotStarted(day.Date);
        foreach (var b in day.Breaks.Select(map))
            rebuilt = b.End is { } e ? rebuilt.WithBreak(b.Start, e) : rebuilt.WithOpenBreak(b.Start);
        foreach (var spell in day.Locations)
            rebuilt = rebuilt.AtLocation(spell.Location, spell.Start);
        if (day.Note is not null) rebuilt = rebuilt.WithNote(day.Note);
        if (day.CompletedAt is { } c) rebuilt = rebuilt.Completed(c);
        return rebuilt;
    }

    // ── configuration (§1) ────────────────────────────────────────────────────

    private WorkingHoursPolicy LoadPolicy()
    {
        var half = _repo.GetConfig("half_day") ?? "04:15";
        var full = _repo.GetConfig("full_day") ?? "08:30";
        var start = _repo.GetConfig("default_start") ?? "09:00";
        var days = _repo.GetConfig("working_days") ?? "Monday,Tuesday,Wednesday,Thursday,Friday";

        return new WorkingHoursPolicy(
            HalfDay: TimeSpan.Parse(half),
            FullDay: TimeSpan.Parse(full),
            WorkingDays: [.. days.Split(',').Select(Enum.Parse<DayOfWeek>)],
            DefaultStart: TimeOnly.Parse(start));
    }

    public void SavePolicy(WorkingHoursPolicy policy)
    {
        _repo.SetConfig("half_day", policy.HalfDay.ToString(@"hh\:mm"));
        _repo.SetConfig("full_day", policy.FullDay.ToString(@"hh\:mm"));
        _repo.SetConfig("default_start", policy.DefaultStart.ToString("HH:mm"));
        _repo.SetConfig("working_days", string.Join(',', policy.WorkingDays));
        _policy = policy;
        Refresh();
    }

    private static string Hhmm(TimeSpan value)
        => $"{(int)value.TotalHours:00}:{Math.Abs(value.Minutes):00}";

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
