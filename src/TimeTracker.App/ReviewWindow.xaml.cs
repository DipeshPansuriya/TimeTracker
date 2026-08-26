using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimeTracker.Core;

namespace TimeTracker.App;

/// <summary>
/// The end-of-day review (brief §23). Shows the day's figures and, importantly, what is
/// missing — so gaps get fixed before the record is closed rather than discovered by HRMS.
/// </summary>
public partial class ReviewWindow : Window
{
    public ReviewWindow(WidgetViewModel model)
    {
        InitializeComponent();

        var now = DateTime.Now;
        var summary = DaySummary.Build(model.Day, model.Log, model.Policy, now);

        SubText.Text = summary.TimeIn is { } inAt
            ? $"{inAt:dddd d MMMM}  ·  in at {inAt:hh\\:mm tt}"
            : $"{summary.Date:dddd d MMMM}";

        WorkedText.Text = Hhmm(summary.Worked);
        RequiredText.Text = Hhmm(summary.Required);

        // Colour encodes state, nothing else: a neutral figure stays Deep Sea. Overtime is
        // success green, a shortfall is warning amber, and each carries its own word.
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

        TaskList.ItemsSource = model.Log.Activities
            .Select(a => new
            {
                a.Title,
                Customer = a.Customer ?? "No customer",
                Duration = Hhmm(a.DurationAt(now))
            })
            .ToList();

        var gaps = model.Log.MissingInformation().ToList();
        if (gaps.Count > 0)
        {
            GapPanel.Visibility = Visibility.Visible;
            GapText.Text = gaps.Count == 1
                ? $"Customer for “{gaps[0].Title}”. You can still complete the day; it will stay flagged."
                : $"Customer for {gaps.Count} activities: {string.Join(", ", gaps.Select(g => g.Title))}.";
        }
    }

    private static void Paint(Border card, TextBlock text, string hex)
    {
        var brush = (System.Windows.Media.Brush)new BrushConverter().ConvertFrom(hex)!;
        card.BorderBrush = brush;
        text.Foreground = brush;
    }

    private static string Hhmm(TimeSpan v) => $"{(int)v.TotalHours:00}:{Math.Abs(v.Minutes):00}";

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
