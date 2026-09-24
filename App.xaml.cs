using System.Windows;
using System.Drawing;
using System.Media;
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
    // One popup + one modal overlay per monitor; created fresh on each popup so the
    // set of monitors and the "all monitors" setting are always honored.
    private readonly List<(ReminderWindow Window, ModalOverlayWindow Overlay)> _popups = new();

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

        _manager.ActiveChanged += active =>
        {
            if (active) PopUpReminders();
            else HideReminders();
        };
        _manager.StatusChanged += status =>
        {
            if (_tray != null) _tray.Text = Truncate("Gmail Calendar Notifier\n" + status, 127);
        };
        // Authorization revoked/broken → open Settings so the user re-authorizes instead of
        // silently missing meetings. Raised on every failed poll; while the Settings dialog is
        // open the dispatcher is blocked, so the dialog pops again after being closed if the
        // problem is still there.
        _manager.AuthFailed += message => ShowSettings(
            "Google authorization is no longer working — sign in again to keep receiving " +
            "reminders.\n\n" + message);

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

    /// <summary>
    /// Create one reminder window (behind a full-screen modal overlay) per monitor —
    /// or just the primary monitor when the setting is off — and pop them all.
    /// All windows bind the same collection, so acting on one updates them all.
    /// </summary>
    private void PopUpReminders()
    {
        if (_manager == null) return;

        HideReminders(); // rebuild in case monitors or the setting changed

        var primary = WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];
        var screens = _settings.ShowOnAllMonitors
            ? WinForms.Screen.AllScreens
            : new[] { primary };

        foreach (var screen in screens)
        {
            var window = new ReminderWindow(_manager, screen);
            var overlay = new ModalOverlayWindow(screen, window);
            _popups.Add((window, overlay));
        }

        // Show each overlay first, then make the popup its OWNED window — owned windows
        // always render above their owner, so the popup stays on top of the dimmed
        // overlay. (The reverse — overlay owning an unshown popup — throws at Show().)
        foreach (var (window, overlay) in _popups)
        {
            overlay.Show();
            window.Owner = overlay;
            window.PopUp();
        }
        _popups[0].Window.Activate();

        if (_settings.PlaySound)
        {
            try { SystemSounds.Exclamation.Play(); } catch { /* ignore */ }
        }
    }

    private void HideReminders()
    {
        foreach (var (window, overlay) in _popups)
        {
            try
            {
                window.ForceClose = true;
                window.Close();
            }
            catch { /* ignore */ }
            try { overlay.Close(); } catch { /* ignore */ }
        }
        _popups.Clear();
    }

    private void ShowReminders()
    {
        if (_manager == null) return;

        if (_manager.Active.Count > 0)
        {
            PopUpReminders();
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

    private bool ShowSettings(string? alert = null)
    {
        var win = new SettingsWindow(_settings, _oauth!, alert) { Topmost = true };
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
        HideReminders();
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
