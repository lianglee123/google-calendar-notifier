using System.Media;
using System.Windows;
using System.Windows.Threading;

namespace GmailCalendarNotifier;

public partial class ReminderWindow : Window
{
    private readonly ReminderManager _manager;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _refreshTimer;

    // Snooze choices, in minutes.
    private static readonly (string Label, int Minutes)[] SnoozeOptions =
    {
        ("1 minute", 1),
        ("5 minutes", 5),
        ("10 minutes", 10),
        ("15 minutes", 15),
        ("30 minutes", 30),
        ("1 hour", 60),
        ("2 hours", 120),
        ("1 day", 1440),
    };

    public ReminderWindow(ReminderManager manager, AppSettings settings)
    {
        InitializeComponent();
        _manager = manager;
        _settings = settings;

        ReminderList.ItemsSource = _manager.Active;
        _manager.Active.CollectionChanged += (_, _) => UpdateHeader();

        foreach (var opt in SnoozeOptions) SnoozeCombo.Items.Add(opt.Label);
        SnoozeCombo.SelectedIndex = 1; // default 5 minutes

        // Live-update the "Due in X" countdown text.
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _refreshTimer.Tick += (_, _) => RefreshTexts();
        _refreshTimer.Start();

        // Closing the window just hides it — reminders stay pending.
        Closing += (e, args) => { args.Cancel = true; HideWindow(); };
    }

    private void RefreshTexts()
    {
        foreach (var item in _manager.Active) item.RefreshDisplay();
    }

    private void UpdateHeader()
    {
        int n = _manager.Active.Count;
        HeaderText.Text = n == 1 ? "1 Reminder" : $"{n} Reminders";
        if (ReminderList.SelectedIndex < 0 && n > 0) ReminderList.SelectedIndex = 0;
    }

    /// <summary>Pop the window to the front, centered on the primary screen, and play the sound.</summary>
    public void PopUp()
    {
        RefreshTexts();
        UpdateHeader();

        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        // Center after Show so the size (SizeToContent height) is known.
        CenterOnPrimary();
        Topmost = true;
        Activate();
        Focus();

        if (_settings.PlaySound)
        {
            try { SystemSounds.Exclamation.Play(); } catch { /* ignore */ }
        }
    }

    public void HideWindow() => Hide();

    /// <summary>Open a clicked location link in the default browser.</summary>
    private void Location_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    /// <summary>Open the clicked reminder's event in the Google Calendar web UI.</summary>
    private void OpenInCalendar_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ReminderItem item && item.HasWebUrl)
            OpenUrl(item.WebUrl);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    private void CenterOnPrimary()
    {
        // Primary screen's work area (excludes the taskbar).
        var area = SystemParameters.WorkArea;
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        Left = area.Left + (area.Width - w) / 2;
        Top = area.Top + (area.Height - h) / 2;
    }

    private int SelectedSnoozeMinutes()
    {
        int idx = SnoozeCombo.SelectedIndex;
        if (idx < 0 || idx >= SnoozeOptions.Length) idx = 1;
        return SnoozeOptions[idx].Minutes;
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        int minutes = SelectedSnoozeMinutes();
        var targets = SelectedOrAll();
        foreach (var item in targets) _manager.Snooze(item, minutes);
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        var targets = SelectedOrAll();
        foreach (var item in targets) _manager.Dismiss(item);
    }

    private void DismissAll_Click(object sender, RoutedEventArgs e) => _manager.DismissAll();

    /// <summary>Act on the selected reminders, or all of them if nothing is selected.</summary>
    private List<ReminderItem> SelectedOrAll()
    {
        if (ReminderList.SelectedItems.Count > 0)
            return ReminderList.SelectedItems.Cast<ReminderItem>().ToList();
        return _manager.Active.ToList();
    }
}
