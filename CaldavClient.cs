using System.Net.Http;
using System.Text;
using System.Xml.Linq;

namespace GmailCalendarNotifier;

/// <summary>
/// Minimal Google CalDAV client (the same API Thunderbird uses). Discovers the user's
/// calendar id via the principal, and pulls events in a time window as raw iCal text.
/// </summary>
public static class CaldavClient
{
    private const string Base = "https://apidata.googleusercontent.com/caldav/v2/";
    private static readonly XNamespace D = "DAV:";
    private static readonly XNamespace C = "urn:ietf:params:xml:ns:caldav";
    private static readonly HttpMethod Propfind = new("PROPFIND");
    private static readonly HttpMethod Report = new("REPORT");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// The signed-in user's primary calendar id (their email), discovered from the
    /// current-user-principal href. Throws on failure.
    /// </summary>
    public static async Task<string> GetPrimaryCalendarIdAsync(string token, CancellationToken ct = default)
    {
        const string body =
            "<d:propfind xmlns:d=\"DAV:\"><d:prop><d:current-user-principal/></d:prop></d:propfind>";

        var xml = await SendAsync(Propfind, Base, token, body, depth: 0, ct);
        var doc = XDocument.Parse(xml);

        var href = doc.Descendants(D + "current-user-principal")
            .Elements(D + "href").FirstOrDefault()?.Value;
        if (string.IsNullOrEmpty(href))
            throw new InvalidOperationException("CalDAV: could not find current-user-principal.");

        // href looks like: /caldav/v2/{url-encoded-id}/user
        var parts = href.Trim('/').Split('/');
        // ["caldav","v2","{id}","user"]
        var idPart = parts.Length >= 3 ? parts[2] : "";
        var id = Uri.UnescapeDataString(idPart);
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("CalDAV: principal href had no calendar id.");
        return id;
    }

    /// <summary>
    /// Return raw VCALENDAR strings for every event on the given calendar that overlaps
    /// [utcStart, utcEnd]. Each string is one event's full iCal (with its VTIMEZONE).
    /// </summary>
    public static async Task<List<string>> ReportEventsAsync(
        string token, string calendarId, DateTime utcStart, DateTime utcEnd, CancellationToken ct = default)
    {
        string Fmt(DateTime d) => d.ToString("yyyyMMdd'T'HHmmss'Z'");
        var body =
            "<c:calendar-query xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\">" +
            "<d:prop><d:getetag/><c:calendar-data/></d:prop>" +
            "<c:filter><c:comp-filter name=\"VCALENDAR\"><c:comp-filter name=\"VEVENT\">" +
            $"<c:time-range start=\"{Fmt(utcStart)}\" end=\"{Fmt(utcEnd)}\"/>" +
            "</c:comp-filter></c:comp-filter></c:filter></c:calendar-query>";

        var url = Base + Uri.EscapeDataString(calendarId) + "/events";
        var xml = await SendAsync(Report, url, token, body, depth: 1, ct);

        var doc = XDocument.Parse(xml);
        return doc.Descendants(C + "calendar-data")
            .Select(e => e.Value)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static async Task<string> SendAsync(
        HttpMethod method, string url, string token, string body, int depth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new("Bearer", token);
        req.Headers.Add("Depth", depth.ToString());
        req.Content = new StringContent(body, Encoding.UTF8, "application/xml");

        using var resp = await Http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode && (int)resp.StatusCode != 207)
        {
            Log.Write($"CalDAV {method} {url} -> {(int)resp.StatusCode}");
            throw new InvalidOperationException(
                $"CalDAV request failed ({(int)resp.StatusCode}). {Truncate(text, 300)}");
        }
        return text;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
