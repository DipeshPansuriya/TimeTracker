using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TimeTracker.Core;

/// <summary>
/// Identifier for one activity record, in the documented form <c>ACT-20260826-000123</c>
/// (brief §20).
/// </summary>
/// <remarks>
/// This is the idempotency key for <b>both</b> HRMS sync retries and retried MCP writes, which
/// is why it is assigned when the record is created and then stored — never recomputed at send
/// time. A value derived at transmission would differ between the first attempt and the retry,
/// which is precisely the duplicate the brief asks to prevent.
/// </remarks>
public readonly record struct ActivityId
{
    private const string Prefix = "ACT";

    public string Value { get; }

    private ActivityId(string value) => Value = value;

    /// <summary>Builds an id from the activity's date and a per-day sequence number.</summary>
    public static ActivityId Create(DateOnly date, int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, 999_999);

        return new ActivityId(
            $"{Prefix}-{date:yyyyMMdd}-{sequence.ToString("D6", CultureInfo.InvariantCulture)}");
    }

    public static ActivityId Parse(string value)
        => TryParse(value, out var id) ? id : throw new FormatException($"Not an activity id: '{value}'.");

    public static bool TryParse([NotNullWhen(true)] string? value, out ActivityId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split('-');
        if (parts.Length != 3) return false;
        if (parts[0] != Prefix) return false;
        if (parts[1].Length != 8 || !DateOnly.TryParseExact(
                parts[1], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return false;
        if (parts[2].Length != 6 || !int.TryParse(
                parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return false;

        id = new ActivityId(value);
        return true;
    }

    public override string ToString() => Value;
}
