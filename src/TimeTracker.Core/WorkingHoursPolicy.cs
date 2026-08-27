using System.Globalization;

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

    /// <summary>The values used until the user changes them.</summary>
    public static WorkingHoursPolicy Default { get; } = new(
        HalfDay: new TimeSpan(4, 15, 0),
        FullDay: new TimeSpan(8, 30, 0),
        WorkingDays: [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                      DayOfWeek.Thursday, DayOfWeek.Friday],
        DefaultStart: new TimeOnly(9, 0));

    /// <summary>
    /// Builds the policy from stored configuration, falling back to <see cref="Default"/>
    /// for anything absent or unreadable.
    /// </summary>
    /// <remarks>
    /// In Core rather than the widget because it is logic, and logic in a view model is
    /// logic nothing can test. Parsing is culture-invariant on purpose: these are stored
    /// values, not user input, and a machine set to a 12-hour locale must not read
    /// <c>09:00</c> as anything other than nine in the morning.
    /// </remarks>
    public static WorkingHoursPolicy FromConfig(Func<string, string?> get)
    {
        ArgumentNullException.ThrowIfNull(get);

        return new WorkingHoursPolicy(
            HalfDay: Span(get("half_day"), Default.HalfDay),
            FullDay: Span(get("full_day"), Default.FullDay),
            WorkingDays: Days(get("working_days")),
            DefaultStart: Start(get("default_start")));
    }

    /// <summary>Key/value pairs to persist. Paired with <see cref="FromConfig"/>.</summary>
    public IEnumerable<(string Key, string Value)> ToConfig()
    {
        yield return ("half_day", HalfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        yield return ("full_day", FullDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        yield return ("default_start", DefaultStart.ToString("HH:mm", CultureInfo.InvariantCulture));
        yield return ("working_days", string.Join(',', WorkingDays));
    }

    private static TimeSpan Span(string? value, TimeSpan fallback)
        => TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : fallback;

    private static TimeOnly Start(string? value)
        => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture,
                                  DateTimeStyles.None, out var parsed)
            ? parsed : Default.DefaultStart;

    private static IReadOnlyCollection<DayOfWeek> Days(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Default.WorkingDays;

        var days = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => Enum.TryParse<DayOfWeek>(d, ignoreCase: true, out var day)
                ? day : (DayOfWeek?)null)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToHashSet();

        return days.Count == 0 ? Default.WorkingDays : days;
    }
}
