using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using TimeTracker.Core;

namespace TimeTracker.App;

/// <summary>
/// Day, week and month reporting (brief §10–§12), with the activity detail behind §6.
/// </summary>
/// <remarks>
/// One window for all three periods rather than three screens: the questions are the same
/// (how long, against what target, for whom) and only the range changes. The day view adds
/// per-activity detail because that is the only period where individual tasks are legible.
/// </remarks>
public partial class SummaryWindow : Window
{
    private readonly WidgetViewModel _model;
    private DateOnly _anchor;
    private Period _period = Period.Day;

    private enum Period { Day, Week, Month }

    public SummaryWindow(WidgetViewModel model)
    {
        InitializeComponent();
        _model = model;
        _anchor = model.Today;
        Render();
    }

    // ── row shapes the templates bind to ──────────────────────────────────────

    private sealed record Kpi(string Label, string Value, string Note, string Accent);

    private sealed record DayRow(
        string Date, string Detail, string Status, string StatusBg, string StatusFg,
        string Worked, string Balance, string BalanceFg);

    private sealed record TaskRow(
        string Id, string Title, string Customer, string Description,
        Visibility DescriptionVisible, string Times, string Duration,
        string Status, string StatusFg);

    private sealed record BreakdownRow(string Name, string Hours, double BarWidth);

    // ── rendering ─────────────────────────────────────────────────────────────

    private void Render()
    {
        var (from, to) = _period switch
        {
            Period.Week => _model.WeekOf(_anchor),
            Period.Month => _model.MonthOf(_anchor),
            _ => (_anchor, _anchor)
        };

        var days = _model.DaysIn(from, to);
        var period = _model.Period(from, to);

        HeadingText.Text = _period switch
        {
            Period.Week => $"Week of {from:d MMMM yyyy}",
            Period.Month => from.ToString("MMMM yyyy"),
            _ => _anchor == _model.Today ? "Today" : _anchor.ToString("dddd d MMMM yyyy")
        };

        RangeText.Text = _period == Period.Day
            ? _anchor.ToString("dddd d MMMM yyyy")
            : $"{from:d MMM} to {to:d MMM yyyy}  ·  {period.WorkingDays} day(s) recorded";

        // Cannot look into the future, so the forward control disables at the current period.
        BtnNext.IsEnabled = to < _model.Today;

        RenderKpis(period, days);
        RenderDays(days);
        RenderTasks();
        RenderBreakdowns(period);
    }

    private void RenderKpis(PeriodSummary period, IReadOnlyList<DaySummary> days)
    {
        const string navy = "#16181D", green = "#2E7D5B", amber = "#B4671F", blue = "#3A6EA5";
        var kpis = new List<Kpi>();

        if (_period == Period.Day)
        {
            var d = days.FirstOrDefault();
            if (d is null)
            {
                kpis.Add(new Kpi("WORKED", "—", "Nothing recorded", navy));
            }
            else
            {
                kpis.Add(new Kpi("WORKED", Hhmm(d.Worked),
                    $"Required {Hhmm(d.Required)}", d.Worked >= d.Required ? green : navy));
                kpis.Add(new Kpi("TIME IN", d.TimeIn?.ToString("HH:mm") ?? "—",
                    d.TimeOut is { } o ? $"Out at {o:HH\\:mm}" : "Still open", navy));
                kpis.Add(d.Overtime > TimeSpan.Zero
                    ? new Kpi("OVERTIME", "+" + Hhmm(d.Overtime), "Beyond full day", green)
                    : d.Shortfall > TimeSpan.Zero
                        ? new Kpi("SHORTFALL", "-" + Hhmm(d.Shortfall), "Below full day", amber)
                        : new Kpi("BALANCE", "00:00", "Exactly on target", navy));
                kpis.Add(new Kpi("STATUS", StatusWord(d.Completion),
                    d.PrimaryLocation?.ToString() ?? "Location not set",
                    d.Completion == DayCompletion.FullDayComplete ? green : navy));
                kpis.Add(new Kpi("ACTIVITIES", d.TotalTasks.ToString(),
                    $"{d.CompletedTasks} complete, {d.PendingTasks} pending", navy));
                kpis.Add(new Kpi("CUSTOMERS", d.Customers.ToString(), "On this day", navy));
                if (d.ClientVisit) kpis.Add(new Kpi("CLIENT VISIT", "Yes", d.Note ?? "Recorded", blue));
            }
        }
        else
        {
            kpis.Add(new Kpi("WORKED", Hhmm(period.WorkedHours),
                $"Required {Hhmm(period.RequiredHours)}",
                period.WorkedHours >= period.RequiredHours ? green : navy));
            kpis.Add(new Kpi("BALANCE",
                (period.Balance < TimeSpan.Zero ? "-" : "+") + Hhmm(period.Balance),
                "Worked less required",
                period.Balance < TimeSpan.Zero ? amber : green));

            // Overtime and shortfall are shown separately, never netted: a week that runs
            // over one day and under another is not a week without exceptions.
            kpis.Add(new Kpi("OVERTIME", "+" + Hhmm(period.Overtime), "Summed, not netted",
                period.Overtime > TimeSpan.Zero ? green : navy));
            kpis.Add(new Kpi("SHORTFALL", "-" + Hhmm(period.Shortfall), "Summed, not netted",
                period.Shortfall > TimeSpan.Zero ? amber : navy));
            kpis.Add(new Kpi("FULL DAYS", period.FullDays.ToString(),
                $"{period.HalfDays} half day(s)", navy));
            kpis.Add(new Kpi("OFFICE", period.OfficeDays.ToString(),
                $"{period.WfhDays} from home", navy));
            kpis.Add(new Kpi("CLIENT VISITS", period.ClientVisitDays.ToString(),
                "Days including one", period.ClientVisitDays > 0 ? blue : navy));
            kpis.Add(new Kpi("ACTIVITIES", period.TotalTasks.ToString(),
                $"{period.CompletedTasks} complete", navy));
        }

        KpiList.ItemsSource = kpis;
    }

