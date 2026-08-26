namespace TimeTracker.Core;

/// <summary>Where the user was working. Brief §9 tracks all three equally.</summary>
public enum WorkLocation
{
    Office,
    Home,
    Client
}

/// <summary>
/// A stretch of the day spent at one location. A day can hold several — the brief's §5
/// case is a client visit in the first half followed by the office in the second.
/// </summary>
public sealed record LocationSpell(DateTime Start, DateTime? End, WorkLocation Location);
