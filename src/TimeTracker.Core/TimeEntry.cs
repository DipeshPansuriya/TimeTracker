using System.Globalization;

namespace TimeTracker.Core;

/// <summary>
/// Parsing for the one time the user actually types. Lives here rather than in the dialog so
/// it can be tested — a wrong Time In is the most damaging single error this application can
/// make, and logic buried in a window's click handler is logic nobody can verify.
/// </summary>
public static class TimeEntry
{
    /// <summary>
    /// Accepts what people actually type for a time: <c>0815</c>, <c>8:15</c>, <c>08:15</c>,
    /// <c>8.15</c>, <c>8:15 AM</c>. Rejects anything ambiguous rather than guessing — the
    /// widget asks again, which is cheap; a silently wrong start time is not.
    /// </summary>
    public static bool TryParse(string? input, out TimeOnly value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var text = input.Trim().Replace('.', ':');

        // 4 digits: 0815 -> 08:15. 3 digits are deliberately not accepted; "815" could be
        // 8:15 or 08:15, and the two differ by nothing here but the guess is still a guess.
        if (text.Length == 4 && text.All(char.IsAsciiDigit))
            text = text[..2] + ":" + text[2..];

        foreach (var format in Formats)
            if (TimeOnly.TryParseExact(text, format, CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out value))
                return Accept(ref value);

        // Culture-specific last, so a machine set to a 12-hour locale still works.
        if (TimeOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out value))
            return Accept(ref value);

        value = default;
        return false;
    }

    private static readonly string[] Formats =
        ["HH:mm", "H:mm", "hh:mm tt", "h:mm tt", "hhmm", "HHmm"];

    /// <summary>
    /// True when a claimed start time is far enough in the past to be worth querying.
    /// </summary>
    /// <remarks>
    /// Not a validation rule — starting at 07:00 and opening the widget at 16:00 is perfectly
    /// normal, and refusing it would be wrong. It exists so a start time that arrives by
    /// accident is <i>visible</i> rather than silently becoming the day's record. A wrong
    /// Time In poisons every figure for the day and every rollup containing it, and the whole
    /// point of the widget is that nobody re-checks these numbers by hand.
    /// </remarks>
    public static bool LooksSuspicious(TimeOnly start, DateTime now, out TimeSpan agesAgo)
    {
        agesAgo = now.TimeOfDay - start.ToTimeSpan();
        return agesAgo > TimeSpan.FromHours(4);
    }

    /// <summary>Seconds are dropped: a timesheet claiming 09:00:37 implies a precision nobody has.</summary>
    private static bool Accept(ref TimeOnly value)
    {
        value = new TimeOnly(value.Hour, value.Minute);
        return true;
    }
}
