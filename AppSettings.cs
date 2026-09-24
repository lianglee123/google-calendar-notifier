using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GmailCalendarNotifier;

/// <summary>
/// User configuration, persisted as JSON in %APPDATA%\GmailCalendarNotifier\settings.json.
/// The OAuth refresh token is NOT stored here — see <see cref="TokenStore"/> (DPAPI-encrypted).
/// </summary>
public class AppSettings
{
    /// <summary>Default minutes-before-start to remind, used when an event has no reminder of its own.</summary>
    public int DefaultReminderMinutes { get; set; } = 5;

    /// <summary>How often to re-download the calendar, in minutes.</summary>
    public int PollIntervalMinutes { get; set; } = 5;

    /// <summary>Honor each event's own reminder overrides when present; otherwise always use the default.</summary>
    public bool UseEventAlarms { get; set; } = true;

    /// <summary>Play a sound when a reminder pops.</summary>
    public bool PlaySound { get; set; } = true;

    /// <summary>Show the reminder popup on every monitor (vs. only the primary one).</summary>
    public bool ShowOnAllMonitors { get; set; } = true;

    /// <summary>Start automatically when Windows starts (managed via Run registry key).</summary>
    public bool RunAtStartup { get; set; } = false;

    /// <summary>Email of the account most recently signed in (for display only).</summary>
    public string AccountEmail { get; set; } = "";

    /// <summary>
    /// When true, use the user's own Google OAuth client (below) and read via the Calendar
    /// REST API. When false (default), use the built-in Thunderbird client and read via CalDAV.
    /// </summary>
    public bool UseCustomOAuthClient { get; set; } = false;

    /// <summary>
    /// OAuth client id/secret for a user-supplied "Desktop app" client (used only when
    /// <see cref="UseCustomOAuthClient"/> is true). The built-in Thunderbird client needs neither.
    /// </summary>
    public string OAuthClientId { get; set; } = "";
    public string OAuthClientSecret { get; set; } = "";

    [JsonIgnore]
    public bool IsSignedIn => TokenStore.HasRefreshToken;

    // ---- persistence ----

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GmailCalendarNotifier");

    public static string ConfigPath => Path.Combine(ConfigDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null) return s;
            }
        }
        catch { /* fall through to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
