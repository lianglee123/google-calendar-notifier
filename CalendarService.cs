using System.Net.Http;
using System.Text.Json;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace GmailCalendarNotifier;

/// <summary>
/// Reads upcoming events from Google Calendar. In built-in (Thunderbird) mode it uses CalDAV +
/// Ical.Net; in custom-OAuth-client mode it uses the Calendar REST API.
/// </summary>
public class CalendarService
{
    private readonly AppSettings _settings;
    private readonly OAuthService _oauth;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public CalendarService(AppSettings settings, OAuthService oauth)
    {
        _settings = settings;
        _oauth = oauth;
    }

    /// <summary>
    /// Fetch every reminder whose occurrence starts within [now - grace, now + lookaheadHours].
    /// Throws on auth/network failure.
    /// </summary>
    public async Task<List<ReminderItem>> FetchRemindersAsync(int lookaheadHours = 26, CancellationToken ct = default)
    {
        return _settings.UseCustomOAuthClient
            ? await FetchViaRestAsync(lookaheadHours, ct)
            : await FetchViaCaldavAsync(lookaheadHours, ct);
    }

    // ---------- CalDAV path (built-in Thunderbird client) ----------

    private async Task<List<ReminderItem>> FetchViaCaldavAsync(int lookaheadHours, CancellationToken ct)
    {
        var token = await _oauth.GetAccessTokenAsync(ct);
        var calendarId = await CaldavClient.GetPrimaryCalendarIdAsync(token, ct);
        RememberAccount(calendarId);

        var now = DateTime.Now;
        var vcals = await CaldavClient.ReportEventsAsync(
            token, calendarId, now.AddHours(-2).ToUniversalTime(), now.AddHours(lookaheadHours).ToUniversalTime(), ct);

        return ParseVCalendars(vcals, now, lookaheadHours, calendarId);
    }

    // ---------- REST path (user's own OAuth client) ----------

