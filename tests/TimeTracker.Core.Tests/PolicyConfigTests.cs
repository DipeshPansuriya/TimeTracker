using TimeTracker.Core;

namespace TimeTracker.Core.Tests;

/// <summary>
/// Reading the stored configuration. Trivial-looking, and exactly where a wrong default
/// start time would come from — which is worth a test, because a wrong Time In poisons
/// every figure the widget shows.
/// </summary>
public class PolicyConfigTests
{
    private static WorkingHoursPolicy From(Dictionary<string, string> values)
        => WorkingHoursPolicy.FromConfig(k => values.GetValueOrDefault(k));

    [Fact]
    public void AnEmptyConfigGivesTheDocumentedDefaults()
    {
        var policy = From([]);

        Assert.Equal(new TimeOnly(9, 0), policy.DefaultStart);
        Assert.Equal(new TimeSpan(8, 30, 0), policy.FullDay);
        Assert.Equal(new TimeSpan(4, 15, 0), policy.HalfDay);
        Assert.Equal(5, policy.WorkingDays.Count);
    }

    [Fact]
    public void StoredValuesAreUsed()
    {
        var policy = From(new()
        {
            ["half_day"] = "04:00",
            ["full_day"] = "09:00",
            ["default_start"] = "08:30",
            ["working_days"] = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday"
        });

        Assert.Equal(new TimeOnly(8, 30), policy.DefaultStart);
        Assert.Equal(new TimeSpan(9, 0, 0), policy.FullDay);
        Assert.Equal(6, policy.WorkingDays.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("banana")]
    [InlineData("9am")]
    [InlineData("25:00")]
    public void AnUnreadableStartTimeFallsBackRatherThanGuessing(string stored)
    {
        var policy = From(new() { ["default_start"] = stored });

        Assert.Equal(new TimeOnly(9, 0), policy.DefaultStart);
    }

    [Fact]
    public void ParsingIsCultureInvariant()
    {
        // The machine this was built on runs en-IN, whose short time pattern is 12-hour.
        // Stored values must not be reinterpreted by whatever locale the laptop is set to.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-IN");
            Assert.Equal(new TimeOnly(9, 0), From([]).DefaultStart);

            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(new TimeOnly(8, 30),
                From(new() { ["default_start"] = "08:30" }).DefaultStart);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void GarbageWorkingDaysFallBackToTheWorkingWeek()
        => Assert.Equal(5, From(new() { ["working_days"] = "Funday,Notaday" }).WorkingDays.Count);
}
