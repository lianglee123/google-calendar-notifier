using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GmailCalendarNotifier;

/// <summary>
/// Google OAuth2 for an installed (desktop) app: PKCE + loopback redirect, exactly the style
/// Thunderbird uses. Holds a short-lived access token in memory and refreshes it from the
/// DPAPI-persisted refresh token as needed.
/// </summary>
public class OAuthService
{
    // Built-in Thunderbird installed-app client. For an "installed" client the secret is not
    // truly secret (it ships in the app); PKCE is what actually protects the exchange.
    private const string DefaultClientId =
        "406964657835-aq8lmia8j95dhl1a2bvharmfk3t1hgqj.apps.googleusercontent.com";
    private const string DefaultClientSecret = "kSmqreRr0qwBWJgbf5Y-PjSU";

    // Endpoints and scope match Thunderbird's Google config verbatim, so this app's requests
    // look identical to Thunderbird's to Google (and to your org's app-access allowlist).
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/auth";
    private const string TokenEndpoint = "https://www.googleapis.com/oauth2/v3/token";
    private const string Scope = "https://www.googleapis.com/auth/calendar";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly AppSettings _settings;
    private string? _accessToken;
    private DateTime _accessExpiresUtc = DateTime.MinValue;

    public OAuthService(AppSettings settings) => _settings = settings;

    private bool UseCustom => _settings.UseCustomOAuthClient;

    private string ClientId => UseCustom ? _settings.OAuthClientId.Trim() : DefaultClientId;
    private string ClientSecret => UseCustom ? _settings.OAuthClientSecret.Trim() : DefaultClientSecret;

    /// <summary>
    /// Run the interactive consent flow in the user's browser. On success, persists the refresh
    /// token and returns the signed-in account's email. Throws on failure/cancellation.
    /// </summary>
    public async Task<string> SignInAsync(CancellationToken ct = default)
    {
        if (UseCustom && (string.IsNullOrWhiteSpace(_settings.OAuthClientId) ||
                          string.IsNullOrWhiteSpace(_settings.OAuthClientSecret)))
            throw new InvalidOperationException(
                "Custom OAuth client is selected but the Client ID/Secret are empty. " +
                "Enter them in Settings, or switch back to the built-in client.");

        // PKCE
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        int port = FindFreePort();
        var redirectUri = $"http://127.0.0.1:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var authUrl =
            $"{AuthEndpoint}?client_id={Uri.EscapeDataString(ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&response_type=code" +
            $"&scope={Uri.EscapeDataString(Scope)}" +
            $"&code_challenge={challenge}&code_challenge_method=S256" +
            $"&access_type=offline&prompt=consent" +
            $"&state={state}";

        Log.Write($"OAuth: starting sign-in. client_id={ClientId}");
        Log.Write($"OAuth: redirect_uri={redirectUri}");
        Log.Write($"OAuth: authorization URL = {authUrl}");

        Process.Start(new ProcessStartInfo { FileName = authUrl, UseShellExecute = true });

        // Wait for the browser redirect (ignore stray requests like /favicon.ico).
        string? code = null;
        using (ct.Register(() => { try { listener.Stop(); } catch { } }))
        {
            while (code == null)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch { throw new OperationCanceledException("Sign-in was cancelled."); }

                var q = context.Request.QueryString;
                var err = q["error"];
                var gotState = q["state"];
                var gotCode = q["code"];

                if (err != null)
                {
                    var desc = q["error_description"];
                    Log.Write($"OAuth: redirect returned error={err} desc={desc}");
                    await RespondAsync(context, $"Sign-in failed: {err}. You can close this tab.");
                    throw new InvalidOperationException(
                        $"Google returned an error: {err}{(string.IsNullOrEmpty(desc) ? "" : " — " + desc)}");
                }

                if (gotCode != null)
                {
                    if (gotState != state)
                    {
                        await RespondAsync(context, "Sign-in failed (state mismatch). You can close this tab.");
                        throw new InvalidOperationException("OAuth state mismatch — possible interference.");
                    }
                    code = gotCode;
                    await RespondAsync(context,
                        "Signed in to Gmail Calendar Notifier. You can close this tab and return to the app.");
                }
                else
                {
                    await RespondAsync(context, "Waiting for authorization…");
                }
            }
        }

        // Exchange the code for tokens.
        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        };
        Log.Write("OAuth: received authorization code, exchanging for tokens…");
        using var resp = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            Log.Write($"OAuth: token exchange failed {(int)resp.StatusCode}: {body}");
            throw new InvalidOperationException($"Token exchange failed ({(int)resp.StatusCode}): {body}");
        }
        Log.Write("OAuth: token exchange OK.");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        if (string.IsNullOrEmpty(refreshToken))
            throw new InvalidOperationException(
                "Google did not return a refresh token. Try removing the app's access in your Google " +
                "Account permissions, then sign in again.");

        TokenStore.Save(refreshToken);
        CacheAccessToken(root);

        var email = await FetchPrimaryEmailAsync(ct);
        _settings.AccountEmail = email;
        return email;
    }

    public void SignOut()
    {
        TokenStore.Clear();
        _accessToken = null;
        _accessExpiresUtc = DateTime.MinValue;
        _settings.AccountEmail = "";
    }

    /// <summary>Return a valid access token, refreshing from the stored refresh token if needed.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_accessToken != null && DateTime.UtcNow < _accessExpiresUtc.AddSeconds(-60))
            return _accessToken;

        var refreshToken = TokenStore.Load()
            ?? throw new InvalidOperationException("Not signed in. Open Settings and sign in with Google.");

        var form = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        };
        using var resp = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Could not refresh access ({(int)resp.StatusCode}). You may need to sign in again. {body}");

        using var doc = JsonDocument.Parse(body);
        CacheAccessToken(doc.RootElement);
        return _accessToken!;
    }

    private void CacheAccessToken(JsonElement root)
    {
        _accessToken = root.GetProperty("access_token").GetString();
        int expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
        _accessExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn);
    }

    /// <summary>The primary calendar's id is the account's email address.</summary>
    private async Task<string> FetchPrimaryEmailAsync(CancellationToken ct)
    {
        try
        {
            var token = await GetAccessTokenAsync(ct);
            if (UseCustom)
            {
                // Custom client uses the REST API (its own project has the Calendar API enabled).
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    "https://www.googleapis.com/calendar/v3/calendars/primary");
                req.Headers.Authorization = new("Bearer", token);
                using var resp = await Http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) return "";
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
            }
            return await CaldavClient.GetPrimaryCalendarIdAsync(token, ct);
        }
        catch (Exception ex)
        {
            Log.Exception("FetchPrimaryEmail", ex);
            return "";
        }
    }

    private static async Task RespondAsync(HttpListenerContext ctx, string message)
    {
        var html = $"<html><body style='font-family:Segoe UI;padding:40px'><h3>{message}</h3></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static int FindFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
