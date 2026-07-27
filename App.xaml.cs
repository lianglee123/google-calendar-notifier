using System.Windows;
using System.Drawing;
using WinForms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace GmailCalendarNotifier;

public partial class App : System.Windows.Application
{
    private System.Threading.Mutex? _singleInstance;
    private WinForms.NotifyIcon? _tray;
    private AppSettings _settings = new();
    private OAuthService? _oauth;
    private ReminderManager? _manager;
    private ReminderWindow? _reminderWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Log any unhandled exception instead of silently crashing; keep the tray alive
        // when it's safe to do so.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Exception("DispatcherUnhandled", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Exception("AppDomainUnhandled", args.ExceptionObject as Exception
                ?? new Exception(args.ExceptionObject?.ToString() ?? "unknown"));

        // Only one copy at a time.
        _singleInstance = new System.Threading.Mutex(true, "GmailCalendarNotifier.SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("Gmail Calendar Notifier is already running (see the system tray).",
                "Already running", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Keep running with no visible window; the tray icon is the app.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settings = AppSettings.Load();
        _oauth = new OAuthService(_settings);

        // Reconcile the Windows "launch at login" entry with the setting on every launch, so the
        // app registers (or unregisters) itself — and self-heals if the exe has moved.
        StartupManager.Apply(_settings.RunAtStartup);

        SetupTray();

        _manager = new ReminderManager(_settings, _oauth, Dispatcher);
        _reminderWindow = new ReminderWindow(_manager, _settings);

        _manager.ActiveChanged += active =>
        {
            if (active) _reminderWindow!.PopUp();
            else _reminderWindow!.HideWindow();
        };
        _manager.StatusChanged += status =>
        {
            if (_tray != null) _tray.Text = Truncate("Gmail Calendar Notifier\n" + status, 127);
        };

        // First run: not signed in yet → open settings so the user can sign in.
        if (!_settings.IsSignedIn)
            ShowSettings();

        _manager.Start();
    }

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Information,
            Visible = true,
            Text = "Gmail Calendar Notifier"
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Show reminders", null, (_, _) => ShowReminders());
        menu.Items.Add("Refresh now", null, (_, _) => _manager?.RefreshNow());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;

        _tray.DoubleClick += (_, _) => ShowReminders();
    }

    private void ShowReminders()
    {
        if (_reminderWindow == null || _manager == null) return;

        if (_manager.Active.Count > 0)
        {
            _reminderWindow.PopUp();
            return;
        }

        // Nothing due yet — tell the user what's next and when it will pop.
        var next = _manager.NextUpcoming();
        string msg;
        if (next == null)
        {
            msg = _manager.KnownCount == 0
                ? "No upcoming events found in the next day."
                : "No reminders are due yet.";
        }
        else
        {
            msg = $"Next: {next.Title} at {next.Start:t}.\n" +
                  $"Reminder will pop at {next.ReminderTime:t}.";
        }
        _tray?.ShowBalloonTip(6000, "Gmail Calendar Notifier", msg, WinForms.ToolTipIcon.Info);
    }

    private bool ShowSettings()
    {
        var win = new SettingsWindow(_settings, _oauth!) { Topmost = true };
        bool? result = win.ShowDialog();
        if (result == true)
        {
            _manager?.RefreshNow();
            return true;
        }
        return false;
    }

    private void ExitApp()
    {
        _manager?.Stop();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
