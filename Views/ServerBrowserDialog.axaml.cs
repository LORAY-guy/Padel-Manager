using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MessageBox.Avalonia.Enums;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using Padel.Manager.Services;
using Padel.Manager.Services.Server;

namespace Padel.Manager.Views;

public partial class ServerBrowserDialog : Window
{
    private readonly ServerSession _session;

    /// <summary>Set to the opened server dataset when the user picks one.</summary>
    public DatasetEntry? ChosenEntry { get; private set; }

    public ServerBrowserDialog()
    {
        InitializeComponent();
        _session = null!;
    }

    public ServerBrowserDialog(ServerSession session)
    {
        InitializeComponent();
        _session = session;

        if (!string.IsNullOrWhiteSpace(session.Url))
        {
            UrlTextBox.Text = session.Url;
        }

        // If we still have a saved token, try to list immediately.
        if (session.HasToken)
        {
            _ = LoadServerDatasetsAsync();
        }
    }

    private async void ConnectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text?.Trim();
        var username = UsernameTextBox.Text?.Trim();
        var password = PasswordTextBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus("Saisissez l'adresse du serveur.", error: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            SetStatus("Saisissez votre identifiant.", error: true);
            return;
        }

        ConnectButton.IsEnabled = false;
        SetStatus("Connexion…");
        try
        {
            _session.Configure(url);
            await _session.LoginAsync(username, password);
            await LoadServerDatasetsAsync();
        }
        catch (ServerAuthException ex)
        {
            SetStatus(ex.Message, error: true);
        }
        catch (ServerUnavailableException ex)
        {
            SetStatus(ex.Message, error: true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private async void RegisterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text?.Trim();
        var username = UsernameTextBox.Text?.Trim();
        var password = PasswordTextBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus("Saisissez l'adresse du serveur.", error: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            SetStatus("Choisissez un identifiant pour votre nouveau compte.", error: true);
            return;
        }

        RegisterButton.IsEnabled = false;
        ConnectButton.IsEnabled = false;
        SetStatus("Création du compte…");
        try
        {
            _session.Configure(url);
            await _session.RegisterAsync(username, password);
            await LoadServerDatasetsAsync();
            SetStatus($"Compte « {username} » créé. Vous êtes connecté.");
        }
        catch (ServerUnavailableException ex)
        {
            SetStatus(ex.Message, error: true);
        }
        catch (Exception ex)
        {
            // InvalidOperationException carries the server's reason (name taken, too short, …).
            SetStatus(ex.Message, error: true);
        }
        finally
        {
            RegisterButton.IsEnabled = true;
            ConnectButton.IsEnabled = true;
        }
    }

    private async Task LoadServerDatasetsAsync()
    {
        try
        {
            SetStatus("Chargement des datasets…");
            var datasets = await _session.ListAsync();
            var items = datasets.Select(d => new ServerItem(d)).ToList();
            ServerListBox.ItemsSource = items;
            ServerEmptyText.IsVisible = items.Count == 0;
            ServerEmptyText.Text = items.Count == 0
                ? "Aucun dataset sur le serveur pour le moment."
                : string.Empty;
            ChangePasswordButton.IsVisible = true;
            SetStatus(items.Count == 0 ? "Connecté." : $"Connecté — {items.Count} dataset(s).");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    private void ServerListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => OpenButton.IsEnabled = ServerListBox.SelectedItem is ServerItem;

    private void ServerListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ServerListBox.SelectedItem is ServerItem)
        {
            _ = OpenSelectedAsync();
        }
    }

    private void OpenButton_OnClick(object? sender, RoutedEventArgs e) => _ = OpenSelectedAsync();

    private async Task OpenSelectedAsync()
    {
        if (ServerListBox.SelectedItem is not ServerItem item)
        {
            return;
        }

        OpenButton.IsEnabled = false;
        SetStatus("Ouverture…");
        try
        {
            ChosenEntry = await _session.OpenAsync(item.Summary);
            Close();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
            OpenButton.IsEnabled = true;
            var box = MessageBoxManager.GetMessageBoxStandard("Erreur", ex.Message, ButtonEnum.Ok, MsBoxIcon.Error);
            await box.ShowWindowDialogAsync(this);
        }
    }

    private async void ChangePasswordButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new ChangePasswordDialog(_session);
        await dialog.ShowDialog(this);
        if (dialog.Changed)
        {
            SetStatus("Mot de passe modifié.");
        }
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close();

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        if (error)
        {
            StatusText.Foreground = Avalonia.Media.Brushes.IndianRed;
        }
        else if (this.TryFindResource("TextMuted", out var resource) && resource is Avalonia.Media.IBrush brush)
        {
            StatusText.Foreground = brush;
        }
        else
        {
            StatusText.Foreground = Avalonia.Media.Brushes.Gray;
        }
    }

    public sealed class ServerItem
    {
        public ServerItem(ServerDatasetSummary summary) => Summary = summary;

        public ServerDatasetSummary Summary { get; }
        public string Name => Summary.Name;
        public string SubText => $"Modifié : {Summary.UpdatedUtc.ToLocalTime():dd/MM/yyyy HH:mm} • v{Summary.Version}";
    }
}
