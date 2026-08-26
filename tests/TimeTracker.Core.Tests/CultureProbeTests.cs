using System.Globalization;
using TimeTracker.Core;

namespace TimeTracker.Core.Tests;

/// <summary>
/// Pins the behaviour that the old culture-sensitive parsing relied on. Written to answer
/// a specific question rather than to specify a requirement: the widget was showing a
/// default start time of 11:09 where 09:00 was expected.
/// </summary>
public class CultureProbeTests
{
    [Fact]
    public void CultureSensitiveParseOfNineAm_UnderTheLocalCulture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-IN");

            var parsed = TimeOnly.Parse("09:00");

            // If this fails, the message shows what it actually produced.
            Assert.Equal(new TimeOnly(9, 0), parsed);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void TheDefaultPolicyStartIsNineAm()
        => Assert.Equal(new TimeOnly(9, 0), WorkingHoursPolicy.Default.DefaultStart);

    [Fact]
    public void FromConfigWithNothingStored_IsNineAm()
        => Assert.Equal(new TimeOnly(9, 0),
                        WorkingHoursPolicy.FromConfig(_ => null).DefaultStart);
}
