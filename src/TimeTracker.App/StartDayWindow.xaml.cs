using System.Globalization;
using System.Windows;
using TimeTracker.Core;

namespace TimeTracker.App;

/// <summary>
/// The one question of the day. Pre-filled with the configured default so the usual answer
/// is a single keypress; editable because the default is a guess, not a fact.
/// </summary>
public partial class StartDayWindow : Window
{
    /// <summary>Only the morning prompt queries a distant time; closing yesterday is expected to be.</summary>
    private readonly bool _confirmDistantStart;

    public StartDayWindow(string title, string question, TimeOnly defaultTime, bool showLocation)
    {
        InitializeComponent();
        _confirmDistantStart = showLocation;   // true only for the start-of-day prompt

        TitleText.Text = title;
        QuestionText.Text = question;
        TimeBox.Text = defaultTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        SelectedTime = defaultTime;

        LocationPanel.Visibility = showLocation ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButton.Content = showLocation ? "Start" : "Save";

        // Whole field selected, so typing over it replaces it and Enter accepts the default.
        Loaded += (_, _) => { TimeBox.Focus(); TimeBox.SelectAll(); };
    }

    public TimeOnly SelectedTime { get; private set; }

    public WorkLocation SelectedLocation =>
        OptHome.IsChecked == true ? WorkLocation.Home
      : OptClient.IsChecked == true ? WorkLocation.Client
      : WorkLocation.Office;

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (!TimeEntry.TryParse(TimeBox.Text, out var parsed))
        {
            ErrorText.Text = $"'{TimeBox.Text}' is not a time. Try 09:15, 9:15 or 0915.";
            ErrorText.Visibility = Visibility.Visible;
            TimeBox.Focus();
            TimeBox.SelectAll();
            return;
        }

        // A start time hours in the past is legitimate — you may have begun at 07:00 and
        // only opened the widget after lunch. But it is also what a stray keystroke or a
        // mis-parse looks like, so it gets confirmed rather than silently recorded.
        if (_confirmDistantStart &&
            TimeEntry.LooksSuspicious(parsed, DateTime.Now, out var agesAgo))
        {
            var answer = System.Windows.MessageBox.Show(
                this,
                $"That records your day as starting at {parsed:HH\\:mm} — " +
                $"{(int)agesAgo.TotalHours}h {agesAgo.Minutes:00}m ago." +
                Environment.NewLine + Environment.NewLine +
                "Is that right?",
                "Time tracker",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                TimeBox.Focus();
                TimeBox.SelectAll();
                return;
            }
        }

        SelectedTime = parsed;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
