using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Padel.Manager.Models;
using Padel.Manager.Services;

namespace Padel.Manager.Views;

public partial class LeaderboardTabView : UserControl, INotifyPropertyChanged
{
    private const int MinPlayersPerSheet = 5;
    private const int MaxPlayersPerSheet = 200;
    private const int DefaultPlayersPerSheet = 20;

    private readonly LeaderboardSheetService _sheetService = new();
    private MainWindow? _boundMainWindow;
    private PlayerScoreSummary? _firstPlace;
    private PlayerScoreSummary? _secondPlace;
    private PlayerScoreSummary? _thirdPlace;
    private int _playersPerSheet = DefaultPlayersPerSheet;

    public LeaderboardTabView()
    {
        InitializeComponent();
        Loaded += LeaderboardTabView_OnLoaded;
        Unloaded += LeaderboardTabView_OnUnloaded;
    }

    public ObservableCollection<LeaderboardRow> RankedSummaries { get; } = new();

    public PlayerScoreSummary? FirstPlace
    {
        get => _firstPlace;
        private set { _firstPlace = value; OnPropertyChanged(); OnPropertyChanged(nameof(FirstPlaceScore)); }
    }

    public PlayerScoreSummary? SecondPlace
    {
        get => _secondPlace;
        private set { _secondPlace = value; OnPropertyChanged(); OnPropertyChanged(nameof(SecondPlaceScore)); }
    }

    public PlayerScoreSummary? ThirdPlace
    {
        get => _thirdPlace;
        private set { _thirdPlace = value; OnPropertyChanged(); OnPropertyChanged(nameof(ThirdPlaceScore)); }
    }

    public int? FirstPlaceScore => AmericanoMode ? FirstPlace?.TotalScoredGames : FirstPlace?.TotalPoints;
    public int? SecondPlaceScore => AmericanoMode ? SecondPlace?.TotalScoredGames : SecondPlace?.TotalPoints;
    public int? ThirdPlaceScore => AmericanoMode ? ThirdPlace?.TotalScoredGames : ThirdPlace?.TotalPoints;

    private bool AmericanoMode => GetMainWindow()?.AmericanoMode ?? false;

    public new event PropertyChangedEventHandler? PropertyChanged;

    private MainWindow? GetMainWindow() => TopLevel.GetTopLevel(this) as MainWindow;

    private void LeaderboardTabView_OnLoaded(object? sender, RoutedEventArgs e)
    {
        BindToMainWindow();
        LoadPlayersPerSheetSetting();
        UpdateTitle();
        ReloadLeaderboard();
    }

