using Avalonia.Controls;
using Avalonia.Interactivity;
using Padel.Manager.Models;

namespace Padel.Manager.Views;

public partial class PlayersTabView : UserControl
{
    public PlayersTabView()
    {
        InitializeComponent();
    }

    private MainWindow? GetMainWindow() => TopLevel.GetTopLevel(this) as MainWindow;

    public void ReloadPlayers(bool includeStats)
    {
        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null || workbookService is null)
        {
            return;
        }

        List<PlayerProfile> players;
        Dictionary<string, PlayerScoreSummary> summariesByName;

        if (includeStats)
        {
            var data = workbookService.LoadPlayersAndSummaries();
            players = data.Players;
            summariesByName = data.Summaries.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            players = workbookService.LoadPlayers();
            summariesByName = new Dictionary<string, PlayerScoreSummary>(StringComparer.OrdinalIgnoreCase);
        }

        players = players.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

        mainWindow.Players.Clear();
        foreach (var entry in players)
        {
            summariesByName.TryGetValue(entry.Name, out var summary);
            mainWindow.Players.Add(new PlayerProfile
            {
                Name = entry.Name,
                Level = entry.Level,
                MatchesPlayed = summary?.MatchesPlayed ?? 0,
                MatchesWon = summary?.MatchesWon ?? 0,
                TotalPoints = summary?.TotalPoints ?? 0,
                AveragePoints = summary?.AveragePoints ?? 0,
                RecentMatchCount = summary?.RecentMatchCount ?? 0,
                RecentAverage = summary?.RecentAverage ?? 0,
                RecentDelta = summary?.RecentDelta ?? 0
            });
        }

        mainWindow.PlayerPool.Clear();
        foreach (var player in mainWindow.Players)
        {
            mainWindow.PlayerPool.Add(new SelectablePlayer { Player = player });
        }

        mainWindow.TournamentPlayerPool.Clear();
        foreach (var player in mainWindow.Players)
        {
            mainWindow.TournamentPlayerPool.Add(new SelectablePlayer { Player = player });
        }
    }

    public void ApplySummaries(IEnumerable<PlayerScoreSummary> summaries)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        var summariesByName = summaries.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var player in mainWindow.Players)
        {
            if (!summariesByName.TryGetValue(player.Name, out var summary))
            {
                player.MatchesPlayed = 0;
                player.MatchesWon = 0;
                player.TotalPoints = 0;
                player.AveragePoints = 0;
                player.RecentMatchCount = 0;
                player.RecentAverage = 0;
                player.RecentDelta = 0;
                continue;
            }

            player.MatchesPlayed = summary.MatchesPlayed;
            player.MatchesWon = summary.MatchesWon;
            player.TotalPoints = summary.TotalPoints;
            player.AveragePoints = summary.AveragePoints;
            player.RecentMatchCount = summary.RecentMatchCount;
            player.RecentAverage = summary.RecentAverage;
            player.RecentDelta = summary.RecentDelta;
        }
    }

    private async void ReloadPlayersButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        try
        {
            ReloadPlayers(includeStats: mainWindow.PlayerStatsLoaded);
            if (mainWindow.ScoreTabLoaded)
            {
                mainWindow.ScoresTabViewControl.ReloadScores();
                ApplySummaries(mainWindow.ScoreSummaries);
                mainWindow.PlayerStatsLoaded = true;
            }

            mainWindow.SetStatus("Joueurs rechargés.");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur de rechargement");
        }
    }

    private async void AddPlayerButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null || workbookService is null)
        {
            return;
        }

        var name = NewPlayerNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            await mainWindow.ShowWarningAsync("Veuillez saisir un nom de joueur.", "Validation");
            return;
        }

        var level = GetSelectedComboNumber(NewPlayerLevelBox, defaultValue: 3);
        if (level < 1 || level > 5)
        {
            await mainWindow.ShowWarningAsync("Le niveau doit être compris entre 1 et 5.", "Validation");
            return;
        }

        try
        {
            workbookService.AddPlayer(new PlayerProfile { Name = name, Level = level });

            NewPlayerNameBox.Text = string.Empty;
            ReloadPlayers(includeStats: mainWindow.PlayerStatsLoaded);
            if (mainWindow.ScoreTabLoaded)
            {
                mainWindow.ScoresTabViewControl.ReloadScores();
                ApplySummaries(mainWindow.ScoreSummaries);
                mainWindow.PlayerStatsLoaded = true;
            }

            mainWindow.SetStatus($"Le joueur '{name}' a été ajouté.");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur lors de l'ajout");
        }
    }

    private async void SaveLevelsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null || workbookService is null)
        {
            return;
        }

        try
        {
            foreach (var player in mainWindow.Players)
            {
                if (player.Level < 1 || player.Level > 5)
                {
                    await mainWindow.ShowWarningAsync(
                        $"Le niveau du joueur '{player.Name}' doit être compris entre 1 et 5.",
                        "Validation");
                    return;
                }
            }

            workbookService.UpdatePlayerLevels(mainWindow.Players.ToList());

            ReloadPlayers(includeStats: mainWindow.PlayerStatsLoaded);
            if (mainWindow.ScoreTabLoaded)
            {
                mainWindow.ScoresTabViewControl.ReloadScores();
                ApplySummaries(mainWindow.ScoreSummaries);
                mainWindow.PlayerStatsLoaded = true;
            }

            mainWindow.SetStatus("Les niveaux des joueurs ont été enregistrés.");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur lors de la sauvegarde des niveaux");
        }
    }

    private static int GetSelectedComboNumber(ComboBox comboBox, int defaultValue)
    {
        if (comboBox.SelectedItem is not ComboBoxItem item)
        {
            return defaultValue;
        }

        return int.TryParse(item.Content?.ToString(), out var number) ? number : defaultValue;
    }
}
