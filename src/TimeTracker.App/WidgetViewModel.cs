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
    private TrackingPreferences _prefs;
    private DateTime _lastNudge;

    private bool _announcedHalfDay;
    private bool _announcedFullDay;

    public WidgetViewModel(TimesheetRepository repo, Func<DateTime>? clock = null)
    {
        _repo = repo;
        _clock = clock ?? (() => DateTime.Now);

        var today = DateOnly.FromDateTime(_clock());
        _policy = LoadPolicy();
        _prefs = TrackingPreferences.FromConfig(_repo.GetConfig);
        _lastNudge = _clock();
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
    public TrackingPreferences Preferences => _prefs;

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

    /// <summary>
    /// Corrects a recorded activity (brief S23's "[Edit]"). The activity id is preserved, so
    /// a correction updates the HRMS record rather than arriving beside it as a duplicate.
    /// </summary>
    public void AmendActivity(
        ActivityId id, string title, string? customer, string? description,
        TimeOnly start, TimeOnly? end, ActivityStatus status)
    {
        var date = _log.Date.ToDateTime(TimeOnly.MinValue);

        _log = _log.Amend(
            id,
            title: title,
            customer: customer,
            start: date.Add(start.ToTimeSpan()),
            end: end is { } e ? date.Add(e.ToTimeSpan()) : null,
            status: status,
            description: description);

        _repo.SaveActivities(_log);
        Refresh();
    }

    /// <summary>Corrects the day's Time In / Time Out after the fact.</summary>
    public void AmendDayTimes(TimeOnly? timeIn, TimeOnly? timeOut)
    {
        var date = _day.Date.ToDateTime(TimeOnly.MinValue);

        _day = _day.AmendTimes(
            startedAt: timeIn is { } i ? date.Add(i.ToTimeSpan()) : null,
            completedAt: timeOut is { } o ? date.Add(o.ToTimeSpan()) : null);

        _repo.SaveDay(_day);
        Refresh();
    }

    /// <summary>The unclosed-day prompt: nothing is ever guessed, the user states the time.</summary>
    public DateOnly? UnclosedDay() => _repo.FindUnclosedDayBefore(DateOnly.FromDateTime(_clock()));

    public void CloseUnclosedDay(DateOnly date, DateTime leftAt)
    {
        if (_repo.LoadDay(date) is { } stale) _repo.SaveDay(stale.Completed(leftAt));
    }

    // ── reporting (S10-S12) ───────────────────────────────────────────────────

    /// <summary>The days making up a period, most recent first.</summary>
    /// <remarks>
    /// Each day is summarised at <i>its own</i> close time, not at now. Summarising a
    /// finished Tuesday against the current clock would keep growing its hours all week.
    /// </remarks>
    public IReadOnlyList<DaySummary> DaysIn(DateOnly from, DateOnly to)
    {
        var days = new List<DaySummary>();

        foreach (var date in _repo.DatesBetween(from, to))
        {
            var day = date == _day.Date ? _day : _repo.LoadDay(date);
            if (day is null) continue;

            var log = date == _log.Date ? _log : _repo.LoadActivities(date);
            var asOf = day.CompletedAt ?? (date == _day.Date ? _clock() : _clock());

            days.Add(DaySummary.Build(day, log, _policy, asOf));
        }

        days.Reverse();
        return days;
    }

    public (DateOnly From, DateOnly To) WeekOf(DateOnly date)
    {
        var monday = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        return (monday, monday.AddDays(6));
    }

    public (DateOnly From, DateOnly To) MonthOf(DateOnly date)
    {
        var first = new DateOnly(date.Year, date.Month, 1);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    public DateOnly Today => DateOnly.FromDateTime(_clock());

    public PeriodSummary Period(DateOnly from, DateOnly to)
        => PeriodSummary.Build(DaysIn(from, to), _policy);

    // ── the tick ──────────────────────────────────────────────────────────────

    public void Refresh()
    {
        var now = _clock();

        // Re-read from storage first. The MCP server is a separate process writing to the
        // same database, so anything an AI agent records would otherwise stay invisible
        // here until the widget was restarted. Cheap: one shared connection, two small
        // reads, every 30 seconds.
        ReloadIfChanged(now);

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
    /// Picks up work recorded elsewhere, and rolls the day over at midnight.
    /// </summary>
    private void ReloadIfChanged(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);

        // Past midnight the widget is still looking at yesterday. Roll to the new day
        // rather than keep accruing against a date that has ended.
        if (today != _day.Date)
        {
            _day = _repo.LoadDay(today) ?? WorkDay.NotStarted(today);
            _log = _repo.LoadActivities(today);
            _announcedHalfDay = false;
            _announcedFullDay = false;
            return;
        }

        if (_repo.LoadDay(today) is { } stored) _day = stored;
        _log = _repo.LoadActivities(today);
    }

    /// <summary>
    /// Fires the §22 notifications, once each per day. The flags matter: the tick runs every
    /// 30 seconds, and without them crossing half day would notify 600 times an afternoon.
    /// </summary>
    private void AnnounceThresholds(WorkDayStatus status)
    {
        if (!_prefs.NotifyThresholds) return;

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

    /// <summary>
    /// Delegates to <see cref="WorkingHoursPolicy.FromConfig"/> so the parsing is covered by
    /// tests. It used to live here, where nothing could reach it.
    /// </summary>
    private WorkingHoursPolicy LoadPolicy() => WorkingHoursPolicy.FromConfig(_repo.GetConfig);

    /// <summary>
    /// Persists the settings screen. Both halves are written together so the stored
    /// configuration can never be half-updated.
    /// </summary>
    public void SaveSettings(WorkingHoursPolicy policy, TrackingPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(preferences);

        foreach (var (key, value) in policy.ToConfig()) _repo.SetConfig(key, value);
        foreach (var (key, value) in preferences.ToConfig()) _repo.SetConfig(key, value);

        _policy = policy;
        _prefs = preferences;
        Refresh();
    }

    /// <summary>
    /// True when the hourly reminder (§8) is due. The caller asks on each tick; this owns
    /// the decision so the "when" lives beside the rest of the day's state.
    /// </summary>
    /// <remarks>
    /// Deliberately silent when the day has not started, when it is closed, and while on a
    /// break — a reminder to describe what you are working on is noise if you are not
    /// working. It also never fires twice for the same interval.
    /// </remarks>
    public bool NudgeIsDue()
    {
        if (!_prefs.NudgeEnabled || !CanTrack) return false;

        var now = _clock();
        if (WorkDayCalculator.Calculate(_day, now, _policy).IsOnBreak) return false;
        if (now - _lastNudge < _prefs.NudgeInterval!.Value) return false;

        _lastNudge = now;
        return true;
    }

    /// <summary>Resets the nudge clock — answering it counts as having been asked.</summary>
    public void NudgeAnswered() => _lastNudge = _clock();

    /// <summary>
    /// True when a second-half office arrival still needs its explanation (§5).
    /// </summary>
    public bool NeedsLocationNote()
        => CanTrack && _day.NeedsLocationNote(new TimeOnly(13, 0));

    public void SetLocationNote(string note)
    {
        _day = _day.WithNote(note);
        _repo.SaveDay(_day);
        Refresh();
    }

    /// <summary>Records a move to a different location during the day (§5).</summary>
    public void MoveTo(WorkLocation location)
    {
        _day = _day.AtLocation(location, _clock());
        _repo.SaveDay(_day);
        Refresh();
    }

    private static string Hhmm(TimeSpan value)
        => $"{(int)value.TotalHours:00}:{Math.Abs(value.Minutes):00}";

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
