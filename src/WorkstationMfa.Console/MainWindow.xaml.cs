using System.IO;
using System.Windows;
using WorkstationMfa.Core.Models;
using WorkstationMfa.Engine;
using WorkstationMfa.Horizon;

namespace WorkstationMfa.Console;

public partial class MainWindow : Window
{
    private readonly ManagementEngine _engine;
    private readonly ConnectionProfile _profile;
    private AdminSession _session;

    public MainWindow(ManagementEngine engine, ConnectionProfile profile, AdminSession session)
    {
        InitializeComponent();
        _engine = engine;
        _profile = profile;
        _session = session;
        HeaderText.Text = profile.DisplayName;
        SessionText.Text = $"Signed in as {session.Username} @ {profile.PrivacyIdeaBaseUrl} (realm {profile.Realm})";
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            ActionStatusText.Text = "Loading…";
            if (_session.IsExpired)
            {
                MessageBox.Show("Session expired. Please sign in again.", "Workstation MFA", MessageBoxButton.OK, MessageBoxImage.Warning);
                SignOut_Click(this, new RoutedEventArgs());
                return;
            }

            var hz = _engine.Horizon.GetStatus();
            var local = _engine.Local.GetStatus();
            LocalStatusText.Text =
                $"Horizon: {hz.State} {hz.Version} | Local MFA: {(local.IsProvisioned ? local.BoundUpn : "not provisioned")} | Host: {Environment.MachineName}";

            var statuses = await _engine.ListUserStatusesAsync(_profile, _session).ConfigureAwait(true);
            UsersGrid.ItemsSource = statuses.Select(s => new UserRow
            {
                Username = s.User.Username,
                Status = s.Status.ToString(),
                TokenSummary = s.Tokens.Count == 0 ? "—" : string.Join(", ", s.Tokens.Select(t => t.Serial)),
                DisplayName = $"{s.User.GivenName} {s.User.Surname}".Trim(),
                Raw = s
            }).ToList();

            ActionStatusText.Text = $"Loaded {statuses.Count} users.";
        }
        catch (Exception ex)
        {
            ActionStatusText.Text = ex.Message;
        }
    }

    private async void InstallHorizon_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var media = ResolveMedia();
            if (!File.Exists(media))
            {
                MessageBox.Show($"Horizon installer not found:\n{media}", "Workstation MFA", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ActionStatusText.Text = "Installing Horizon Client…";
            var status = await _engine.Horizon.InstallOrUpgradeAsync(new HorizonInstallOptions
            {
                MediaPath = media,
                Broker = _profile.HorizonBroker
            }).ConfigureAwait(true);
            ActionStatusText.Text = $"Horizon: {status.State} {status.Version}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ActionStatusText.Text = ex.Message;
        }
    }

    private async void Provision_Click(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRow row)
        {
            MessageBox.Show("Select a user first.", "Workstation MFA");
            return;
        }

        var hasToken = row.Raw.Tokens.Count > 0;
        if (hasToken)
        {
            var confirm = MessageBox.Show(
                $"User {row.Username} already has token(s):\n{row.TokenSummary}\n\nReplace will DESTROY the previous token and force a new local enrollment. Continue?",
                "Replace token",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;
        }

        var password = PromptPassword();
        if (password is null)
            return;

        try
        {
            ActionStatusText.Text = "Provisioning…";
            var result = await _engine.OnboardAsync(new OnboardRequest
            {
                Profile = _profile,
                AdminUsername = _session.Username,
                AdminPassword = password,
                TargetUsername = row.Username,
                InstallHorizon = false,
                ReplaceToken = hasToken,
                Verify = true
            }).ConfigureAwait(true);

            ActionStatusText.Text = result.Message + (result.NewSerial is null ? "" : $" Serial={result.NewSerial}");
            if (!result.Success)
                MessageBox.Show(result.Message, "Workstation MFA", MessageBoxButton.OK, MessageBoxImage.Error);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ActionStatusText.Text = ex.Message;
        }
    }

    private async void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRow row)
        {
            MessageBox.Show("Select a user first.", "Workstation MFA");
            return;
        }

        var confirm = MessageBox.Show(
            $"Revoke managed TOTP for {row.Username} and clear local credential if bound?",
            "Revoke",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var clearLocal = string.Equals(_engine.Local.GetStatus().BoundUpn?.Split('@')[0], row.Username, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(_engine.Local.GetStatus().BoundUpn, $"{row.Username}@{_profile.Realm}", StringComparison.OrdinalIgnoreCase);

            var result = await _engine.RevokeAsync(_profile, _session, row.Username, clearLocal, whatIf: false)
                .ConfigureAwait(true);
            ActionStatusText.Text = result.Message;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ActionStatusText.Text = ex.Message;
        }
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        var login = new LoginWindow();
        login.Show();
        Close();
    }

    private string ResolveMedia()
    {
        var relative = _profile.HorizonMediaRelativePath
            ?? @"packages\horizon\VMware-Horizon-Client-2309-8.11.0-22660907.exe";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return Path.GetFullPath(relative);
    }

    private static string? PromptPassword()
    {
        var dialog = new PasswordPromptWindow();
        var ok = dialog.ShowDialog();
        return ok == true ? dialog.Password : null;
    }

    private sealed class UserRow
    {
        public string Username { get; set; } = "";
        public string Status { get; set; } = "";
        public string TokenSummary { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public UserTokenStatus Raw { get; set; } = null!;
    }
}
