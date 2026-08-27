using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TimeTracker.Core;

namespace TimeTracker.App;

/// <summary>
/// The first-run setup from brief §1, available at any time.
/// </summary>
/// <remarks>
/// Until this existed, <c>SavePolicy</c> was never called by anything — so the working-hour
/// rules were readable from the database but unchangeable from the product, which made the
/// brief's "configurable rather than hard-coded" true only on paper.
/// </remarks>
public partial class SettingsWindow : Window
{
    private readonly WidgetViewModel _model;
    private readonly Dictionary<DayOfWeek, CheckBox> _days = [];
    private readonly Dictionary<ActivityStatus, CheckBox> _statuses = [];

    public SettingsWindow(WidgetViewModel model)
    {
        InitializeComponent();
        _model = model;

        BuildDayCheckboxes();
        BuildStatusCheckboxes();
        Load(model.Policy, model.Preferences);
    }

    private void BuildDayCheckboxes()
    {
        // Monday first: a working week, not a calendar week.
        foreach (var day in (DayOfWeek[])
                 [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                  DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday])
        {
            var box = new CheckBox
            {
                Content = day.ToString()[..3],
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 13,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = day.ToString()
            };
            _days[day] = box;
            DayGrid.Children.Add(box);
        }
    }

    private void BuildStatusCheckboxes()
    {
        foreach (var status in Enum.GetValues<ActivityStatus>())
        {
            var box = new CheckBox
            {
                Content = Humanise(status),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 13,
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = status.ToString()
            };

            // In progress is what a running task *is*, so it cannot be switched off.
            if (status == ActivityStatus.InProgress)
            {
                box.IsChecked = true;
                box.IsEnabled = false;
                box.ToolTip = "Always available — this is what a running task is";
            }

            _statuses[status] = box;
            StatusGrid.Children.Add(box);
        }
    }

    private void Load(WorkingHoursPolicy policy, TrackingPreferences prefs)
    {
        HalfDayBox.Text = policy.HalfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        FullDayBox.Text = policy.FullDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        StartBox.Text = policy.DefaultStart.ToString("HH:mm", CultureInfo.InvariantCulture);

        foreach (var (day, box) in _days) box.IsChecked = policy.WorkingDays.Contains(day);
        foreach (var (status, box) in _statuses)
            box.IsChecked = prefs.Statuses.Contains(status) || status == ActivityStatus.InProgress;

        var minutes = (int)(prefs.NudgeInterval?.TotalMinutes ?? 0);
        NudgeOff.IsChecked = minutes == 0;
        Nudge30.IsChecked = minutes == 30;
        Nudge60.IsChecked = minutes == 60;
        Nudge120.IsChecked = minutes == 120;
        if (minutes is not (0 or 30 or 60 or 120)) Nudge60.IsChecked = true;

        NotifyThresholds.IsChecked = prefs.NotifyThresholds;
        NotifyReview.IsChecked = prefs.NotifyReviewAtEnd;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!TimeEntry.TryParse(StartBox.Text, out var start))
        {
            Fail($"'{StartBox.Text}' is not a time. Try 09:00 or 0900.", StartBox);
            return;
        }

        if (!TryDuration(HalfDayBox.Text, out var half))
        {
            Fail($"'{HalfDayBox.Text}' is not a length of time. Try 04:15.", HalfDayBox);
            return;
        }

        if (!TryDuration(FullDayBox.Text, out var full))
        {
            Fail($"'{FullDayBox.Text}' is not a length of time. Try 08:30.", FullDayBox);
            return;
        }

        // A half day longer than a full day would make every status calculation nonsense,
        // and it is an easy thing to type by accident.
        if (half >= full)
        {
            Fail($"Half day ({half:hh\\:mm}) must be shorter than full day ({full:hh\\:mm}).",
                 HalfDayBox);
            return;
        }

        var days = _days.Where(d => d.Value.IsChecked == true).Select(d => d.Key).ToList();
        if (days.Count == 0)
        {
            Fail("Pick at least one working day, or the widget will never track anything.",
                 HalfDayBox);
            return;
        }

        var statuses = _statuses
            .Where(s => s.Value.IsChecked == true)
            .Select(s => s.Key)
            .ToList();

        _model.SaveSettings(
            new WorkingHoursPolicy(half, full, days, start),
            new TrackingPreferences(
                Statuses: statuses,
                NudgeInterval: SelectedNudge(),
                NotifyThresholds: NotifyThresholds.IsChecked == true,
                NotifyReviewAtEnd: NotifyReview.IsChecked == true));

        DialogResult = true;
    }

    private TimeSpan? SelectedNudge()
        => Nudge30.IsChecked == true ? TimeSpan.FromMinutes(30)
         : Nudge60.IsChecked == true ? TimeSpan.FromHours(1)
         : Nudge120.IsChecked == true ? TimeSpan.FromHours(2)
         : null;

    /// <summary>
    /// Accepts <c>08:30</c> and <c>8:30</c>, and also a bare number of hours.
    /// </summary>
    private static bool TryDuration(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim().Replace('.', ':');

        if (TimeSpan.TryParseExact(trimmed, [@"hh\:mm", @"h\:mm"],
                                   CultureInfo.InvariantCulture, out value))
            return value > TimeSpan.Zero && value < TimeSpan.FromHours(24);

        if (int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            && hours is > 0 and < 24)
        {
            value = TimeSpan.FromHours(hours);
            return true;
        }

        return false;
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        Load(WorkingHoursPolicy.Default, TrackingPreferences.Default);
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private static string Humanise(ActivityStatus status) => status switch
    {
        ActivityStatus.NotStarted => "Not started",
        ActivityStatus.InProgress => "In progress",
        ActivityStatus.OnHold => "On hold",
        _ => status.ToString()
    };

    private void Fail(string message, Control focus)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        focus.Focus();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
