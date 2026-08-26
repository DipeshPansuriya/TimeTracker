using System.Windows;
using TimeTracker.Core;

namespace TimeTracker.App;

/// <summary>
/// Corrects one recorded activity, or the day's own Time In and Time Out.
/// </summary>
/// <remarks>
/// Two modes in one window on purpose: they ask the same four questions (what, for whom,
/// from when, to when) and a second near-identical dialog would drift from this one.
/// </remarks>
public partial class EditActivityWindow : Window
{
    private readonly Activity? _activity;
    private readonly bool _dayMode;

    /// <summary>Edit a recorded activity.</summary>
    public EditActivityWindow(Activity activity)
    {
        InitializeComponent();
        _activity = activity;

        HeadingText.Text = "Edit activity";
        IdText.Text = activity.Id.Value;
        TitleBox.Text = activity.Title;
        CustomerBox.Text = activity.Customer ?? string.Empty;
        DescriptionBox.Text = activity.Description ?? string.Empty;
        StartBox.Text = activity.Start.ToString("HH:mm");

        if (activity.End is { } end)
        {
            EndBox.Text = end.ToString("HH:mm");
        }
        else
        {
            // Running: leaving the end blank keeps it running, which is almost always what
            // is wanted when correcting the start time of the task in front of you.
            EndLabel.Text = "END (BLANK = STILL RUNNING)";
            EndBox.Text = string.Empty;
        }

        StatusBox.ItemsSource = Enum.GetValues<ActivityStatus>();
        StatusBox.SelectedItem = activity.Status;

        Loaded += (_, _) => { TitleBox.Focus(); TitleBox.SelectAll(); };
    }

    /// <summary>Edit the day's Time In and Time Out.</summary>
    public EditActivityWindow(WorkDay day)
    {
        InitializeComponent();
        _dayMode = true;

        HeadingText.Text = "Edit today's hours";
        IdText.Text = day.Date.ToString("dddd d MMMM yyyy");

        // Day mode asks only about the clock, so the task fields go away entirely rather
        // than sitting there disabled.
        foreach (var element in new UIElement[]
                 { TitleLabel, TitleBox, CustomerLabel, CustomerBox,
                   DescriptionLabel, DescriptionBox, StatusPanel })
        {
            element.Visibility = Visibility.Collapsed;
        }

        StartLabel.Text = "TIME IN";

        StartBox.Text = day.StartedAt?.ToString("HH:mm") ?? string.Empty;
        EndBox.Text = day.CompletedAt?.ToString("HH:mm") ?? string.Empty;
        EndLabel.Text = "TIME OUT (BLANK = STILL OPEN)";

        Loaded += (_, _) => { StartBox.Focus(); StartBox.SelectAll(); };
    }

    public string TaskTitle { get; private set; } = string.Empty;
    public string? Customer { get; private set; }
    public string? Description { get; private set; }
    public TimeOnly Start { get; private set; }
    public TimeOnly? End { get; private set; }
    public ActivityStatus Status { get; private set; }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (!_dayMode && string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            Fail("Give the task a name — it is what you will see on the summary.", TitleBox);
            return;
        }

        if (!TimeEntry.TryParse(StartBox.Text, out var start))
        {
            Fail($"'{StartBox.Text}' is not a time. Try 09:15, 9:15 or 0915.", StartBox);
            return;
        }

        TimeOnly? end = null;
        if (!string.IsNullOrWhiteSpace(EndBox.Text))
        {
            if (!TimeEntry.TryParse(EndBox.Text, out var parsedEnd))
            {
                Fail($"'{EndBox.Text}' is not a time. Try 17:30, 5:30 PM or 1730.", EndBox);
                return;
            }

            if (parsedEnd <= start)
            {
                Fail($"End must be after start. You entered {start:HH\\:mm} to {parsedEnd:HH\\:mm}.",
                     EndBox);
                return;
            }

            end = parsedEnd;
        }

        TaskTitle = TitleBox.Text.Trim();
        var customer = CustomerBox.Text.Trim();
        Customer = customer.Length == 0 ? string.Empty : customer;   // empty clears it
        var description = DescriptionBox.Text.Trim();
        Description = description.Length == 0 ? string.Empty : description;
        Start = start;
        End = end;
        Status = StatusBox.SelectedItem is ActivityStatus s ? s
              : _activity?.Status ?? ActivityStatus.InProgress;

        DialogResult = true;
    }

    private void Fail(string message, System.Windows.Controls.Control focus)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        focus.Focus();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
