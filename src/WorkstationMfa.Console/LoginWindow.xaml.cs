using System.Windows;
using WorkstationMfa.Core.Models;
using WorkstationMfa.Engine;
using WorkstationMfa.Profiles;

namespace WorkstationMfa.Console;

public partial class LoginWindow : Window
{
    private readonly ManagementEngine _engine = new();
    private ResolvedProfile? _resolved;

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadProfilesAsync();
    }

    private async Task LoadProfilesAsync()
    {
        try
        {
            ErrorText.Text = string.Empty;
            var items = new List<ProfileItem>();

            foreach (var local in _engine.Profiles.ListLocalProfiles())
            {
                items.Add(new ProfileItem(local, ProfileSourceKind.Local, "(local)"));
            }

            _resolved = await _engine.Profiles.ResolveAsync().ConfigureAwait(true);
            if (!items.Any(i => i.Profile.Id == _resolved.Profile.Id))
            {
                items.Insert(0, new ProfileItem(_resolved.Profile, _resolved.SourceKind, _resolved.SourcePathOrUrl));
            }

            ProfileCombo.ItemsSource = items;
            ProfileCombo.SelectedIndex = 0;
            ProfileSourceText.Text = $"Source: {_resolved.SourceKind} — {_resolved.SourcePathOrUrl}";
            if (!string.IsNullOrWhiteSpace(_resolved.Warning))
                ErrorText.Text = _resolved.Warning;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadProfilesAsync();

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Text = string.Empty;
            if (ProfileCombo.SelectedItem is not ProfileItem item)
            {
                ErrorText.Text = "Select a profile.";
                return;
            }

            var password = PasswordBox.Password;
            if (string.IsNullOrWhiteSpace(UsernameBox.Text) || string.IsNullOrWhiteSpace(password))
            {
                ErrorText.Text = "Username and password are required.";
                return;
            }

            IsEnabled = false;
            var session = await _engine.AuthenticateAsync(item.Profile, UsernameBox.Text.Trim(), password)
                .ConfigureAwait(true);
            password = string.Empty;
            PasswordBox.Clear();

            var main = new MainWindow(_engine, item.Profile, session);
            main.Show();
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            IsEnabled = true;
        }
    }

    private sealed record ProfileItem(ConnectionProfile Profile, ProfileSourceKind Source, string Detail)
    {
        public string DisplayLabel => $"{Profile.DisplayName} [{Source}]";
    }
}