    private void RenderDays(IReadOnlyList<DaySummary> days)
    {
        if (_period == Period.Day)
        {
            DaysSection.Visibility = Visibility.Collapsed;
            return;
        }

        DaysSection.Visibility = Visibility.Visible;
        DaysList.ItemsSource = days.Select(d =>
        {
            var over = d.Worked >= d.Required;
            var balance = over ? d.Worked - d.Required : d.Required - d.Worked;

            var customers = d.ByCustomer.Count == 0
                ? "No activities"
                : string.Join(", ", d.ByCustomer.OrderByDescending(kv => kv.Value)
                                                .Take(3).Select(kv => kv.Key));

            return new DayRow(
                Date: d.Date.ToString("ddd d MMM"),
                Detail: customers + (d.ClientVisit ? "  ·  client visit" : ""),
                Status: StatusWord(d.Completion),
                StatusBg: d.Completion == DayCompletion.FullDayComplete ? "#E9F5EF" : "#FDF3E6",
                StatusFg: d.Completion == DayCompletion.FullDayComplete ? "#2E7D5B" : "#B4671F",
                Worked: Hhmm(d.Worked),
                Balance: (over ? "+" : "-") + Hhmm(balance),
                BalanceFg: over ? "#2E7D5B" : "#B4671F");
        }).ToList();
    }

