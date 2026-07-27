using Microsoft.Win32;

namespace GmailCalendarNotifier;

/// <summary>Toggles the app in the per-user Run key so it launches at login.</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GmailCalendarNotifier";

    public static void Apply(bool enabled)
    {
        try
        {
            // CreateSubKey opens the (normally existing) Run key for writing, creating it if absent.
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key == null) return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\""); // overwrite → self-heals a moved exe
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch { /* non-fatal */ }
    }

    /// <summary>Current Run-key value for this app (for verification), or null if not set.</summary>
    public static string? CurrentValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) as string;
        }
        catch { return null; }
    }
}
