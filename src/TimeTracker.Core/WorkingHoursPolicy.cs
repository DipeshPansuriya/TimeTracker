namespace TimeTracker.Core;

/// <summary>
/// The working-hour rules captured during first-run setup (brief §1). Every value here is
/// configured by the user rather than hard-coded, because they mirror whatever the HRMS
/// happens to enforce and that differs between employers and grades.
/// </summary>
/// <param name="HalfDay">Worked time at which a half day is credited, e.g. 04:15.</param>
/// <param name="FullDay">Worked time at which a full day is credited, e.g. 08:30.</param>
/// <param name="WorkingDays">Days the widget tracks at all.</param>
/// <param name="DefaultStart">
/// Pre-filled answer to the one morning question. The user accepts it with a tap or
/// changes it; the widget never adopts it silently.
/// </param>
public sealed record WorkingHoursPolicy(
    TimeSpan HalfDay,
    TimeSpan FullDay,
    IReadOnlyCollection<DayOfWeek> WorkingDays,
    TimeOnly DefaultStart)
{
    /// <summary>
    /// Required time for a full day. Named separately from <see cref="FullDay"/> because
    /// the brief uses "Required" in the UI and "Full Day" as the status label; keeping one
    /// backing value stops the two drifting apart.
    /// </summary>
    public TimeSpan Required => FullDay;

    public bool IsWorkingDay(DateOnly date) => WorkingDays.Contains(date.DayOfWeek);
}
