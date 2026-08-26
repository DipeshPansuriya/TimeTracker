namespace TimeTracker.Core;

/// <summary>
/// Everything the end-of-day view shows (brief §10). Built from a <see cref="WorkDay"/> and
/// its <see cref="ActivityLog"/>; also the unit the weekly and monthly rollups consume, so
/// a period summary never has to re-derive a day's arithmetic.
/// </summary>
public sealed record DaySummary(
    DateOnly Date,
    DateTime? TimeIn,
    DateTime? TimeOut,
    TimeSpan Worked,
    TimeSpan Required,
    TimeSpan Remaining,
    TimeSpan Overtime,
    TimeSpan Shortfall,
    DayCompletion Completion,
    WorkLocation? PrimaryLocation,
    bool ClientVisit,
    int TotalTasks,
    int CompletedTasks,
    int PendingTasks,
    IReadOnlyDictionary<string, TimeSpan> ByCustomer,
    IReadOnlyDictionary<string, TimeSpan> ByTask,
    string? Note)
{
    public int Customers => ByCustomer.Count;

    public static DaySummary Build(
        WorkDay day, ActivityLog log, WorkingHoursPolicy policy, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(log);

        var status = WorkDayCalculator.Calculate(day, now, policy);

        return new DaySummary(
            Date: day.Date,
            TimeIn: day.StartedAt,
            TimeOut: day.CompletedAt,
            Worked: status.Worked,
            Required: status.Required,
            Remaining: status.Remaining,
            Overtime: status.Overtime,
            Shortfall: status.Shortfall,
            Completion: status.Completion,
            PrimaryLocation: day.PrimaryLocation(now),
            ClientVisit: day.HadClientVisit,
            TotalTasks: log.Activities.Count,
            CompletedTasks: log.CompletedCount,
            PendingTasks: log.PendingCount,
            ByCustomer: log.TotalsByCustomer(now),
            ByTask: log.TotalsByTask(now),
            Note: day.Note);
    }
}

/// <summary>
/// Weekly and monthly rollups (brief §11, §12). One type serves both — the only difference
/// between a week and a month is which days you hand it.
/// </summary>
public sealed record PeriodSummary(
    DateOnly? From,
    DateOnly? To,
    int WorkingDays,
    TimeSpan RequiredHours,
    TimeSpan WorkedHours,
    TimeSpan Overtime,
    TimeSpan Shortfall,
    TimeSpan Balance,
    int FullDays,
    int HalfDays,
    int WfhDays,
    int OfficeDays,
    int ClientVisitDays,
    int TotalTasks,
    int CompletedTasks,
    int PendingTasks,
    IReadOnlyDictionary<string, TimeSpan> ByCustomer,
    IReadOnlyDictionary<string, TimeSpan> ByTask)
{
    public static PeriodSummary Build(
        IEnumerable<DaySummary> days, WorkingHoursPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(policy);

        var list = days as IReadOnlyList<DaySummary> ?? [.. days];

        return new PeriodSummary(
            From: list.Count == 0 ? null : list.Min(d => d.Date),
            To: list.Count == 0 ? null : list.Max(d => d.Date),
            WorkingDays: list.Count,
            RequiredHours: Sum(list, d => d.Required),
            WorkedHours: Sum(list, d => d.Worked),

            // Overtime and shortfall are summed independently, not netted. A week that runs
            // +2h one day and −2h another has a zero balance but is not a week with no
            // exceptions — HRMS reconciles against both figures.
            Overtime: Sum(list, d => d.Overtime),
            Shortfall: Sum(list, d => d.Shortfall),
            Balance: Sum(list, d => d.Worked) - Sum(list, d => d.Required),

            FullDays: list.Count(d => d.Completion == DayCompletion.FullDayComplete),
            HalfDays: list.Count(d => d.Completion == DayCompletion.HalfDayComplete),
            WfhDays: list.Count(d => d.PrimaryLocation == WorkLocation.Home),
            OfficeDays: list.Count(d => d.PrimaryLocation == WorkLocation.Office),

            // Counted from the flag, not the primary location: a day can be spent mostly in
            // the office and still contain a client visit.
            ClientVisitDays: list.Count(d => d.ClientVisit),

            TotalTasks: list.Sum(d => d.TotalTasks),
            CompletedTasks: list.Sum(d => d.CompletedTasks),
            PendingTasks: list.Sum(d => d.PendingTasks),
            ByCustomer: Merge(list, d => d.ByCustomer),
            ByTask: Merge(list, d => d.ByTask));
    }

    private static TimeSpan Sum(IEnumerable<DaySummary> days, Func<DaySummary, TimeSpan> pick)
        => days.Aggregate(TimeSpan.Zero, (t, d) => t + pick(d));

    private static IReadOnlyDictionary<string, TimeSpan> Merge(
        IEnumerable<DaySummary> days,
        Func<DaySummary, IReadOnlyDictionary<string, TimeSpan>> pick)
        => days
            .SelectMany(pick)
            .GroupBy(kv => kv.Key)
            .ToDictionary(g => g.Key, g => g.Aggregate(TimeSpan.Zero, (t, kv) => t + kv.Value));
}