    private async Task<List<ReminderItem>> FetchViaRestAsync(int lookaheadHours, CancellationToken ct)
    {
        var token = await _oauth.GetAccessTokenAsync(ct);
        var now = DateTime.Now;
        var timeMin = now.AddHours(-2).ToUniversalTime();
        var timeMax = now.AddHours(lookaheadHours).ToUniversalTime();

        var calendarIds = await GetRestCalendarIdsAsync(token, ct);
        if (calendarIds.Contains("primary")) RememberAccount("primary");

        var results = new List<ReminderItem>();
        foreach (var calId in calendarIds)
        {
            var url =
                $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calId)}/events" +
                $"?singleEvents=true&orderBy=startTime&showDeleted=false&maxResults=250" +
                $"&timeMin={Uri.EscapeDataString(Rfc3339(timeMin))}&timeMax={Uri.EscapeDataString(Rfc3339(timeMax))}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new("Bearer", token);
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) continue;

            results.AddRange(ParseRestEvents(await resp.Content.ReadAsStringAsync(ct), now));
        }
        return results;
    }

    /// <summary>Parse one events.list JSON payload into reminders (REST path). Testable in isolation.</summary>
    public List<ReminderItem> ParseRestEvents(string json, DateTime now)
    {
        var results = new List<ReminderItem>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items)) return results;

        foreach (var ev in items.EnumerateArray())
        {
            if (RestStr(ev, "status") == "cancelled") continue;
            if (!ev.TryGetProperty("start", out var start)) continue;
            if (!start.TryGetProperty("dateTime", out var startDt)) continue; // skip all-day
            if (!DateTimeOffset.TryParse(startDt.GetString(), out var startOffset)) continue;

            DateTime startLocal = startOffset.LocalDateTime;
            DateTime endLocal = startLocal;
            if (ev.TryGetProperty("end", out var end) &&
                end.TryGetProperty("dateTime", out var endDt) &&
                DateTimeOffset.TryParse(endDt.GetString(), out var endOffset))
                endLocal = endOffset.LocalDateTime;

            if (endLocal < now) continue;

            results.Add(new ReminderItem
            {
                Key = $"{RestStr(ev, "id")}|{startLocal:o}",
                Title = string.IsNullOrWhiteSpace(RestStr(ev, "summary")) ? "(No title)" : RestStr(ev, "summary"),
                Location = RestStr(ev, "location"),
                Description = RestStr(ev, "description"),
                HtmlLink = RestStr(ev, "htmlLink"),
                Start = startLocal,
                End = endLocal,
                ReminderTime = startLocal.AddMinutes(-RestLeadMinutesFor(ev)),
            });
        }
        return results;
    }

    private int RestLeadMinutesFor(JsonElement ev)
    {
        if (_settings.UseEventAlarms &&
            ev.TryGetProperty("reminders", out var rem) &&
            rem.TryGetProperty("overrides", out var overrides) &&
            overrides.ValueKind == JsonValueKind.Array && overrides.GetArrayLength() > 0)
        {
            int? maxLead = null;
            foreach (var o in overrides.EnumerateArray())
                if (o.TryGetProperty("minutes", out var m) && m.TryGetInt32(out var mins) && mins >= 0)
                    maxLead = maxLead.HasValue ? Math.Max(maxLead.Value, mins) : mins;
            if (maxLead.HasValue) return maxLead.Value;
        }
        return Math.Max(0, _settings.DefaultReminderMinutes);
    }

    private async Task<List<string>> GetRestCalendarIdsAsync(string token, CancellationToken ct)
    {
        var ids = new List<string>();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://www.googleapis.com/calendar/v3/users/me/calendarList");
            req.Headers.Authorization = new("Bearer", token);
            using var resp = await Http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("items", out var items))
                    foreach (var c in items.EnumerateArray())
                    {
                        bool selected = !c.TryGetProperty("selected", out var sel) || sel.GetBoolean();
                        if (selected && c.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } s)
                            ids.Add(s);
                    }
            }
        }
        catch { /* fall through */ }
        if (ids.Count == 0) ids.Add("primary");
        return ids;
    }

    private void RememberAccount(string calendarId)
    {
        if (!string.IsNullOrEmpty(calendarId) && calendarId != "primary" && _settings.AccountEmail != calendarId)
        {
            _settings.AccountEmail = calendarId;
            try { _settings.Save(); } catch { /* non-fatal */ }
        }
    }

    private static string RestStr(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static string Rfc3339(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    /// <summary>Expand a set of single-event VCALENDAR strings into reminders. Testable in isolation.</summary>
    public List<ReminderItem> ParseVCalendars(
        IEnumerable<string> vcalendars, DateTime now, int lookaheadHours = 26, string calendarId = "")
    {
        var windowStart = now.AddHours(-2);
        var windowEnd = now.AddHours(lookaheadHours);
        var results = new List<ReminderItem>();
        var seen = new HashSet<string>();

        foreach (var text in vcalendars)
        {
            Calendar calendar;
            try { calendar = Calendar.Load(text); }
            catch { continue; }

            foreach (var ev in calendar.Events)
            {
                if (ev.Start == null) continue;
                if (!ev.Start.HasTime) continue; // skip all-day events

                IEnumerable<Occurrence> occurrences;
                try { occurrences = ev.GetOccurrences(windowStart, windowEnd); }
                catch { continue; }

                int leadMinutes = LeadMinutesFor(ev);

                foreach (var occ in occurrences)
                {
                    // Convert via UTC to the machine's local zone. AsSystemLocal does NOT convert
                    // from the event's own time zone, so it would show the source wall-clock.
                    DateTime start = occ.Period.StartTime.AsUtc.ToLocalTime();
                    DateTime end = occ.Period.EndTime?.AsUtc.ToLocalTime() ?? start;
                    if (end < now) continue;

                    var key = $"{ev.Uid}|{start:o}";
                    if (!seen.Add(key)) continue; // de-dupe across calendar-data entries

                    results.Add(new ReminderItem
                    {
                        Key = key,
                        Title = string.IsNullOrWhiteSpace(ev.Summary) ? "(No title)" : ev.Summary.Trim(),
                        Location = ev.Location?.Trim() ?? "",
                        Description = ev.Description?.Trim() ?? "",
                        Uid = ev.Uid ?? "",
                        CalendarId = calendarId,
                        Start = start,
                        End = end,
                        ReminderTime = start.AddMinutes(-leadMinutes),
                    });
                }
            }
        }

        return results;
    }

    /// <summary>Minutes before start: from the event's earliest alarm, else the configured default.</summary>
    private int LeadMinutesFor(CalendarEvent ev)
    {
        if (_settings.UseEventAlarms && ev.Alarms is { Count: > 0 })
        {
            int? maxLead = null;
            foreach (var alarm in ev.Alarms)
            {
                var dur = alarm.Trigger?.Duration;
                if (dur.HasValue)
                {
                    int lead = (int)Math.Round(-dur.Value.TotalMinutes); // pre-start triggers are negative
                    if (lead >= 0)
                        maxLead = maxLead.HasValue ? Math.Max(maxLead.Value, lead) : lead;
                }
            }
            if (maxLead.HasValue) return maxLead.Value;
        }
        return Math.Max(0, _settings.DefaultReminderMinutes);
    }
}
