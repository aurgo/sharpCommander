using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Choosing a server and editing the list, in one window. Selecting a saved server fills the fields; Save writes
/// the fields back (adding a server when nothing is selected); Connect uses whatever is on screen, saved or not,
/// so a one-off connection needs no bookkeeping.
///
/// The password box is the one field that never reaches the settings file: it goes to the platform keychain on
/// Save, and is handed straight to the connection otherwise.
/// </summary>
public partial class SftpConnectDialog : Window
{
    private readonly ISettingsService? _settings;
    private readonly ISecretStore? _secrets;
    private readonly IDialogService? _dialogs;
    private bool _filling;

    /// <summary>The server to connect to; only meaningful when the dialog was accepted.</summary>
    public SftpSite? Result { get; private set; }

    /// <summary>The password typed for this connection, when there was one.</summary>
    public string? Password { get; private set; }

    public SftpConnectDialog()
    {
        InitializeComponent();
    }

    public SftpConnectDialog(ISettingsService settings, ISecretStore secrets, IDialogService dialogs)
        : this()
    {
        _settings = settings;
        _secrets = secrets;
        _dialogs = dialogs;
        Reload(settings.Settings.SftpSites.FirstOrDefault());
    }

    private void Reload(SftpSite? select)
    {
        if (_settings is null)
        {
            return;
        }

        _filling = true;
        SitesList.ItemsSource = _settings.Settings.SftpSites.ToList();
        _filling = false;

        SitesList.SelectedItem = _settings.Settings.SftpSites
            .FirstOrDefault(site => select is not null && site.CredentialKey == select.CredentialKey);

        if (SitesList.SelectedItem is null)
        {
            Fill(null);
        }
    }

    private void Sites_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_filling && SitesList.SelectedItem is SftpSite site)
        {
            Fill(site);
        }
    }

    private void Fill(SftpSite? site)
    {
        NameBox.Text = site?.Name ?? string.Empty;
        HostBox.Text = site?.Host ?? string.Empty;
        PortBox.Text = (site?.Port ?? 22).ToString();
        UserBox.Text = site?.Username ?? string.Empty;
        KeyBox.Text = site?.KeyPath ?? string.Empty;
        PathBox.Text = site?.InitialPath ?? string.Empty;
        KeyAuth.IsChecked = site is null || site.Authentication == SftpAuthentication.PrivateKey;
        PasswordAuth.IsChecked = site?.Authentication == SftpAuthentication.Password;

        // A stored password stays in the keychain; showing it again here would only put it back on screen.
        PasswordBox.Text = string.Empty;
        ErrorText.IsVisible = false;
    }

    /// <summary>Reads the form, or explains what is missing and returns null.</summary>
    private SftpSite? Read()
    {
        var host = (HostBox.Text ?? string.Empty).Trim();
        var user = (UserBox.Text ?? string.Empty).Trim();

        if (host.Length == 0 || user.Length == 0)
        {
            return Fail("Enter a host name and a user name.");
        }

        if (!int.TryParse((PortBox.Text ?? string.Empty).Trim(), out var port) || port is < 1 or > 65535)
        {
            return Fail("The port must be a number between 1 and 65535.");
        }

        return new SftpSite
        {
            Name = (NameBox.Text ?? string.Empty).Trim(),
            Host = host,
            Port = port,
            Username = user,
            Authentication = PasswordAuth.IsChecked == true ? SftpAuthentication.Password : SftpAuthentication.PrivateKey,
            KeyPath = (KeyBox.Text ?? string.Empty).Trim(),
            InitialPath = (PathBox.Text ?? string.Empty).Trim()
        };
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings is null || Read() is not { } site)
        {
            return;
        }

        var index = _settings.Settings.SftpSites.FindIndex(saved => saved.CredentialKey == site.CredentialKey);
        if (index >= 0)
        {
            _settings.Settings.SftpSites[index] = site;
        }
        else
        {
            _settings.Settings.SftpSites.Add(site);
        }

        _settings.RequestSave();

        // The password is the only thing that does not go in the settings file.
        if (PasswordBox.Text is { Length: > 0 } password && _secrets is { IsAvailable: true })
        {
            try
            {
                await _secrets.SetAsync(site.CredentialKey, password);
            }
            catch (Exception ex)
            {
                Fail($"The server was saved, but the password could not go into the keychain: {ex.Message}");
            }
        }

        Reload(site);
    }

    private async void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings is null || _dialogs is null || SitesList.SelectedItem is not SftpSite current)
        {
            return;
        }

        if (!await _dialogs.ShowConfirmAsync("SFTP", $"Remove '{current.DisplayName}'?", "Remove", "Cancel", destructive: true))
        {
            return;
        }

        _settings.Settings.SftpSites.RemoveAll(saved => saved.CredentialKey == current.CredentialKey);
        _settings.RequestSave();

        if (_secrets is { IsAvailable: true })
        {
            await _secrets.RemoveAsync(current.CredentialKey);
        }

        Reload(null);
    }

    /// <summary>
    /// Picks the private key from disk. It starts in ~/.ssh, where keys live, and shows every file: keys have no
    /// extension by convention, so filtering by one would hide exactly what the user came for.
    /// </summary>
    private async void BrowseKey_Click(object? sender, RoutedEventArgs e)
    {
        if (GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var start = Directory.Exists(ssh) ? await storage.TryGetFolderFromPathAsync(ssh) : null;

        var chosen = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Private key",
            AllowMultiple = false,
            SuggestedStartLocation = start
        });

        if (chosen.Count > 0 && chosen[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            KeyBox.Text = path;
        }
    }

    private void Sites_DoubleTapped(object? sender, TappedEventArgs e) => Accept();

    private void Ok_Click(object? sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        if (Read() is not { } site)
        {
            return;
        }

        Result = site;
        Password = PasswordBox.Text is { Length: > 0 } typed ? typed : null;
        Close(true);
    }

    private SftpSite? Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
        return null;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
