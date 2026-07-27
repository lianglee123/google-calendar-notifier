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
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                if (key.GetValue(ValueName) != null)
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch { /* non-fatal */ }
    }
}
