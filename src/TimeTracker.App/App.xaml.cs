using System.Drawing;
using System.IO;
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
        _tick.Tick += (_, _) =>
        {
            _model.Refresh();
            if (_model.NudgeIsDue()) AskWhatYouAreWorkingOn();
            if (_model.NeedsLocationNote()) AskForLocationNote();
        };
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
        if (_model is null) return;

        var storedTimeIn = _model.Day.StartedAt?.ToString("HH:mm") ?? "none";
        Log($"policy.DefaultStart={_model.Policy.DefaultStart:HH\\:mm} " +
            $"fullDay={_model.Policy.FullDay} dayStarted={_model.DayStarted} " +
            $"storedTimeIn={storedTimeIn}");

        if (_model.DayStarted) return;

        if (!_model.Policy.IsWorkingDay(DateOnly.FromDateTime(DateTime.Now)))
            return;   // not a configured working day — stay quiet

        var dialog = new StartDayWindow(
            title: "Good morning",
            question: "What time did you start working?",
            defaultTime: _model.Policy.DefaultStart,
            showLocation: true);

        var confirmed = dialog.ShowDialog();
        Log($"startDialog result={confirmed} selected={dialog.SelectedTime:HH\\:mm}");

        if (confirmed == true)
            _model.StartDay(
                DateTime.Today.Add(dialog.SelectedTime.ToTimeSpan()),
                dialog.SelectedLocation);
    }

    /// <summary>
    /// Appends a line to %LOCALAPPDATA%\TimeTrackerpp.log.
    /// </summary>
    /// <remarks>
    /// A desktop application that misbehaves on someone else's machine is undiagnosable
    /// without one of these. Deliberately plain text, in the data folder, and never
    /// throwing — a logging failure must not take the widget down.
    /// </remarks>
    internal static void Log(string message)
    {
        try
        {
            var path = Path.Combine(TimeTrackerDatabase.DefaultDirectory, "app.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Diagnostics are never worth crashing for.
        }
    }

    /// <summary>
    /// The optional reminder from §8. Answering "continue" writes nothing at all — the
    /// whole point is that an unchanged day costs one click and leaves no record behind.
    /// </summary>
    private void AskWhatYouAreWorkingOn()
    {
        if (_model is null) return;

        var current = _model.Log.Current;
        var prompt = current is null
            ? "You have not recorded a task yet. Start one now?"
            : $"Still on “{current.Title}”?";

        var answer = System.Windows.MessageBox.Show(
            prompt + Environment.NewLine + Environment.NewLine +
            "Yes keeps it running. No lets you start something else.",
            "Time tracker",
            current is null ? MessageBoxButton.YesNo : MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        _model.NudgeAnswered();

        if (answer == MessageBoxResult.No)
        {
            ShowWidget();
            _widget?.StartNewTask();
        }
    }

    /// <summary>
    /// §5: arriving at the office after midday having been elsewhere needs an explanation.
    /// Asked once — declining records that it was asked so it does not nag.
    /// </summary>
    private void AskForLocationNote()
    {
        if (_model is null) return;

        var answer = System.Windows.MessageBox.Show(
            "You reached the office after midday, having started somewhere else." +
            Environment.NewLine + Environment.NewLine +
            "Add a note explaining why? HRMS usually wants one.",
            "Time tracker", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            ShowWidget();
            var review = new ReviewWindow(_model) { Owner = _widget };
            if (review.ShowDialog() == true) _model.CompleteDay();
        }
        else
        {
            _model.SetLocationNote("(no reason given)");
        }
    }

    private void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show widget", null, (_, _) => ShowWidget());
        // Reachable all day, not only when finishing: corrections are usually noticed
        // mid-afternoon, and a review you can only open by ending the day is one people
        // work around instead of using.
        menu.Items.Add("Review / edit today", null, (_, _) => ShowReview());
        menu.Items.Add("Day, week and month report", null, (_, _) => ShowSummary());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add("Complete day", null, (_, _) => { _model?.CompleteDay(); ShowWidget(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "Time tracker",
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowWidget();
    }

    /// <summary>The embedded app mark, at the size the tray actually wants.</summary>
    /// <remarks>
    /// Asking for <c>SystemInformation.SmallIconSize</c> matters: the .ico carries several
    /// frames, and letting the runtime choose yields a scaled-down 32px frame that looks
    /// soft at tray scale. Falls back to the system icon rather than throwing - a missing
    /// icon must never stop the day being tracked.
    /// </remarks>
    private static Icon LoadAppIcon()
    {
        try
        {
            using var stream = typeof(App).Assembly
                .GetManifestResourceStream("TimeTracker.App.app.ico");
            if (stream is not null)
                return new Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception)
        {
            // Fall through to the system icon below.
        }

        return SystemIcons.Application;
    }

    /// <summary>Brief §1 setup, reachable at any time rather than only on first run.</summary>
    private void ShowSettings()
    {
        if (_model is null) return;
        new SettingsWindow(_model) { Owner = _widget }.ShowDialog();
    }

    private void ShowSummary()
    {
        if (_model is null) return;
        _model.Refresh();
        new SummaryWindow(_model).Show();
    }

    private void ShowReview()
    {
        if (_model is null) return;
        ShowWidget();
        var review = new ReviewWindow(_model) { Owner = _widget };
        if (review.ShowDialog() == true) _model.CompleteDay();
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

    /// <summary>
    /// Parks the widget above the tray, using its measured height rather than a guess.
    /// </summary>
    /// <remarks>
    /// This used to subtract a hard-coded 420px. Adding a row of buttons made the widget
    /// taller and it started hanging off the bottom of the screen — the classic cost of a
    /// magic number standing in for a measurement.
    /// </remarks>
    private static void PositionBottomRight(Window window)
    {
        var area = SystemParameters.WorkArea;

        void Park()
        {
            var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            window.Left = area.Right - window.Width - 16;
            window.Top = Math.Max(area.Top + 8, area.Bottom - height - 12);
        }

        Park();

        // SizeToContent means the final height is not known until it has been laid out,
        // so park again once it is.
        window.SizeChanged += (_, _) => Park();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tick?.Stop();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _database?.Dispose();
        base.OnExit(e);
    }
}
