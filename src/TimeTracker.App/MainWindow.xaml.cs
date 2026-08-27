using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using TimeTracker.Core;

namespace TimeTracker.App;

public partial class MainWindow : Window
{
    private WidgetViewModel? _model;

    public MainWindow() => InitializeComponent();

    public void Bind(WidgetViewModel model)
    {
        _model = model;
        DataContext = model;
        model.PropertyChanged += (_, _) => UpdateButtons();
        UpdateButtons();
    }

    /// <summary>
    /// The buttons say what is actually available. "Continue" on a day that has not started
    /// is a control that lies, and the brief's whole point is not making the user guess.
    /// </summary>
    private void UpdateButtons()
    {
        if (_model is null) return;

        var onBreak = _model.Day.Breaks.Any(b => b.End is null);
        BtnBreak.Content = onBreak ? "Resume" : "Break";
        BtnBreak.IsEnabled = _model.CanTrack;
        BtnPrimary.IsEnabled = _model.CanTrack;
        BtnComplete.IsEnabled = _model.CanTrack;
        BtnPrimary.Content = _model.Log.Current is null ? "Start task" : "Continue";
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void OnHide(object sender, RoutedEventArgs e) => Hide();

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        new SettingsWindow(_model) { Owner = this }.ShowDialog();
    }

    /// <summary>
    /// "Continue" is deliberately a no-op on the record when a task is already running —
    /// it acknowledges the nudge without writing anything (brief §7).
    /// </summary>
    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        if (_model.Log.Current is null) OnNewTask(sender, e);
        else _model.Refresh();
    }

    /// <summary>Lets the hourly nudge open the same dialog the button does.</summary>
    public void StartNewTask() => OnNewTask(this, new RoutedEventArgs());

    private void OnNewTask(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;

        // The current customer is offered as the default, so an unchanged customer costs
        // nothing to confirm and a changed one is a single edit.
        var dialog = new NewTaskWindow(_model.Log.CurrentCustomer) { Owner = this };
        if (dialog.ShowDialog() == true)
            _model.StartTask(dialog.TaskTitle, dialog.Customer, dialog.Description);
    }

    private void OnBreak(object sender, RoutedEventArgs e) => _model?.ToggleBreak();

    /// <summary>
    /// Review without ending the day. Corrections get noticed mid-afternoon, and a review
    /// reachable only by finishing is one people work around instead of using.
    /// </summary>
    private void OnReview(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        var review = new ReviewWindow(_model) { Owner = this };
        if (review.ShowDialog() == true) _model.CompleteDay();
    }

    private void OnOpenSummary(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        new SummaryWindow(_model).Show();
    }

    private void OnCompleteDay(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;

        var review = new ReviewWindow(_model) { Owner = this };
        if (review.ShowDialog() == true) _model.CompleteDay();
    }

    /// <summary>Closing the widget hides it; the day keeps running in the tray.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
