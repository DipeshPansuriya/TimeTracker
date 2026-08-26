using TimeTracker.Core;

namespace TimeTracker.Core.Tests;

/// <summary>
/// A wrong Time In poisons every figure for the day and every rollup that includes it, so the
/// one field the user types gets its own tests.
/// </summary>
public class TimeEntryTests
{
    [Theory]
    [InlineData("0815", 8, 15)]     // 4-digit shorthand
    [InlineData("08:15", 8, 15)]
    [InlineData("8:15", 8, 15)]
    [InlineData("8.15", 8, 15)]     // full stop, because keypads have one
    [InlineData(" 09:00 ", 9, 0)]   // stray whitespace
    [InlineData("1830", 18, 30)]    // 24-hour
    [InlineData("18:30", 18, 30)]
    [InlineData("00:00", 0, 0)]
    [InlineData("23:59", 23, 59)]
    public void AcceptsWhatPeopleActuallyType(string input, int hour, int minute)
    {
        Assert.True(TimeEntry.TryParse(input, out var value));
        Assert.Equal(new TimeOnly(hour, minute), value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("banana")]
    [InlineData("25:00")]           // not a time of day
    [InlineData("09:75")]
    [InlineData("--")]
    public void RejectsRatherThanGuesses(string? input)
        => Assert.False(TimeEntry.TryParse(input, out _));

    [Fact]
    public void ThreeDigitsAreRejected_BecauseTheyAreAmbiguous()
    {
        // "815" could be 8:15; treating it as such is a guess about the user's intent, and
        // the cost of asking again is one keypress against a wrong day.
        Assert.False(TimeEntry.TryParse("815", out _));
    }

    [Fact]
    public void SecondsAreDropped()
    {
        Assert.True(TimeEntry.TryParse("09:15:37", out var value));
        Assert.Equal(new TimeOnly(9, 15), value);
    }

    [Fact]
    public void AParsedTimeFeedsTheEngineUnchanged()
    {
        // The end-to-end path that matters: typed text becomes Time In becomes worked hours.
        Assert.True(TimeEntry.TryParse("0910", out var start));

        var day = WorkDay.Started(new DateTime(2026, 8, 26).Add(start.ToTimeSpan()))
                         .Completed(new DateTime(2026, 8, 26, 18, 0, 0));

        var policy = new WorkingHoursPolicy(
            new TimeSpan(4, 15, 0), new TimeSpan(8, 30, 0),
            [DayOfWeek.Wednesday], new TimeOnly(9, 0));

        var status = WorkDayCalculator.Calculate(day, new DateTime(2026, 8, 26, 18, 0, 0), policy);

        Assert.Equal(new TimeSpan(8, 50, 0), status.Worked);   // brief §10
    }
}
