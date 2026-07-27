using System.IO;

namespace GmailCalendarNotifier;

/// <summary>Minimal append-only file logger at %APPDATA%\GmailCalendarNotifier\log.txt.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GmailCalendarNotifier", "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never throw */ }
    }

    public static void Exception(string context, Exception ex) =>
        Write($"ERROR {context}: {ex.GetType().Name}: {ex.Message}");
}
