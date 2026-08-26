using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using TimeTracker.Core;
using TimeTracker.Storage;
using Forms = System.Windows.Forms;

namespace TimeTracker.App;

public partial class App : System.Windows.Application
{
    private TimeTrackerDatabase? _database;
    private Forms.NotifyIcon? _tray;
    private MainWindow? _widget;
    private WidgetViewModel? _model;
    private DispatcherTimer? _tick;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _database = TimeTrackerDatabase.OpenDefault();
        }
        catch (Exception ex)
        {
            // The database is the whole application. Failing loudly beats starting up in a
            // state where the user believes hours are being recorded and they are not.
            System.Windows.MessageBox.Show(
                $"The local database could not be opened, so nothing can be recorded.\n\n{ex.Message}",
                "Time tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var repo = new TimesheetRepository(_database);
        _model = new WidgetViewModel(repo);
        _model.Notify += ShowBalloon;

        BuildTray();

        _widget = new MainWindow { DataContext = _model };
        _widget.Bind(_model);
        PositionBottomRight(_widget);
        _widget.Show();

        // Every 30 seconds. Nothing accumulates between ticks — each one recomputes from
        // the clock — so a missed tick or a sleeping machine costs nothing.
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _tick.Tick += (_, _) => _model.Refresh();
        _tick.Start();

        AskAboutUnclosedDay();
        AskToStartTheDay();
    }

    /// <summary>
    /// The "you didn't close Tuesday" prompt. The widget never invents a time out — it asks,
    /// and the user states it.
    /// </summary>
    private void AskAboutUnclosedDay()
    {
        if (_model?.UnclosedDay() is not { } stale) return;

        var dialog = new StartDayWindow(
            title: $"{stale:dddd d MMMM} was never closed",
            question: "What time did you finish that day?",
            defaultTime: new TimeOnly(18, 0),
            showLocation: false);

        if (dialog.ShowDialog() == true)
            _model.CloseUnclosedDay(stale, stale.ToDateTime(dialog.SelectedTime));
    }

    /// <summary>The one question of the day (§2), pre-filled from the configured default.</summary>
    private void AskToStartTheDay()
    {
        if (_model is null || _model.DayStarted) return;

        if (!_model.Policy.IsWorkingDay(DateOnly.FromDateTime(DateTime.Now)))
            return;   // not a configured working day — stay quiet

        var dialog = new StartDayWindow(
            title: "Good morning",
            question: "What time did you start working?",
            defaultTime: _model.Policy.DefaultStart,
            showLocation: true);

        if (dialog.ShowDialog() == true)
            _model.StartDay(
                DateTime.Today.Add(dialog.SelectedTime.ToTimeSpan()),
                dialog.SelectedLocation);
    }

    private void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show widget", null, (_, _) => ShowWidget());
        menu.Items.Add("Complete day", null, (_, _) => { _model?.CompleteDay(); ShowWidget(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Time tracker",
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowWidget();
    }

    private void ShowWidget()
    {
        if (_widget is null) return;
        _model?.Refresh();
        _widget.Show();
        _widget.Activate();
    }

    private void ShowBalloon(string title, string message)
        => _tray?.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Info);

    private static void PositionBottomRight(Window window)
    {
        var area = SystemParameters.WorkArea;
        window.Left = area.Right - window.Width - 16;
        window.Top = area.Bottom - 420;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tick?.Stop();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _database?.Dispose();
        base.OnExit(e);
    }
}
