using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using MessageBox.Avalonia.Enums;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using Padel.Manager.Models;
using Padel.Manager.Services;
using Padel.Manager.Services.Server;
using Padel.Manager.Views;

namespace Padel.Manager;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private WorkbookService? _workbookService;
    private DatasetRegistry? _datasetRegistry;
    private DatasetRegistry? _preloadedRegistry;
    private DatasetEntry? _preloadedEntry;
    private ServerSession? _serverSession;
    private ServerSyncBinding? _syncBinding;
    private DatasetEntry? _currentEntry;
    private bool _scoreTabLoaded;
    private bool _playerStatsLoaded;
    private bool _hasUnsavedSheetChanges;
    private bool _suppressSheetChangeTracking;
    private bool _closingConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        GeneratedMatches.CollectionChanged += GeneratedMatches_OnCollectionChanged;
        Closing += MainWindow_OnClosing;
    }

    public MainWindow(DatasetRegistry registry, ServerSession serverSession, DatasetEntry entry) : this()
    {
        _preloadedRegistry = registry;
        _serverSession = serverSession;
        _preloadedEntry = entry;
    }

    public ObservableCollection<PlayerProfile> Players { get; } = new();
    public ObservableCollection<SelectablePlayer> PlayerPool { get; } = new();
    public ObservableCollection<SelectablePlayer> TournamentPlayerPool { get; } = new();
    public ObservableCollection<UiMatch> GeneratedMatches { get; } = new();
    public ObservableCollection<TournamentMatch> TournamentMatches { get; } = new();
    public ObservableCollection<PrintableRotationGroup> PrintableRotationGroups { get; } = new();
    public ObservableCollection<TournamentBracketRound> TournamentBracketRounds { get; } = new();
    public ObservableCollection<MatchHistoryEntry> MatchHistory { get; } = new();
    public ObservableCollection<PlayerScoreSummary> ScoreSummaries { get; } = new();

    public new event PropertyChangedEventHandler? PropertyChanged;

    internal WorkbookService? WorkbookService => _workbookService;

    internal bool ScoreTabLoaded
    {
        get => _scoreTabLoaded;
        set => _scoreTabLoaded = value;
    }

    internal bool PlayerStatsLoaded
    {
        get => _playerStatsLoaded;
        set => _playerStatsLoaded = value;
    }

    internal PlayersTabView PlayersTabViewControl => PlayersTab;
    internal MatchesTabView MatchesTabViewControl => MatchesTab;
    internal ScoresTabView ScoresTabViewControl => ScoresTab;
    internal LeaderboardTabView LeaderboardTabViewControl => LeaderboardTab;
    internal SettingsTabView SettingsTabViewControl => SettingsTab;

    internal bool AmericanoMode => _workbookService?.LoadAmericanoMode() ?? false;

    internal void SetAmericanoMode(bool value)
    {
        _workbookService?.SaveAmericanoMode(value);
        LeaderboardTab.RefreshLeaderboard();
    }

    internal bool DarkMode => _datasetRegistry?.DarkMode ?? false;

    internal void SetDarkMode(bool value)
    {
        if (_datasetRegistry is null) return;
        _datasetRegistry.SetDarkMode(value);
        _datasetRegistry.Save();
        Application.Current!.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _ = CheckForUpdatesAsync();
        if (_preloadedRegistry is null || _preloadedEntry is null)
            return;
        try
        {
            _datasetRegistry = _preloadedRegistry;
            await LoadDatasetAsync(_preloadedEntry);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Impossible de démarrer l'application.\n\n{ex.Message}", "Erreur de démarrage");
            Environment.Exit(1);
        }
    }

    private async Task LoadDatasetAsync(DatasetEntry entry)
    {
        // Tear down any previous server sync before swapping datasets.
        _syncBinding?.Dispose();
        _syncBinding = null;
        _currentEntry = entry;

        // Clear previous state
        Players.Clear();
        PlayerPool.Clear();
        TournamentPlayerPool.Clear();
        GeneratedMatches.Clear();
        TournamentMatches.Clear();
        PrintableRotationGroups.Clear();
        TournamentBracketRounds.Clear();
        MatchHistory.Clear();
        ScoreSummaries.Clear();
        _scoreTabLoaded = false;
        _playerStatsLoaded = false;

        var serverStatus = await PrepareServerDatasetAsync(entry);

        _workbookService = new WorkbookService(entry.Path, null);

        DatasetNameTextBlock.Text = entry.Name;
        WorkbookPathTextBlock.Text = entry.Path;
        ServerBadge.IsVisible = entry.IsServer;
        BackupButton.IsVisible = entry.IsServer;

        if (entry.IsServer && _serverSession is not null)
        {
            AttachServerSync(entry);
        }

        MatchesTab.InitializeDefaults();
        PlayersTab.ReloadPlayers(includeStats: false);
        SettingsTab.LoadSettings();
        MarkSheetAsSaved();
        SetStatus(serverStatus ?? $"Dataset « {entry.Name} » chargé. Chargement des statistiques en arrière-plan...");

        _ = WarmUpPlayerStatsAsync();
    }

    /// <summary>
    /// For a server dataset, pulls the latest copy into the local cache before it is
    /// opened. Returns a status message, or null for a normal local dataset. Throws
    /// only if the server is unreachable AND there is no local cache to fall back on.
    /// </summary>
    private async Task<string?> PrepareServerDatasetAsync(DatasetEntry entry)
    {
        if (!entry.IsServer || _serverSession is null)
        {
            return null;
        }

        try
        {
            await _serverSession.RefreshAsync(entry);
            return $"Dataset « {entry.Name} » à jour depuis le serveur.";
        }
        catch (ServerUnavailableException)
        {
            if (!File.Exists(entry.Path))
            {
                throw new InvalidOperationException(
                    "Le serveur est injoignable et aucune copie locale n'est disponible pour ce dataset.");
            }

            return $"Serveur injoignable — ouverture de la dernière copie locale de « {entry.Name} ».";
        }
        catch (ServerAuthException)
        {
            return File.Exists(entry.Path)
                ? "Session serveur expirée — ouverture de la copie locale. Reconnectez-vous pour synchroniser."
                : throw new InvalidOperationException("Session serveur expirée. Reconnectez-vous au serveur.");
        }
        catch (Exception)
        {
            // e.g. the dataset belongs to a different account now, or was deleted on the
            // server. Fall back to the local copy if we have one rather than failing.
            if (File.Exists(entry.Path))
            {
                return $"Ce dataset n'est pas accessible avec le compte actuel — ouverture de la copie locale de « {entry.Name} ».";
            }

            throw new InvalidOperationException(
                "Ce dataset n'est pas accessible avec le compte actuel et aucune copie locale n'est disponible.");
        }
    }

    private void AttachServerSync(DatasetEntry entry)
    {
        var binding = _serverSession!.CreateSyncBinding(entry, _workbookService!);
        binding.Status += s => Dispatcher.UIThread.Post(() => SetStatus(s));
        binding.Offline += _ => Dispatcher.UIThread.Post(() =>
            SetStatus("Hors ligne — vos modifications sont conservées localement et seront synchronisées à la reconnexion."));
        binding.Conflict += ex => Dispatcher.UIThread.Post(() => _ = HandleSyncConflictAsync(ex));
        _syncBinding = binding;
    }

    private async Task HandleSyncConflictAsync(ServerConflictException ex)
    {
        if (_serverSession is null || _currentEntry is null)
        {
            return;
        }

        // Always make a safety copy first so no local work can be lost either way.
        DatasetEntry? backup = null;
        try
        {
            backup = _serverSession.SaveLocalBackup(_currentEntry);
        }
        catch
        {
            // If the backup fails we still let the user choose; the cache file is intact.
        }

        var backupNote = backup is not null
            ? $"Une sauvegarde locale « {backup.Name} » a été créée par sécurité.\n\n"
            : string.Empty;

        var reload = await ShowYesNoAsync(
            "Ce dataset a été modifié sur le serveur depuis son ouverture.\n\n" +
            backupNote +
            "Oui : recharger la version du serveur (vos modifications locales seront remplacées).\n" +
            "Non : écraser le serveur avec votre version.",
            "Conflit de synchronisation");

        try
        {
            if (reload)
            {
                await LoadDatasetAsync(_currentEntry);
                SetStatus("Version du serveur rechargée.");
            }
            else
            {
                await _syncBinding!.OverwriteServerAsync(ex.ServerVersion);
                SetStatus("Le serveur a été écrasé avec votre version.");
            }
        }
        catch (Exception e)
        {
            await ShowErrorAsync(e.Message, "Erreur de résolution du conflit");
        }
    }

    private async void BackupButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_serverSession is null || _currentEntry is null || !_currentEntry.IsServer)
        {
            return;
        }

        try
        {
            var backup = _serverSession.SaveLocalBackup(_currentEntry);
            SetStatus($"Sauvegarde locale créée : {backup.Name}");
            await ShowInfoAsync(
                $"Une copie locale « {backup.Name} » a été enregistrée.\n\n" +
                "Elle apparaît dans vos datasets récents et s'ouvre sans aucune connexion au serveur.",
                "Sauvegarde locale");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message, "Erreur de sauvegarde locale");
        }
    }

    private async void ChangeDatasetButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_hasUnsavedSheetChanges)
        {
            var save = await ShowYesNoCancelAsync(
                "Des modifications non enregistrées sont en cours.\n\nEnregistrer avant de changer de dataset ?",
                "Modifications non enregistrées");

            if (save is null)
            {
                return;
            }

            if (save == true)
            {
                if (!MatchesTab.TrySaveCurrentSheetForExit(out var errorMsg))
                {
                    if (!string.IsNullOrWhiteSpace(errorMsg))
                    {
                        await ShowErrorAsync(errorMsg, "Erreur de sauvegarde");
                    }
                    return;
                }

                MarkSheetAsSaved();
            }
        }

        if (_datasetRegistry is null)
        {
            return;
        }

        var currentPath = _datasetRegistry.GetLastOpened()?.Path;

        var dialog = new DatasetManagerDialog(_datasetRegistry, currentPath);
        var chosen = await dialog.ShowDialog<DatasetEntry?>(this);

        if (chosen is null)
        {
            return;
        }

        _datasetRegistry.SetLastOpened(chosen.Id);
        _datasetRegistry.Save();
        await LoadDatasetAsync(chosen);
    }

    private async void MainTabControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, MainTabControl))
        {
            return;
        }

        var selectedIsScoreDataTab = ReferenceEquals(MainTabControl.SelectedItem, ScoresTabItem)
            || ReferenceEquals(MainTabControl.SelectedItem, LeaderboardTabItem);

        if (_scoreTabLoaded || !selectedIsScoreDataTab)
        {
            return;
        }

        try
        {
            ScoresTab.ReloadScores(this);
            PlayersTab.ApplySummaries(ScoreSummaries);
            _playerStatsLoaded = true;
            SetStatus("Scores et classement chargés.");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message, "Erreur de chargement des scores");
        }
    }

    private async Task WarmUpPlayerStatsAsync()
    {
        var workbookService = _workbookService;
        if (workbookService is null)
        {
            return;
        }

        try
        {
            var summaries = await Task.Run(workbookService.LoadPlayerScoreSummaries);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_workbookService is null)
                {
                    return;
                }

                PlayersTab.ApplySummaries(summaries);
                _playerStatsLoaded = true;

                if (!_scoreTabLoaded)
                {
                    SetStatus("Fichier .padel chargé. Statistiques joueurs préchargées.");
                }
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_scoreTabLoaded)
                {
                    SetStatus("Fichier .padel chargé. Les statistiques seront chargées à l'ouverture de l'onglet Scores.");
                }
            });
        }
    }

    private async void ExportDataButton_OnClick(object sender, RoutedEventArgs e)
    {
        var workbookService = _workbookService;
        if (workbookService is null)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exporter les données en XLSX",
            DefaultExtension = "xlsx",
            SuggestedFileName = $"PadelExport_{DateTime.Today:yyyyMMdd}.xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Fichier Excel") { Patterns = new[] { "*.xlsx" } }
            ]
        });

        if (file is null)
        {
            return;
        }

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            workbookService.ExportToXlsx(path);
            SetStatus($"Export XLSX terminé : {path}");
            await ShowInfoAsync("Les données ont été exportées en XLSX.", "Export terminé");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message, "Erreur d'export XLSX");
        }
    }

    internal void MarkSheetAsChanged()
    {
        if (_suppressSheetChangeTracking)
        {
            return;
        }

        _hasUnsavedSheetChanges = true;
    }

    internal void MarkSheetAsSaved()
    {
        _hasUnsavedSheetChanges = false;
    }

    internal void RunWithoutSheetChangeTracking(Action action)
    {
        _suppressSheetChangeTracking = true;
        try
        {
            action();
        }
        finally
        {
            _suppressSheetChangeTracking = false;
        }
    }

    private void GeneratedMatches_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<UiMatch>())
            {
                item.PropertyChanged -= GeneratedMatch_OnPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<UiMatch>())
            {
                item.PropertyChanged += GeneratedMatch_OnPropertyChanged;
            }
        }

        if (e.Action != NotifyCollectionChangedAction.Reset)
        {
            MarkSheetAsChanged();
        }
    }

    private void GeneratedMatch_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UiMatch.ScoreA)
            or nameof(UiMatch.ScoreB)
            or nameof(UiMatch.TeamAPlayer1)
            or nameof(UiMatch.TeamAPlayer2)
            or nameof(UiMatch.TeamBPlayer1)
            or nameof(UiMatch.TeamBPlayer2))
        {
            MarkSheetAsChanged();
        }
    }

    private async void MainWindow_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingConfirmed || !_hasUnsavedSheetChanges)
        {
            return;
        }

        e.Cancel = true;

        var result = await ShowYesNoCancelAsync(
            "Des modifications du planning courant n'ont pas été enregistrées.\n\nVoulez-vous enregistrer avant de quitter ?",
            "Modifications non enregistrées");

        if (result is null)
        {
            return;
        }

        if (result == false)
        {
            _closingConfirmed = true;
            Close();
            return;
        }

        if (MatchesTab.TrySaveCurrentSheetForExit(out var errorMessage))
        {
            MarkSheetAsSaved();
            _closingConfirmed = true;
            Close();
            return;
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            await ShowErrorAsync(errorMessage, "Erreur de sauvegarde");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _syncBinding?.Dispose();
        _syncBinding = null;
        base.OnClosed(e);
    }

    internal void SetStatus(string text)
    {
        StatusTextBlock.Text = text;
        OnPropertyChanged(nameof(StatusTextBlock));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    internal async Task ShowInfoAsync(string message, string title)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, MsBoxIcon.Info);
        await box.ShowWindowDialogAsync(this);
    }

    internal async Task ShowWarningAsync(string message, string title)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, MsBoxIcon.Warning);
        await box.ShowWindowDialogAsync(this);
    }

    internal async Task ShowErrorAsync(string message, string title)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, MsBoxIcon.Error);
        await box.ShowWindowDialogAsync(this);
    }

    internal async Task<bool> ShowYesNoAsync(string message, string title)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.YesNo, MsBoxIcon.Question);
        var result = await box.ShowWindowDialogAsync(this);
        return result == ButtonResult.Yes;
    }

    internal async Task<bool?> ShowYesNoCancelAsync(string message, string title)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.YesNoCancel, MsBoxIcon.Warning);
        var result = await box.ShowWindowDialogAsync(this);
        return result switch
        {
            ButtonResult.Yes => true,
            ButtonResult.No => false,
            _ => null
        };
    }

    internal async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await UpdateService.CheckForUpdateAsync();
            if (update is null) return;

            var box = MessageBoxManager.GetMessageBoxStandard(
                "Mise à jour disponible",
                $"Une nouvelle version (v{update.Version}) est disponible.\nTélécharger et installer maintenant ?",
                ButtonEnum.YesNo);

            var result = await box.ShowWindowDialogAsync(this);
            if (result != ButtonResult.Yes) return;

            await UpdateService.DownloadAndLaunchAsync(update.DownloadUrl);
        }
        catch
        {
            // Ignore update errors — the app works fine without them
        }
    }
}
