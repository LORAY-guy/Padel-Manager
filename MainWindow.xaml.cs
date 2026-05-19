using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MessageBox.Avalonia.Enums;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using Padel.Manager.Models;
using Padel.Manager.Services;
using Padel.Manager.Views;

namespace Padel.Manager;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private WorkbookService? _workbookService;
    private DatasetRegistry? _datasetRegistry;
    private DatasetRegistry? _preloadedRegistry;
    private DatasetEntry? _preloadedEntry;
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

    public MainWindow(DatasetRegistry registry, DatasetEntry entry) : this()
    {
        _preloadedRegistry = registry;
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

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
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

    private Task LoadDatasetAsync(DatasetEntry entry)
    {
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

        _workbookService = new WorkbookService(entry.Path, null);

        DatasetNameTextBlock.Text = entry.Name;
        WorkbookPathTextBlock.Text = entry.Path;

        MatchesTab.InitializeDefaults();
        PlayersTab.ReloadPlayers(includeStats: false);
        MarkSheetAsSaved();
        SetStatus($"Dataset « {entry.Name} » chargé. Chargement des statistiques en arrière-plan...");

        _ = WarmUpPlayerStatsAsync();
        return Task.CompletedTask;
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
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Fichier Excel") { Patterns = new[] { "*.xlsx" } }
            }
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
}
