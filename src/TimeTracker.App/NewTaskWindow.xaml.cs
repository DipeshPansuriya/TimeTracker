using System.Windows;

namespace TimeTracker.App;

/// <summary>
/// Asked only when the task actually changes (brief §7). The customer arrives pre-filled from
/// the running task, so an unchanged customer costs nothing and a changed one is one edit —
/// which is the whole difference between this and a manual timesheet.
/// </summary>
public partial class NewTaskWindow : Window
{
    public NewTaskWindow(string? currentCustomer)
    {
        InitializeComponent();

        CustomerBox.Text = currentCustomer ?? string.Empty;
        CarryNote.Text = currentCustomer is null
            ? "First task of the day."
            : $"Still {currentCustomer}? Leave it as it is — change it only if it moved.";

        Loaded += (_, _) => { TitleBox.Focus(); };
    }

    public string TaskTitle { get; private set; } = string.Empty;
    public string? Customer { get; private set; }
    public string? Description { get; private set; }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (title.Length == 0)
        {
            ErrorText.Text = "Give the task a name — it is what you will see on the summary.";
            ErrorText.Visibility = Visibility.Visible;
            TitleBox.Focus();
            return;
        }

        TaskTitle = title;

        // Empty means "not known", which the end-of-day review will chase (§23). It is not
        // the same as "unchanged" — that case is handled by the box arriving pre-filled.
        var customer = CustomerBox.Text.Trim();
        Customer = customer.Length == 0 ? null : customer;

        var description = DescriptionBox.Text.Trim();
        Description = description.Length == 0 ? null : description;

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
