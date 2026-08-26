using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimeTracker.Core;

namespace TimeTracker.App;

/// <summary>
/// The end-of-day review (brief §23). Shows the day's figures, lets anything wrong be
/// corrected, and reports what is missing — so gaps get fixed before the record is closed
/// rather than discovered later by HRMS.
/// </summary>
public partial class ReviewWindow : Window
{
    private readonly WidgetViewModel _model;

    public ReviewWindow(WidgetViewModel model)
    {
        InitializeComponent();
        _model = model;
        Render();
    }

    /// <summary>
    /// Rebuilt from the view model after every edit rather than patched in place, so what is
    /// on screen is always what was actually saved.
    /// </summary>
    private void Render()
    {
        var now = DateTime.Now;
        var summary = DaySummary.Build(_model.Day, _model.Log, _model.Policy, now);

        SubText.Text = summary.TimeIn is { } inAt
            ? $"{inAt:dddd d MMMM}  ·  in at {inAt:HH\\:mm}" +
              (summary.TimeOut is { } outAt ? $", out at {outAt:HH\\:mm}" : ", still open")
            : $"{summary.Date:dddd d MMMM}  ·  not started";

        WorkedText.Text = Hhmm(summary.Worked);
        RequiredText.Text = Hhmm(summary.Required);

        // Colour encodes state and nothing else — a neutral figure stays Deep Sea. Reset
        // first, because Render runs repeatedly and a card painted green once would stay
        // green after an edit made it wrong.
        ResetCard(KpiWorked, WorkedText);
        ResetCard(KpiBalance, BalanceText);
        ResetCard(KpiStatus, StatusText);

        if (summary.Worked >= summary.Required)
        {
            BalanceKey.Text = "OVERTIME";
            BalanceText.Text = "+" + Hhmm(summary.Worked - summary.Required);
            Paint(KpiBalance, BalanceText, "#2E7D32");
            Paint(KpiWorked, WorkedText, "#2E7D32");
        }
        else
        {
            BalanceKey.Text = "SHORTFALL";
            BalanceText.Text = "-" + Hhmm(summary.Required - summary.Worked);
            Paint(KpiBalance, BalanceText, "#E65100");
        }

        StatusText.Text = summary.Completion switch
        {
            DayCompletion.FullDayComplete => "Full day",
            DayCompletion.HalfDayComplete => "Half day",
            DayCompletion.InProgress => "Part day",
            _ => "Not started"
        };

        if (summary.Completion == DayCompletion.FullDayComplete)
            Paint(KpiStatus, StatusText, "#2E7D32");

        TaskList.ItemsSource = _model.Log.Activities
            .Select(a => new ActivityRow(
                a.Id.Value,
                a.Title,
                a.Customer ?? "No customer",
                $"{a.Start:HH\\:mm}-{(a.End is { } e ? e.ToString("HH\\:mm") : "running")}",
                Hhmm(a.DurationAt(now))))
            .ToList();

        var gaps = _model.Log.MissingInformation().ToList();
        GapPanel.Visibility = gaps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (gaps.Count > 0)
        {
            GapText.Text = gaps.Count == 1
                ? $"Customer for “{gaps[0].Title}”. Click the row to fill it in."
                : $"Customer for {gaps.Count} activities: " +
                  $"{string.Join(", ", gaps.Select(g => g.Title))}. Click a row to fill it in.";
        }
    }

    /// <summary>One row of the activity list. A record so the template can bind by name.</summary>
    private sealed record ActivityRow(
        string Id, string Title, string Customer, string Times, string Duration);

    private void OnEditActivity(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ActivityRow row }) return;

        var activity = _model.Log.Activities.FirstOrDefault(a => a.Id.Value == row.Id);
        if (activity is null) return;

        var dialog = new EditActivityWindow(activity) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _model.AmendActivity(
            activity.Id, dialog.TaskTitle, dialog.Customer, dialog.Description,
            dialog.Start, dialog.End, dialog.Status);

        Render();
    }

    private void OnEditHours(object sender, RoutedEventArgs e)
    {
        if (!_model.DayStarted)
        {
            System.Windows.MessageBox.Show(this, "The day has not been started yet, so there are no hours to edit.",
                            "Time tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new EditActivityWindow(_model.Day) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _model.AmendDayTimes(dialog.Start, dialog.End);
        Render();
    }

    private static void ResetCard(Border card, TextBlock text)
    {
        var navy = (System.Windows.Media.Brush)new BrushConverter().ConvertFrom("#081F4B")!;
        card.BorderBrush = navy;
        text.Foreground = navy;
    }

    private static void Paint(Border card, TextBlock text, string hex)
    {
        var brush = (System.Windows.Media.Brush)new BrushConverter().ConvertFrom(hex)!;
        card.BorderBrush = brush;
        text.Foreground = brush;
    }

    private static string Hhmm(TimeSpan v)
        => $"{Math.Abs((int)v.TotalHours):00}:{Math.Abs(v.Minutes):00}";

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
