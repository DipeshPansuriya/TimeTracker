using System.Globalization;
using System.Text;

namespace TimeTracker.Core;

/// <summary>
/// Renders a period as CSV, for handing to HR by hand.
/// </summary>
/// <remarks>
/// This exists because HRMS sync is blocked on a credential decision that is not ours to
/// make. Rather than leave people unable to report their hours until that is resolved, they
/// can export the same figures and submit them however they already do. It is the fallback
/// in the workflow diagram, made real.
/// </remarks>
public static class TimesheetExport
{
    /// <summary>Characters that force a field to be quoted, per RFC 4180.</summary>
    private static readonly char[] SpecialChars =
        [',', '"', (char)10, (char)13];

    public static string DaysToCsv(IEnumerable<DaySummary> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        var list = days.OrderBy(d => d.Date).ToList();
        var sb = new StringBuilder();

        sb.AppendLine(string.Join(',',
            "Date", "Day", "Time in", "Time out", "Worked", "Required",
            "Overtime", "Shortfall", "Status", "Location", "Client visit",
            "Tasks", "Completed", "Note"));

        foreach (var d in list)
        {
            sb.AppendLine(string.Join(',',
                Csv(d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Csv(d.Date.DayOfWeek.ToString()),
                Csv(Clock(d.TimeIn)),
                Csv(Clock(d.TimeOut)),
                Csv(Hhmm(d.Worked)),
                Csv(Hhmm(d.Required)),
                Csv(Hhmm(d.Overtime)),
                Csv(Hhmm(d.Shortfall)),
                Csv(d.Completion.ToString()),
                Csv(d.PrimaryLocation?.ToString() ?? ""),
                Csv(d.ClientVisit ? "Yes" : "No"),
                Csv(d.TotalTasks.ToString(CultureInfo.InvariantCulture)),
                Csv(d.CompletedTasks.ToString(CultureInfo.InvariantCulture)),
                Csv(d.Note ?? "")));
        }

        // A totals row, because the first thing anyone does with an exported timesheet is
        // add up the hours column.
        var worked = list.Aggregate(TimeSpan.Zero, (t, d) => t + d.Worked);
        var required = list.Aggregate(TimeSpan.Zero, (t, d) => t + d.Required);
        var overtime = list.Aggregate(TimeSpan.Zero, (t, d) => t + d.Overtime);
        var shortfall = list.Aggregate(TimeSpan.Zero, (t, d) => t + d.Shortfall);

        sb.AppendLine(string.Join(',',
            Csv("TOTAL"), Csv($"{list.Count} day(s)"), Csv(""), Csv(""),
            Csv(Hhmm(worked)), Csv(Hhmm(required)), Csv(Hhmm(overtime)), Csv(Hhmm(shortfall)),
            Csv(""), Csv(""), Csv(""),
            Csv(list.Sum(d => d.TotalTasks).ToString(CultureInfo.InvariantCulture)),
            Csv(list.Sum(d => d.CompletedTasks).ToString(CultureInfo.InvariantCulture)),
            Csv("")));

        return sb.ToString();
    }

    /// <summary>Activity-level detail for one day.</summary>
    public static string ActivitiesToCsv(ActivityLog log, DateTime asOf)
    {
        ArgumentNullException.ThrowIfNull(log);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',',
            "Activity ID", "Date", "Task", "Customer", "Description",
            "Start", "End", "Duration", "Status", "Notes"));

        foreach (var a in log.Activities.OrderBy(a => a.Start))
        {
            sb.AppendLine(string.Join(',',
                Csv(a.Id.Value),
                Csv(log.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Csv(a.Title),
                Csv(a.Customer ?? ""),
                Csv(a.Description ?? ""),
                Csv(a.Start.ToString("HH:mm", CultureInfo.InvariantCulture)),
                Csv(a.End?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "running"),
                Csv(Hhmm(a.DurationAt(asOf))),
                Csv(a.Status.ToString()),
                Csv(a.Notes ?? "")));
        }

        return sb.ToString();
    }

    /// <summary>
    /// RFC 4180 quoting. Task titles and notes are free text — a comma in
    /// "Investigated slowness, tuned queries" would otherwise shift every later column.
    /// </summary>
    private static string Csv(string value)
    {
        if (value.Length == 0) return "";

        var needsQuoting = value.AsSpan().IndexOfAny(SpecialChars) >= 0
                        || value[0] == ' ' || value[^1] == ' ';

        return needsQuoting ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }

    private static string Hhmm(TimeSpan v)
        => $"{Math.Abs((int)v.TotalHours):00}:{Math.Abs(v.Minutes):00}";

    private static string Clock(DateTime? value)
        => value?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
}
