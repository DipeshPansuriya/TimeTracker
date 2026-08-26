namespace TimeTracker.Core;

/// <summary>How far through the day's required hours the user has got.</summary>
public enum DayCompletion
{
    /// <summary>The user has not answered the start prompt. Nothing is being counted.</summary>
    NotStarted,
    InProgress,
    HalfDayComplete,
    FullDayComplete
}

/// <summary>
/// A stretch of time excluded from worked hours. <paramref name="End"/> is null while the
/// user is still on the break — the widget deducts only up to the current moment, never a
/// notional full break.
/// </summary>
public sealed record BreakInterval(DateTime Start, DateTime? End)
{
    /// <summary>The break clipped to <paramref name="upTo"/>, for partial deduction.</summary>
    internal (DateTime Start, DateTime End) ClipTo(DateTime upTo)
        => (Start, End is { } e && e < upTo ? e : upTo);
}

/// <summary>
/// One day's record. Immutable: every mutation returns a new instance, so a day can be
/// recomputed from any point without worrying about who mutated it last.
/// </summary>
/// <remarks>
/// <see cref="StartedAt"/> and <see cref="CompletedAt"/> are full <see cref="DateTime"/>
/// values rather than times-of-day. That is deliberate — it makes a shift running past
/// midnight arithmetic rather than a special case.
/// </remarks>
public sealed class WorkDay
{
    public DateOnly Date { get; }

    /// <summary>Time In. Null until the user answers the morning prompt.</summary>
    public DateTime? StartedAt { get; }

    /// <summary>Time Out. Set by "Complete day"; null while the day is open.</summary>
    public DateTime? CompletedAt { get; }

    public IReadOnlyList<BreakInterval> Breaks { get; }

    /// <summary>Where the user was, over the course of the day (§5).</summary>
    public IReadOnlyList<LocationSpell> Locations { get; }

    /// <summary>Free-text reason, e.g. why the office arrival was after midday (§5).</summary>
    public string? Note { get; }

    private WorkDay(DateOnly date, DateTime? startedAt, DateTime? completedAt,
                    IReadOnlyList<BreakInterval> breaks,
                    IReadOnlyList<LocationSpell> locations, string? note)
        => (Date, StartedAt, CompletedAt, Breaks, Locations, Note)
         = (date, startedAt, completedAt, breaks, locations, note);

    /// <summary>A configured working day the user has not started yet.</summary>
    public static WorkDay NotStarted(DateOnly date) => new(date, null, null, [], [], null);

    /// <summary>The user answered the morning prompt with <paramref name="at"/>.</summary>
    public static WorkDay Started(DateTime at)
        => new(DateOnly.FromDateTime(at), at, null, [], [], null);

    public WorkDay WithBreak(DateTime start, DateTime end)
        => With(breaks: [.. Breaks, new BreakInterval(start, end)]);

    /// <summary>User pressed Break and has not returned.</summary>
    public WorkDay WithOpenBreak(DateTime start)
        => With(breaks: [.. Breaks, new BreakInterval(start, null)]);

    /// <summary>User pressed Complete day. Hours stop accruing from this point.</summary>
    public WorkDay Completed(DateTime at)
        => With(completedAt: at, locations: CloseSpells(at));

    /// <summary>Records a move to a new location, closing the previous spell.</summary>
    public WorkDay AtLocation(WorkLocation location, DateTime from)
        => With(locations: [.. CloseSpells(from), new LocationSpell(from, null, location)]);

    public WorkDay WithNote(string note) => With(note: note);

    /// <summary>
    /// Corrects Time In and/or Time Out after the fact. Null means "leave it alone".
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Started"/> and <see cref="Completed"/> because those are
    /// events — they close location spells and set the day's shape. This only moves the
    /// clock hands: breaks, locations and the note survive untouched, and correcting the
    /// end does not reopen a closed day.
    /// </remarks>
    public WorkDay AmendTimes(DateTime? startedAt = null, DateTime? completedAt = null)
    {
        var newStart = startedAt ?? StartedAt;
        var newEnd = completedAt ?? CompletedAt;

        if (newStart is { } s && newEnd is { } e && e <= s)
            throw new ArgumentOutOfRangeException(nameof(completedAt),
                $"A day cannot end at {e:HH:mm} having started at {s:HH:mm}.");

        return new WorkDay(Date, newStart, newEnd, Breaks, Locations, Note);
    }

    private IReadOnlyList<LocationSpell> CloseSpells(DateTime at)
        => [.. Locations.Select(s => s.End is null ? s with { End = at } : s)];

    private WorkDay With(
        DateTime? completedAt = null,
        IReadOnlyList<BreakInterval>? breaks = null,
        IReadOnlyList<LocationSpell>? locations = null,
        string? note = null)
        => new(Date, StartedAt, completedAt ?? CompletedAt, breaks ?? Breaks,
               locations ?? Locations, note ?? Note);

    public bool IsOpen => StartedAt is not null && CompletedAt is null;

    public WorkLocation? LocationAt(DateTime at)
        => Locations.LastOrDefault(s => s.Start <= at && (s.End is null || at < s.End))?.Location;

    public bool HadClientVisit => Locations.Any(s => s.Location == WorkLocation.Client);

    /// <summary>
    /// The location the day is attributed to when a single answer is needed — the one the
    /// user spent longest at. A client visit is reported separately rather than overwriting
    /// this, so a day can be both "office" and "had a client visit".
    /// </summary>
    public WorkLocation? PrimaryLocation(DateTime now)
        => Locations.Count == 0 ? null
         : Locations
            .GroupBy(s => s.Location)
            .OrderByDescending(g => g.Aggregate(TimeSpan.Zero,
                (t, s) => t + ((s.End ?? CompletedAt ?? now) - s.Start)))
            .First().Key;

    /// <summary>
    /// True when the user arrived at the office after the midday boundary having been
    /// elsewhere, and has not yet explained why (§5).
    /// </summary>
    public bool NeedsLocationNote(TimeOnly middayBoundary)
    {
        if (Note is not null) return false;

        var officeSpell = Locations.FirstOrDefault(s => s.Location == WorkLocation.Office);
        if (officeSpell is null) return false;

        var arrivedAfterMidday = TimeOnly.FromDateTime(officeSpell.Start) > middayBoundary;
        var wasElsewhereFirst = Locations.Any(s => s.Location != WorkLocation.Office
                                                && s.Start < officeSpell.Start);
        return arrivedAfterMidday && wasElsewhereFirst;
    }
}
