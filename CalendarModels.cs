using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace GmailCalendarNotifier;

/// <summary>A single reminder for one occurrence of a calendar event.</summary>
public class ReminderItem : INotifyPropertyChanged
{
    /// <summary>Stable identity: event UID + occurrence start. Used to de-duplicate across polls.</summary>
    public required string Key { get; init; }

    public required string Title { get; init; }
    public string Location { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>iCal UID of the event (e.g. "abc123@google.com").</summary>
    public string Uid { get; init; } = "";

    /// <summary>The calendar this event lives on (the account email for the primary calendar).</summary>
    public string CalendarId { get; init; } = "";

    /// <summary>Canonical event web link when known (e.g. the REST API's htmlLink). Overrides the computed URL.</summary>
    public string HtmlLink { get; init; } = "";

    /// <summary>Local start time of this occurrence.</summary>
    public DateTime Start { get; init; }

    /// <summary>Local end time of this occurrence.</summary>
    public DateTime End { get; init; }

    /// <summary>Local time at which this reminder should first pop.</summary>
    public DateTime ReminderTime { get; set; }

    /// <summary>True once the user dismisses it (so it never comes back this run).</summary>
    public bool Dismissed { get; set; }

    /// <summary>True while it is currently shown in the reminder window.</summary>
    public bool Active { get; set; }

    // ---- display helpers (bound by the window) ----

    private static readonly Regex UrlRx =
        new(@"https?://[^\s<>""]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Known conferencing hosts, preferred over stray URLs (maps, unsubscribe links, etc.).
    private static readonly string[] MeetingHosts =
        { "meet.google.com", "zoom.us", "teams.microsoft.com", "teams.live.com",
          "webex.com", "meet.jit.si", "whereby.com", "gotomeeting.com", "bluejeans.com" };

    /// <summary>The first URL found in the location, or "" if none.</summary>
    public string LocationUrl
    {
        get { var m = UrlRx.Match(Location ?? ""); return m.Success ? m.Value : ""; }
    }

    /// <summary>Best meeting URL in the description: a known conferencing host if present, else the first URL.</summary>
    public string DescriptionUrl
    {
        get
        {
            var urls = UrlRx.Matches(Description ?? "").Select(m => m.Value).ToList();
            var preferred = urls.FirstOrDefault(u =>
                MeetingHosts.Any(h => u.Contains(h, StringComparison.OrdinalIgnoreCase)));
            return preferred ?? urls.FirstOrDefault() ?? "";
        }
    }

    /// <summary>The link to open: the location's URL if it has one, otherwise a link from the description.</summary>
    public string LinkUrl => LocationUrl.Length > 0 ? LocationUrl : DescriptionUrl;

    /// <summary>There is a clickable link (from location or description).</summary>
    public bool HasLink => LinkUrl.Length > 0;

    /// <summary>Text for the link line: the location when it's the link, else a generic "Join meeting".</summary>
    public string LinkText => LocationUrl.Length > 0 ? Location : "Join meeting";

    /// <summary>Location is present but is plain text (no URL) — render as a label, not a link.</summary>
    public bool ShowPlainLocation => !string.IsNullOrWhiteSpace(Location) && LocationUrl.Length == 0;

    /// <summary>
    /// Link to open this event in the Google Calendar web UI. Google's event URL uses
    /// eid = base64("{eventId} {calendarId}"), where eventId is the UID without its @domain.
    /// </summary>
    public string WebUrl
    {
        get
        {
            if (!string.IsNullOrEmpty(HtmlLink)) return HtmlLink;
            if (string.IsNullOrEmpty(Uid) || string.IsNullOrEmpty(CalendarId)) return "";
            var eventId = Uid;
            int at = eventId.IndexOf('@');
            if (at > 0) eventId = eventId.Substring(0, at);
            var eid = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{eventId} {CalendarId}")).TrimEnd('=');
            return $"https://www.google.com/calendar/event?eid={eid}";
        }
    }

    public bool HasWebUrl => WebUrl.Length > 0;

    /// <summary>e.g. "Tomorrow 9:00 AM" or "Fri 2:30 PM".</summary>
    public string WhenText
    {
        get
        {
            var today = DateTime.Today;
            string day = Start.Date == today ? "Today"
                : Start.Date == today.AddDays(1) ? "Tomorrow"
                : Start.ToString("ddd M/d");
            return $"{day} {Start:t}";
        }
    }

    /// <summary>Live countdown, Outlook-style: "Starts in 5 minutes" / "Overdue 12 minutes".</summary>
    public string StatusText
    {
        get
        {
            var now = DateTime.Now;
            if (Start > now)
                return "Due in " + Friendly(Start - now);
            if (End > now)
                return "Now (started " + Friendly(now - Start) + " ago)";
            return "Overdue " + Friendly(now - Start);
        }
    }

    private static string Friendly(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return "less than a minute";
        if (span.TotalMinutes < 60)
        {
            int m = (int)Math.Round(span.TotalMinutes);
            return $"{m} minute{(m == 1 ? "" : "s")}";
        }
        if (span.TotalHours < 24)
        {
            int h = (int)span.TotalHours;
            int m = span.Minutes;
            return m == 0 ? $"{h} hour{(h == 1 ? "" : "s")}" : $"{h} hr {m} min";
        }
        int d = (int)span.TotalDays;
        return $"{d} day{(d == 1 ? "" : "s")}";
    }

    /// <summary>Ask the UI to re-read the live text (called by the window's refresh timer).</summary>
    public void RefreshDisplay()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WhenText)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
