using System.Globalization;

namespace TimeTracker.Core;

/// <summary>
/// The settings that are not about hours: which task statuses exist (§9), how often to nudge
/// (§8), and which notifications to show (§22).
/// </summary>
/// <remarks>
/// Separate from <see cref="WorkingHoursPolicy"/> because they change for different reasons.
/// Working hours mirror what HRMS enforces and are effectively fixed for a grade; these are
/// personal preferences someone will fiddle with in their first week and then leave alone.
/// </remarks>
public sealed record TrackingPreferences(
    IReadOnlyList<ActivityStatus> Statuses,
    TimeSpan? NudgeInterval,
    bool NotifyThresholds,
    bool NotifyReviewAtEnd)
{
    /// <summary>All six documented statuses, an hourly nudge, notifications on.</summary>
    public static TrackingPreferences Default { get; } = new(
        Statuses:
        [
            ActivityStatus.NotStarted, ActivityStatus.InProgress, ActivityStatus.Completed,
            ActivityStatus.OnHold, ActivityStatus.Blocked, ActivityStatus.Cancelled
        ],
        NudgeInterval: TimeSpan.FromHours(1),
        NotifyThresholds: true,
        NotifyReviewAtEnd: true);

    /// <summary>§8 says the reminder is optional; null means off.</summary>
    public bool NudgeEnabled => NudgeInterval is { } n && n > TimeSpan.Zero;

    public static TrackingPreferences FromConfig(Func<string, string?> get)
    {
        ArgumentNullException.ThrowIfNull(get);

        return new TrackingPreferences(
            Statuses: ParseStatuses(get("task_statuses")),
            NudgeInterval: ParseNudge(get("nudge_minutes")),
            NotifyThresholds: ParseBool(get("notify_thresholds"), Default.NotifyThresholds),
            NotifyReviewAtEnd: ParseBool(get("notify_review"), Default.NotifyReviewAtEnd));
    }

    /// <summary>Key/value pairs to persist. Paired with <see cref="FromConfig"/>.</summary>
    public IEnumerable<(string Key, string Value)> ToConfig()
    {
        yield return ("task_statuses", string.Join(',', Statuses));
        yield return ("nudge_minutes",
            NudgeInterval is { } n ? ((int)n.TotalMinutes).ToString(CultureInfo.InvariantCulture) : "0");
        yield return ("notify_thresholds", NotifyThresholds ? "1" : "0");
        yield return ("notify_review", NotifyReviewAtEnd ? "1" : "0");
    }

    private static IReadOnlyList<ActivityStatus> ParseStatuses(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return Default.Statuses;

        var parsed = stored
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.TryParse<ActivityStatus>(s, ignoreCase: true, out var v)
                ? v : (ActivityStatus?)null)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .Distinct()
            .ToList();

        // InProgress is not optional — it is what a running task is, so removing it would
        // leave the widget unable to describe the task in front of you.
        if (!parsed.Contains(ActivityStatus.InProgress))
            parsed.Insert(0, ActivityStatus.InProgress);

        return parsed.Count == 0 ? Default.Statuses : parsed;
    }

    private static TimeSpan? ParseNudge(string? stored)
    {
        if (!int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
            return Default.NudgeInterval;

        // Zero or negative means off. A nudge under five minutes is not a reminder, it is
        // harassment, so it is clamped rather than honoured.
        return minutes <= 0 ? null : TimeSpan.FromMinutes(Math.Max(5, minutes));
    }

    private static bool ParseBool(string? stored, bool fallback) => stored switch
    {
        "1" or "true" or "True" => true,
        "0" or "false" or "False" => false,
        _ => fallback
    };
}
