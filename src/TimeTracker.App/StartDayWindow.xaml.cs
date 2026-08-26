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
    public StartDayWindow(string title, string question, TimeOnly defaultTime, bool showLocation)
    {
        InitializeComponent();

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

        SelectedTime = parsed;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
