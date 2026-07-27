using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace GmailCalendarNotifier;

/// <summary>
/// Owns reminder state: polls the calendar on an interval, ticks every few seconds to fire
/// due reminders into the always-on-top window, and handles snooze/dismiss.
/// </summary>
public class ReminderManager
{
    private readonly AppSettings _settings;
    private readonly CalendarService _service;
    private readonly Dispatcher _dispatcher;

    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _pollTimer;

    // Every reminder we currently know about, keyed by stable identity.
    private readonly Dictionary<string, ReminderItem> _known = new();

    /// <summary>Reminders currently shown in the window. Bound directly by the UI.</summary>
    public ObservableCollection<ReminderItem> Active { get; } = new();

    /// <summary>Raised when reminders appear (true) or the list empties (false).</summary>
    public event Action<bool>? ActiveChanged;

    /// <summary>Raised with a human-readable status after each poll (for the tray tooltip).</summary>
    public event Action<string>? StatusChanged;

    public ReminderManager(AppSettings settings, OAuthService oauth, Dispatcher dispatcher)
    {
        _settings = settings;
        _service = new CalendarService(settings, oauth);
        _dispatcher = dispatcher;

        _tickTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _tickTimer.Tick += (_, _) => Tick();

        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMinutes(Math.Max(1, settings.PollIntervalMinutes))
        };
        _pollTimer.Tick += async (_, _) => await PollAsync();
    }

    public async void Start()
    {
        _tickTimer.Start();
        _pollTimer.Start();
        await PollAsync();
    }

    public void Stop()
    {
        _tickTimer.Stop();
        _pollTimer.Stop();
    }

    /// <summary>Force an immediate refresh (used by the tray "Refresh now" command).</summary>
    public async void RefreshNow() => await PollAsync();

    private async Task PollAsync()
    {
        if (!_settings.IsSignedIn)
        {
            StatusChanged?.Invoke("Not signed in — open Settings and sign in with Google.");
            return;
        }

        try
        {
            var fresh = await _service.FetchRemindersAsync();

            // Merge: keep existing state (dismissed / snoozed) for keys we already track,
            // add brand-new occurrences.
            foreach (var item in fresh)
            {
                if (!_known.ContainsKey(item.Key))
                    _known[item.Key] = item;
            }

            // Forget past items that are no longer returned and aren't currently shown,
            // so the dictionary doesn't grow forever.
            var stale = _known.Values
                .Where(k => !k.Active && k.End < DateTime.Now.AddHours(-2))
                .Select(k => k.Key)
                .ToList();
            foreach (var key in stale) _known.Remove(key);

            StatusChanged?.Invoke($"Last synced {DateTime.Now:t} — {_known.Count} upcoming.");
            Tick();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Sync failed: {ex.Message}");
        }
    }

    /// <summary>Move any due, non-dismissed reminders into the active window.</summary>
    private void Tick()
    {
        var now = DateTime.Now;
        bool added = false;

        foreach (var item in _known.Values)
        {
            if (item.Dismissed || item.Active) continue;
            if (now >= item.ReminderTime)
            {
                item.Active = true;
                InsertSorted(item);
                added = true;
            }
        }

        if (added)
            ActiveChanged?.Invoke(true);
    }

    private void InsertSorted(ReminderItem item)
    {
        // Keep the list ordered by start time, soonest first.
        int i = 0;
        while (i < Active.Count && Active[i].Start <= item.Start) i++;
        Active.Insert(i, item);
    }

    /// <summary>The soonest non-dismissed upcoming event we know about, or null if none.</summary>
    public ReminderItem? NextUpcoming()
    {
        var now = DateTime.Now;
        return _known.Values
            .Where(k => !k.Dismissed && k.End > now)
            .OrderBy(k => k.Start)
            .FirstOrDefault();
    }

    /// <summary>How many upcoming events we currently track.</summary>
    public int KnownCount => _known.Count;

    public void Snooze(ReminderItem item, int minutes)
    {
        item.Active = false;
        item.ReminderTime = DateTime.Now.AddMinutes(minutes);
        Active.Remove(item);
        if (Active.Count == 0) ActiveChanged?.Invoke(false);
    }

    public void Dismiss(ReminderItem item)
    {
        item.Active = false;
        item.Dismissed = true;
        Active.Remove(item);
        if (Active.Count == 0) ActiveChanged?.Invoke(false);
    }

    public void DismissAll()
    {
        foreach (var item in Active.ToList())
        {
            item.Active = false;
            item.Dismissed = true;
        }
        Active.Clear();
        ActiveChanged?.Invoke(false);
    }
}