    private void LeaderboardTabView_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_boundMainWindow is not null)
        {
            _boundMainWindow.ScoreSummaries.CollectionChanged -= ScoreSummaries_OnCollectionChanged;
            _boundMainWindow = null;
        }
    }

    private void BindToMainWindow()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        if (ReferenceEquals(_boundMainWindow, mainWindow))
        {
            return;
        }

        if (_boundMainWindow is not null)
        {
            _boundMainWindow.ScoreSummaries.CollectionChanged -= ScoreSummaries_OnCollectionChanged;
        }

        _boundMainWindow = mainWindow;
        _boundMainWindow.ScoreSummaries.CollectionChanged += ScoreSummaries_OnCollectionChanged;
    }

    private void ScoreSummaries_OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        ReloadLeaderboard();
    }

    internal void RefreshLeaderboard() => ReloadLeaderboard();

    private void ReloadLeaderboard()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
            return;

        var americano = mainWindow.AmericanoMode;

        var ordered = americano
            ? mainWindow.ScoreSummaries
                .OrderByDescending(x => x.TotalScoredGames)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : mainWindow.ScoreSummaries
                .OrderByDescending(x => x.TotalPoints)
                .ThenByDescending(x => x.MatchesWon)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        RankedSummaries.Clear();
        for (var i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            RankedSummaries.Add(new LeaderboardRow
            {
                Rank = i + 1,
                Name = item.Name,
                Level = item.Level,
                MatchesPlayed = item.MatchesPlayed,
                MatchesWon = item.MatchesWon,
                TotalPoints = item.TotalPoints,
                TotalScoredGames = item.TotalScoredGames,
                AveragePoints = item.AveragePoints,
                RecentMatchCount = item.RecentMatchCount,
                RecentAverage = item.RecentAverage,
                RecentDelta = item.RecentDelta
            });
        }

        FirstPlace = ordered.ElementAtOrDefault(0);
        SecondPlace = ordered.ElementAtOrDefault(1);
        ThirdPlace = ordered.ElementAtOrDefault(2);

        AmericanoBadge.IsVisible = americano;
    }

    private void UpdateTitle()
    {
        LeaderboardTitleTextBlock.Text = $"CLASSEMENT GLOBAL - {DateTime.Today:dd/MM/yyyy}";
        LeaderboardSubtitleTextBlock.Text = $"Top {Math.Max(3, RankedSummaries.Count)} joueurs";
    }

    private void LoadPlayersPerSheetSetting()
    {
        var mainWindow = GetMainWindow();
        var configured = mainWindow?.WorkbookService?.LoadLeaderboardPlayersPerSheet() ?? DefaultPlayersPerSheet;
        _playersPerSheet = Math.Clamp(configured, MinPlayersPerSheet, MaxPlayersPerSheet);
        PlayersPerSheetTextBox.Text = _playersPerSheet.ToString();
    }

    private void PlayersPerSheetTextBox_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        NormalizePlayersPerSheetInput();
    }

    private void PlayersPerSheetTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        NormalizePlayersPerSheetInput();
        e.Handled = true;
    }

    private void NormalizePlayersPerSheetInput()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            _playersPerSheet = DefaultPlayersPerSheet;
            PlayersPerSheetTextBox.Text = _playersPerSheet.ToString();
            return;
        }

        if (!int.TryParse(PlayersPerSheetTextBox.Text, out var parsed))
        {
            parsed = _playersPerSheet;
        }

        _playersPerSheet = Math.Clamp(parsed, MinPlayersPerSheet, MaxPlayersPerSheet);
        PlayersPerSheetTextBox.Text = _playersPerSheet.ToString();

        mainWindow.WorkbookService?.SaveLeaderboardPlayersPerSheet(_playersPerSheet);
        mainWindow.SetStatus($"Classement: {_playersPerSheet} joueurs par feuille.");
    }

    private async void ExportLeaderboardImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        if (mainWindow.ScoreSummaries.Count == 0)
        {
            await mainWindow.ShowWarningAsync("Aucun classement à exporter.", "Validation");
            return;
        }

        try
        {
            ReloadLeaderboard();
            UpdateTitle();
            NormalizePlayersPerSheetInput();

            var rows = BuildLeaderboardRows();
            var playersPerSheet = Math.Clamp(_playersPerSheet, MinPlayersPerSheet, MaxPlayersPerSheet);
            var pageBytes = _sheetService.RenderPages(rows, playersPerSheet, DateTime.Today);

            if (pageBytes.Count == 0)
            {
                await mainWindow.ShowWarningAsync("Aucune page à exporter.", "Validation");
                return;
            }

            var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Exporter le classement",
                DefaultExtension = "png",
                SuggestedFileName = $"Classement_{DateTime.Today:yyyyMMdd}.png",
                FileTypeChoices = new[] { new FilePickerFileType("Image PNG") { Patterns = new[] { "*.png" } } }
            });

            if (file is null)
            {
                return;
            }

            var outputPath = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            var outputDirectory = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory;
            var outputFileBase = Path.GetFileNameWithoutExtension(outputPath);

            if (pageBytes.Count == 1)
            {
                await File.WriteAllBytesAsync(outputPath, pageBytes[0]);
            }
            else
            {
                for (var i = 0; i < pageBytes.Count; i++)
                {
                    var pagePath = Path.Combine(outputDirectory, $"{outputFileBase}_{i + 1:00}.png");
                    await File.WriteAllBytesAsync(pagePath, pageBytes[i]);
                }
            }

            var exportTarget = pageBytes.Count == 1
                ? outputPath
                : $"{outputDirectory} ({pageBytes.Count} fichiers)";
            mainWindow.SetStatus($"Classement exporté : {exportTarget}");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur d'export image");
        }
    }

    private async void PrintLeaderboardButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        if (mainWindow.ScoreSummaries.Count == 0)
        {
            await mainWindow.ShowWarningAsync("Aucun classement à imprimer.", "Validation");
            return;
        }

        try
        {
            ReloadLeaderboard();
            UpdateTitle();
            NormalizePlayersPerSheetInput();

            var rows = BuildLeaderboardRows();
            var playersPerSheet = Math.Clamp(_playersPerSheet, MinPlayersPerSheet, MaxPlayersPerSheet);
            var pageBytes = _sheetService.RenderPages(rows, playersPerSheet, DateTime.Today);

            if (pageBytes.Count == 0)
            {
                await mainWindow.ShowWarningAsync("Aucune page à imprimer.", "Validation");
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"PadelLeaderboard_{DateTime.Now:yyyyMMddHHmmss}");
            Directory.CreateDirectory(tempDir);

            for (var i = 0; i < pageBytes.Count; i++)
            {
                var pagePath = Path.Combine(tempDir, $"page_{i + 1:00}.png");
                await File.WriteAllBytesAsync(pagePath, pageBytes[i]);
                OpenWithSystemDefault(pagePath);
            }

            mainWindow.SetStatus($"Classement ouvert pour impression ({pageBytes.Count} page(s)).");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur d'impression");
        }
    }

    private static void OpenWithSystemDefault(string filePath)
    {
        Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
    }

    private List<LeaderboardSheetRow> BuildLeaderboardRows()
    {
        var americano = GetMainWindow()?.AmericanoMode ?? false;
        return RankedSummaries
            .Select(row => new LeaderboardSheetRow
            {
                Rank = row.Rank,
                Name = row.Name,
                Level = row.Level,
                MatchesPlayed = row.MatchesPlayed,
                MatchesWon = row.MatchesWon,
                TotalPoints = americano ? row.TotalScoredGames : row.TotalPoints,
                AveragePoints = americano && row.MatchesPlayed > 0
                    ? (double)row.TotalScoredGames / row.MatchesPlayed
                    : row.AveragePoints,
                RecentFormDisplay = row.RecentFormDisplay
            })
            .ToList();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class LeaderboardRow
    {
        public required int Rank { get; init; }
        public required string Name { get; init; }
        public required int Level { get; init; }
        public required int MatchesPlayed { get; init; }
        public required int MatchesWon { get; init; }
        public required int TotalPoints { get; init; }
        public required int TotalScoredGames { get; init; }
        public required double AveragePoints { get; init; }
        public required int RecentMatchCount { get; init; }
        public required double RecentAverage { get; init; }
        public required double RecentDelta { get; init; }

        public string RecentFormDisplay
        {
            get
            {
                if (RecentMatchCount == 0)
                {
                    return "—";
                }

                var arrow = RecentDelta > 0.05 ? "▲" : RecentDelta < -0.05 ? "▼" : "▶";
                var sign = RecentDelta >= 0 ? "+" : "";
                return $"{RecentAverage:F2} {arrow} ({sign}{RecentDelta:F2})";
            }
        }
    }
}