    private void RenderTasks()
    {
        if (_period != Period.Day)
        {
            TasksSection.Visibility = Visibility.Collapsed;
            return;
        }

        TasksSection.Visibility = Visibility.Visible;

        // Only today's activities are held in memory; other days come from storage.
        var log = _anchor == _model.Log.Date ? _model.Log : null;
        if (log is null)
        {
            TasksList.ItemsSource = null;
            NoTasksText.Text = "Activity detail is available for today.";
            NoTasksText.Visibility = Visibility.Visible;
            return;
        }

        var now = DateTime.Now;
        var rows = log.Activities.Select(a => new TaskRow(
            Id: a.Id.Value,
            Title: a.Title,
            Customer: a.Customer ?? "No customer",
            Description: a.Description ?? string.Empty,
            DescriptionVisible: string.IsNullOrWhiteSpace(a.Description)
                ? Visibility.Collapsed : Visibility.Visible,
            Times: $"{a.Start:HH\\:mm}-{(a.End is { } e ? e.ToString("HH\\:mm") : "running")}",
            Duration: Hhmm(a.DurationAt(now)),
            Status: a.Status.ToString(),
            StatusFg: a.Status switch
            {
                ActivityStatus.Completed => "#2E7D5B",
                ActivityStatus.InProgress => "#3A6EA5",
                ActivityStatus.OnHold or ActivityStatus.Blocked => "#B4671F",
                ActivityStatus.Cancelled => "#C0392B",
                _ => "#6B7280"
            })).ToList();

        TasksList.ItemsSource = rows;
        NoTasksText.Text = "No activities recorded.";
        NoTasksText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderBreakdowns(PeriodSummary period)
    {
        CustomerList.ItemsSource = Breakdown(period.ByCustomer);
        TaskBreakdownList.ItemsSource = Breakdown(period.ByTask);
    }

    /// <summary>
    /// Bars are scaled to the largest entry, not to the period total — the question these
    /// answer is "where did most of it go", and against a total everything looks small.
    /// </summary>
    private static List<BreakdownRow> Breakdown(IReadOnlyDictionary<string, TimeSpan> source)
    {
        if (source.Count == 0) return [];

        var ordered = source.OrderByDescending(kv => kv.Value).ToList();
        var max = ordered[0].Value.TotalMinutes;

        return [.. ordered.Select(kv => new BreakdownRow(
            kv.Key,
            Hhmm(kv.Value),
            max <= 0 ? 0 : Math.Max(2, kv.Value.TotalMinutes / max * 260)))];
    }

    private static string StatusWord(DayCompletion c) => c switch
    {
        DayCompletion.FullDayComplete => "Full day",
        DayCompletion.HalfDayComplete => "Half day",
        DayCompletion.InProgress => "Part day",
        _ => "Not started"
    };

    private static string Hhmm(TimeSpan v)
        => $"{Math.Abs((int)v.TotalHours):00}:{Math.Abs(v.Minutes):00}";

    // ── interaction ───────────────────────────────────────────────────────────

    private void OnPeriodChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _period = TabWeek.IsChecked == true ? Period.Week
                : TabMonth.IsChecked == true ? Period.Month
                : Period.Day;
        Render();
    }

    private void OnPrevious(object sender, RoutedEventArgs e)
    {
        _anchor = _period switch
        {
            Period.Week => _anchor.AddDays(-7),
            Period.Month => _anchor.AddMonths(-1),
            _ => _anchor.AddDays(-1)
        };
        Render();
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        _anchor = _period switch
        {
            Period.Week => _anchor.AddDays(7),
            Period.Month => _anchor.AddMonths(1),
            _ => _anchor.AddDays(1)
        };
        if (_anchor > _model.Today) _anchor = _model.Today;
        Render();
    }

    private void OnToday(object sender, RoutedEventArgs e)
    {
        _anchor = _model.Today;
        Render();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    /// <summary>
    /// Writes the visible period to CSV.
    /// </summary>
    /// <remarks>
    /// The stop-gap for HRMS sync being blocked on a credential decision. People still have
    /// to report their hours this month, and "wait for the integration" is not an answer.
    /// A single day also gets its activity detail, since that is the only period where
    /// individual tasks are worth reading.
    /// </remarks>
    private void OnExport(object sender, RoutedEventArgs e)
    {
        var (from, to) = _period switch
        {
            Period.Week => _model.WeekOf(_anchor),
            Period.Month => _model.MonthOf(_anchor),
            _ => (_anchor, _anchor)
        };

        var suggested = _period switch
        {
            Period.Week => $"timesheet-week-{from:yyyy-MM-dd}.csv",
            Period.Month => $"timesheet-{from:yyyy-MM}.csv",
            _ => $"timesheet-{from:yyyy-MM-dd}.csv"
        };

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = suggested,
            DefaultExt = ".csv",
            Filter = "Comma separated values (*.csv)|*.csv",
            Title = "Export timesheet"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var csv = TimesheetExport.DaysToCsv(_model.DaysIn(from, to));

            if (_period == Period.Day && _anchor == _model.Log.Date)
            {
                csv += Environment.NewLine +
                       TimesheetExport.ActivitiesToCsv(_model.Log, DateTime.Now);
            }

            File.WriteAllText(dialog.FileName, csv, Encoding.UTF8);

            MessageBox.Show(this, $"Saved to{Environment.NewLine}{dialog.FileName}",
                            "Time tracker", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Named the two that actually happen — file open in Excel, or a folder the user
            // cannot write to. Anything else is a real bug and should not be swallowed.
            MessageBox.Show(this,
                $"Could not write the file.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Time tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
