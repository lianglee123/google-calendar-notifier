using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GmailCalendarNotifier;

/// <summary>
/// Persists the OAuth refresh token, encrypted with Windows DPAPI (CurrentUser scope) so it
/// can only be read back by this Windows account on this machine.
/// </summary>
public static class TokenStore
{
    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GmailCalendarNotifier");

    private static string TokenPath => Path.Combine(Dir, "token.dat");

    // Extra entropy mixed into the DPAPI blob.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GmailCalendarNotifier.v1");

    public static bool HasRefreshToken => File.Exists(TokenPath);

    public static void Save(string refreshToken)
    {
        Directory.CreateDirectory(Dir);
        var plain = Encoding.UTF8.GetBytes(refreshToken);
        var enc = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenPath, enc);
    }

    public static string? Load()
    {
        try
        {
            if (!File.Exists(TokenPath)) return null;
            var enc = File.ReadAllBytes(TokenPath);
            var plain = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null; // corrupt or created under a different user
        }
    }

    public static void Clear()
    {
        try { if (File.Exists(TokenPath)) File.Delete(TokenPath); }
        catch { /* ignore */ }
    }
}
