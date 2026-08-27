using TimeTracker.Core;

namespace TimeTracker.Core.Tests;

public class ExportTests
{
    private static readonly WorkingHoursPolicy Policy = new(
        new TimeSpan(4, 15, 0), new TimeSpan(8, 30, 0),
        [DayOfWeek.Wednesday], new TimeOnly(9, 0));

    private static DateTime On(int day, int h, int m) => new(2026, 8, day, h, m, 0);

    private static DaySummary Day(int date, TimeSpan worked, string? note = null)
    {
        var d = new DateOnly(2026, 8, date);
        var day = WorkDay.Started(new DateTime(d, new TimeOnly(9, 0)))
                         .AtLocation(WorkLocation.Office, new DateTime(d, new TimeOnly(9, 0)))
                         .Completed(new DateTime(d, new TimeOnly(9, 0)).Add(worked));

        if (note is not null) day = day.WithNote(note);

        return DaySummary.Build(day, ActivityLog.Empty(d), Policy,
                                new DateTime(d, new TimeOnly(23, 0)));
    }

    [Fact]
    public void TheHeaderRowNamesEveryColumn()
    {
        var csv = TimesheetExport.DaysToCsv([Day(24, new TimeSpan(8, 30, 0))]);

        Assert.StartsWith("Date,Day,Time in,Time out,Worked,Required,", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void EachDayBecomesARow_AndThereIsATotal()
    {
        var csv = TimesheetExport.DaysToCsv(
            [Day(24, new TimeSpan(8, 30, 0)), Day(25, new TimeSpan(9, 0, 0))]);

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, lines.Length);                       // header + 2 days + total
        Assert.Contains("2026-08-24", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("TOTAL", lines[3], StringComparison.Ordinal);
        Assert.Contains("17:30", lines[3], StringComparison.Ordinal);   // 8:30 + 9:00
    }

    [Fact]
    public void DaysAreExportedInDateOrder_WhateverOrderTheyArriveIn()
    {
        var csv = TimesheetExport.DaysToCsv(
            [Day(26, new TimeSpan(8, 0, 0)), Day(24, new TimeSpan(8, 0, 0))]);

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("2026-08-24", lines[1], StringComparison.Ordinal);
        Assert.Contains("2026-08-26", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void ACommaInFreeTextDoesNotShiftTheColumns()
    {
        // "Client visit, reached office at 2pm" would otherwise split into two fields and
        // silently move every later column one to the right.
        var csv = TimesheetExport.DaysToCsv(
            [Day(24, new TimeSpan(8, 0, 0), note: "Client visit, reached office at 2pm")]);

        Assert.Contains("\"Client visit, reached office at 2pm\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuoteInFreeTextIsDoubled()
    {
        var csv = TimesheetExport.DaysToCsv(
            [Day(24, new TimeSpan(8, 0, 0), note: "Fixed the \"slow\" report")]);

        Assert.Contains("\"Fixed the \"\"slow\"\" report\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivitiesExportCarriesIdsAndDurations()
    {
        var log = ActivityLog.Empty(new DateOnly(2026, 8, 26))
            .Start("API performance optimization", "Galaxy", On(26, 10, 0))
            .Start("Database migration", "ABC Logistics", On(26, 12, 30));

        var csv = TimesheetExport.ActivitiesToCsv(log, On(26, 14, 0));
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);                        // header + 2
        Assert.Contains("ACT-20260826-000001", lines[1], StringComparison.Ordinal);
        Assert.Contains("02:30", lines[1], StringComparison.Ordinal);
        Assert.Contains("running", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPeriodStillProducesAUsableFile()
    {
        var csv = TimesheetExport.DaysToCsv([]);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);                        // header + a zero total
        Assert.StartsWith("TOTAL", lines[1], StringComparison.Ordinal);
    }
}
