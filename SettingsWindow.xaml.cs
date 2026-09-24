using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace GmailCalendarNotifier;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly OAuthService _oauth;

    public SettingsWindow(AppSettings settings, OAuthService oauth)
    {
        InitializeComponent();
        _settings = settings;
        _oauth = oauth;

        ReminderBox.Text = settings.DefaultReminderMinutes.ToString();
        PollBox.Text = settings.PollIntervalMinutes.ToString();
        UseAlarmsCheck.IsChecked = settings.UseEventAlarms;
        SoundCheck.IsChecked = settings.PlaySound;
        AllMonitorsCheck.IsChecked = settings.ShowOnAllMonitors;
        StartupCheck.IsChecked = settings.RunAtStartup;

        CustomRadio.IsChecked = settings.UseCustomOAuthClient;
        BuiltInRadio.IsChecked = !settings.UseCustomOAuthClient;
        ClientIdBox.Text = settings.OAuthClientId;
        ClientSecretBox.Text = settings.OAuthClientSecret;
        UpdateCustomFieldsVisibility();

        UpdateAccountUi();
    }

    private void OAuthMode_Changed(object sender, RoutedEventArgs e) => UpdateCustomFieldsVisibility();

    private void UpdateCustomFieldsVisibility()
    {
        if (CustomFields != null)
            CustomFields.Visibility = CustomRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateAccountUi()
    {
        bool signedIn = _settings.IsSignedIn;
        AccountText.Text = signedIn
            ? (string.IsNullOrEmpty(_settings.AccountEmail) ? "Signed in" : $"Signed in as {_settings.AccountEmail}")
            : "Not signed in";
        SignInButton.Content = signedIn ? "Re-authorize" : "Sign in with Google";
        SignOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Opening your browser to sign in…", Colors.Gray);
        SignInButton.IsEnabled = false;
        try
        {
            var email = await _oauth.SignInAsync();
            _settings.Save();
            UpdateAccountUi();
            SetStatus($"Signed in{(string.IsNullOrEmpty(email) ? "" : " as " + email)}.", Colors.Green);
        }
        catch (Exception ex)
        {
            SetStatus("Sign-in failed: " + ex.Message, Colors.Firebrick);
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _oauth.SignOut();
        _settings.Save();
        UpdateAccountUi();
        SetStatus("Signed out.", Colors.Gray);
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.IsSignedIn)
        {
            SetStatus("Sign in first, then test.", Colors.Firebrick);
            return;
        }
        SetStatus("Testing…", Colors.Gray);
        try
        {
            var reminders = await new CalendarService(_settings, _oauth).FetchRemindersAsync();
            SetStatus($"Success — found {reminders.Count} upcoming event(s) in the next day.", Colors.Green);
        }
        catch (Exception ex)
        {
            SetStatus("Failed: " + ex.Message, Colors.Firebrick);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.DefaultReminderMinutes = ParseInt(ReminderBox.Text, _settings.DefaultReminderMinutes);
        _settings.PollIntervalMinutes = Math.Max(1, ParseInt(PollBox.Text, _settings.PollIntervalMinutes));
        _settings.UseEventAlarms = UseAlarmsCheck.IsChecked == true;
        _settings.PlaySound = SoundCheck.IsChecked == true;
        _settings.ShowOnAllMonitors = AllMonitorsCheck.IsChecked == true;
        _settings.RunAtStartup = StartupCheck.IsChecked == true;

        // Detect a change to the OAuth client — if it changed, the stored token is for the old
        // client and must be discarded so the user re-signs in with the new one.
        bool newUseCustom = CustomRadio.IsChecked == true;
        string newId = ClientIdBox.Text.Trim();
        string newSecret = ClientSecretBox.Text.Trim();
        bool clientChanged = newUseCustom != _settings.UseCustomOAuthClient
                             || newId != _settings.OAuthClientId
                             || newSecret != _settings.OAuthClientSecret;

        _settings.UseCustomOAuthClient = newUseCustom;
        _settings.OAuthClientId = newId;
        _settings.OAuthClientSecret = newSecret;

        if (clientChanged && _settings.IsSignedIn)
        {
            _oauth.SignOut();
            SetStatus("Sign-in method changed — please sign in again.", Colors.DarkOrange);
        }

        _settings.Save();
        StartupManager.Apply(_settings.RunAtStartup);

        // If the client changed, keep the window open so the user can re-authorize now.
        if (clientChanged)
        {
            UpdateAccountUi();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _settings.IsSignedIn;
        Close();
    }

    private void SetStatus(string text, Color color)
    {
        StatusLabel.Text = text;
        StatusLabel.Foreground = new SolidColorBrush(color);
        StatusLabel.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Log_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Log.LogPath;
            if (System.IO.File.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            else
                SetStatus("No log yet — try signing in first. Log path: " + path, Colors.Gray);
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't open log: " + ex.Message, Colors.Firebrick);
        }
    }

    private static int ParseInt(string text, int fallback)
        => int.TryParse(text.Trim(), out var v) ? v : fallback;
}
