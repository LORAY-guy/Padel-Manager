using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MessageBox.Avalonia.Enums;
using MsBox.Avalonia;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using Padel.Manager.Services;

namespace Padel.Manager.Views;

public partial class StartupDialog : Window
{
    private readonly DatasetRegistry _registry;
    private readonly ServerSession _session;

    public DatasetEntry? ChosenEntry { get; private set; }

    public StartupDialog() { InitializeComponent(); _registry = null!; _session = null!; }

    public StartupDialog(DatasetRegistry registry, ServerSession session)
    {
        InitializeComponent();
        _registry = registry;
        _session = session;
        RefreshList();
    }

    private async void ServerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new ServerBrowserDialog(_session);
        await dialog.ShowDialog(this);

        if (dialog.ChosenEntry is not null)
        {
            ChosenEntry = dialog.ChosenEntry;
            Close();
            return;
        }

        // The browse session may have registered/updated server datasets in recents.
        RefreshList();
    }

    private void RefreshList(string? selectPath = null)
    {
        var items = _registry.Datasets
            .OrderByDescending(d => d.LastOpenedUtc)
            .Select(d => new RecentItem(d))
            .ToList();

        RecentListBox.ItemsSource = items;
        EmptyStateText.IsVisible = items.Count == 0;

        if (selectPath is not null)
        {
            var toSelect = items.FirstOrDefault(i =>
                string.Equals(i.Entry.Path, selectPath, StringComparison.OrdinalIgnoreCase));
            if (toSelect is not null)
                RecentListBox.SelectedItem = toSelect;
        }
        else if (items.Count > 0)
        {
            var lastId = _registry.LastOpenedId;
            var preferred = lastId is not null ? items.FirstOrDefault(i => i.Entry.Id == lastId) : null;
            RecentListBox.SelectedItem = preferred ?? items[0];
        }

        UpdateOpenButton();
    }

    private void UpdateOpenButton()
    {
        var selected = RecentListBox.SelectedItem as RecentItem;
        var hasSelection = selected is not null;
        OpenRecentButton.IsEnabled = hasSelection;
        ShowLocationButton.IsEnabled = hasSelection && !selected!.IsServer;
        // Only local datasets can be uploaded to the server.
        UploadButton.IsEnabled = hasSelection && !selected!.IsServer;
    }

    private async void UploadButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (RecentListBox.SelectedItem is not RecentItem item || item.IsServer)
        {
            return;
        }

        if (!_session.HasToken)
        {
            await ShowMessageAsync(
                "Connectez-vous d'abord au serveur (bouton « Se connecter au serveur… ») avant de téléverser un dataset.",
                "Connexion requise", MsBoxIcon.Info);
            return;
        }

        if (!File.Exists(item.Entry.Path))
        {
            await ShowMessageAsync("Le fichier de ce dataset est introuvable.", "Erreur", MsBoxIcon.Error);
            return;
        }

        UploadButton.IsEnabled = false;
        try
        {
            var serverEntry = await _session.UploadLocalFileAsync(item.Entry);
            RefreshList();
            await ShowMessageAsync(
                $"Le dataset « {serverEntry.Name} » a été téléversé sur le serveur.\n\n" +
                "Il est maintenant accessible depuis n'importe quel poste connecté au serveur.",
                "Téléversement terminé", MsBoxIcon.Success);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(ex.Message, "Erreur de téléversement", MsBoxIcon.Error);
        }
        finally
        {
            UpdateOpenButton();
        }
    }

    private async Task ShowMessageAsync(string message, string title, MsBoxIcon icon)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, icon);
        await box.ShowWindowDialogAsync(this);
    }

    private void RecentListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => UpdateOpenButton();

    private void RecentListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RecentListBox.SelectedItem is RecentItem)
            OpenSelected();
    }

    private void OpenRecentButton_OnClick(object? sender, RoutedEventArgs e) => OpenSelected();

    private void ShowLocationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (RecentListBox.SelectedItem is RecentItem item)
            OpenFileLocation(item.Entry.Path);
    }

    private static void OpenFileLocation(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            else if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", filePath } });
            else
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(filePath) ?? filePath) { UseShellExecute = true });
        }
        catch { }
    }

    private void OpenSelected()
    {
        if (RecentListBox.SelectedItem is not RecentItem item)
            return;
        ChosenEntry = item.Entry;
        Close();
    }

    private void NewDatasetButton_OnClick(object? sender, RoutedEventArgs e)
    {
        NewDatasetPanel.IsVisible = true;
        NewDatasetNameTextBox.Text = string.Empty;
        NewDatasetNameTextBox.Focus();
    }

    private void CancelNewButton_OnClick(object? sender, RoutedEventArgs e)
        => NewDatasetPanel.IsVisible = false;

    private void NewDatasetNameTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            CreateDataset();
        else if (e.Key == Key.Escape)
            NewDatasetPanel.IsVisible = false;
    }

    private void CreateButton_OnClick(object? sender, RoutedEventArgs e) => CreateDataset();

    private void CreateDataset()
    {
        var name = NewDatasetNameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var path = _registry.BuildNewDatasetPath(name);
        var entry = new DatasetEntry { Name = name, Path = path };
        _registry.AddOrUpdate(entry);
        _registry.Save();

        NewDatasetPanel.IsVisible = false;
        ChosenEntry = entry;
        Close();
    }

    private async void OpenFileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Ouvrir un fichier dataset (.padel)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Dataset Padel") { Patterns = new[] { "*.padel" } } }
        });

        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        var existing = _registry.TryGetByPath(path);
        if (existing is null)
        {
            existing = new DatasetEntry
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Path = path
            };
            _registry.AddOrUpdate(existing);
            _registry.Save();
        }

        ChosenEntry = existing;
        Close();
    }

    public sealed class RecentItem
    {
        public RecentItem(DatasetEntry entry) => Entry = entry;

        public DatasetEntry Entry { get; }
        public string Name => Entry.Name;
        public bool IsServer => Entry.IsServer;

        public string LocationLabel => Entry.IsServer ? "Dataset serveur" : ShortPath;

        public string ShortPath
        {
            get
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Entry.Path.StartsWith(home, StringComparison.OrdinalIgnoreCase)
                    ? "~" + Entry.Path[home.Length..]
                    : Entry.Path;
            }
        }

        public string LastOpenedText => Entry.LastOpenedUtc == default
            ? "Jamais ouvert"
            : $"Ouvert : {Entry.LastOpenedUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
    }
}
