using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Padel.Manager.Models;
using Padel.Manager.Services;

namespace Padel.Manager.Views;

public partial class MatchesTabView : UserControl
{
    private readonly Random _random = new();
    private readonly List<IPrintStyle> _availablePrintStyles = new();
    private IPrintStyle _currentPrintStyle = new ColoredPrintStyle();

    public MatchesTabView()
    {
        InitializeComponent();
        GeneratedMatchesDataGrid.DoubleTapped += GeneratedMatchesDataGrid_OnDoubleTapped;
    }

    private MainWindow? GetMainWindow() => TopLevel.GetTopLevel(this) as MainWindow;

    public void InitializeDefaults()
    {
        MatchDatePicker.SelectedDate = DateTime.Today;
        InitializePrintStyles();
        UpdatePrintableSheetHeader();
    }

    private void InitializePrintStyles()
    {
        _availablePrintStyles.Clear();
        _availablePrintStyles.Add(new ColoredPrintStyle());
        _availablePrintStyles.Add(new BlackAndWhitePrintStyle());

        PrintStyleComboBox.ItemsSource = _availablePrintStyles;
        PrintStyleComboBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(IPrintStyle.DisplayName));
        PrintStyleComboBox.SelectedIndex = 0;
        _currentPrintStyle = _availablePrintStyles[0];
    }

    private void MatchDatePicker_OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdatePrintableSheetHeader();
    }

    private void PrintStyleComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PrintStyleComboBox.SelectedItem is IPrintStyle style)
        {
            _currentPrintStyle = style;
            ApplyPrintStyle(_currentPrintStyle);
        }
    }

    private async void DeleteSelectedMatchButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null)
        {
            return;
        }

        if (GeneratedMatchesDataGrid.SelectedItem is not UiMatch selectedMatch)
        {
            await mainWindow.ShowWarningAsync("Sélectionnez un match à supprimer.", "Validation");
            return;
        }

        var confirm = await mainWindow.ShowYesNoAsync(
            $"Supprimer la rotation {selectedMatch.RoundNumber}, match {selectedMatch.MatchNumber} ?",
            "Confirmation");

        if (!confirm)
        {
            return;
        }

        var removedScores = 0;
        var removedPlanned = 0;
        var selectedDate = MatchDatePicker.SelectedDate;
        if (workbookService is not null && selectedDate is not null)
        {
            var deleteResult = workbookService.DeleteMatch(selectedDate.Value, selectedMatch.RoundNumber, selectedMatch.TerrainNumber);
            removedPlanned = deleteResult.RemovedPlanned;
            removedScores = deleteResult.RemovedScores;
        }

        mainWindow.GeneratedMatches.Remove(selectedMatch);
        RefreshPrintableGroups(mainWindow);
        mainWindow.MarkSheetAsChanged();

        if (removedScores > 0)
        {
            mainWindow.PlayersTabViewControl.ReloadPlayers(includeStats: mainWindow.PlayerStatsLoaded);
            if (mainWindow.ScoreTabLoaded)
            {
                mainWindow.ScoresTabViewControl.ReloadScores();
                mainWindow.PlayersTabViewControl.ApplySummaries(mainWindow.ScoreSummaries);
                mainWindow.PlayerStatsLoaded = true;
            }

            mainWindow.SetStatus($"Match supprimé (planning: {removedPlanned}, scores: {removedScores}). Les points ont été recalculés.");
            return;
        }

        mainWindow.SetStatus(removedPlanned > 0
            ? $"Match supprimé du planning (lignes supprimées: {removedPlanned})."
            : "Match supprimé du planning courant.");
    }

    private void SelectAllPlayersButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        foreach (var p in mainWindow.PlayerPool)
        {
            p.IsSelected = true;
        }
    }

    private void ClearPlayersButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        foreach (var p in mainWindow.PlayerPool)
        {
            p.IsSelected = false;
        }
    }

    private async void GenerateMatchesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null || workbookService is null)
        {
            return;
        }

        var selectedPlayers = mainWindow.PlayerPool
            .Where(p => p.IsSelected)
            .Select(p => p.Player)
            .ToList();

        if (selectedPlayers.Count < 4)
        {
            await mainWindow.ShowWarningAsync("Sélectionnez au moins 4 joueurs.", "Validation");
            return;
        }

        var usableCount = selectedPlayers.Count - (selectedPlayers.Count % 4);
        if (usableCount < selectedPlayers.Count)
        {
            selectedPlayers = selectedPlayers.Take(usableCount).ToList();
            mainWindow.SetStatus($"{usableCount} joueurs utilisés (multiple de 4 requis).");
        }

        var rounds = GetSelectedComboNumber(RoundCountComboBox, defaultValue: 3);
        rounds = Math.Max(1, rounds);

        var averagePoints = workbookService.LoadAveragePointsByPlayer();
        var matches = MatchGenerationService.BuildBalancedMatches(selectedPlayers, averagePoints, rounds, _random);

        mainWindow.GeneratedMatches.Clear();
        foreach (var match in matches)
        {
            match.TerrainNumber = 0;
            mainWindow.GeneratedMatches.Add(match);
        }

        RefreshPrintableGroups(mainWindow);
        mainWindow.MarkSheetAsChanged();
        mainWindow.SetStatus($"{mainWindow.GeneratedMatches.Count} matchs générés pour {rounds} rotation(s).");
    }

    private async void SaveResultsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null || workbookService is null)
        {
            return;
        }

        if (mainWindow.GeneratedMatches.Count == 0)
        {
            await mainWindow.ShowWarningAsync("Générez d'abord les matchs.", "Validation");
            return;
        }

        var selectedDate = MatchDatePicker.SelectedDate;
        if (selectedDate is null)
        {
            await mainWindow.ShowWarningAsync("Veuillez sélectionner une date.", "Validation");
            return;
        }

        var parsedMatches = new List<(UiMatch Match, int ScoreA, int ScoreB)>();
        foreach (var match in mainWindow.GeneratedMatches)
        {
            if (!int.TryParse(match.ScoreA, out var scoreA) || !int.TryParse(match.ScoreB, out var scoreB))
            {
                await mainWindow.ShowWarningAsync(
                    $"Score invalide pour la rotation {match.RoundNumber}, match {match.MatchNumber}.",
                    "Validation");
                return;
            }

            if (scoreA < 0 || scoreB < 0)
            {
                await mainWindow.ShowWarningAsync("Les scores ne peuvent pas être négatifs.", "Validation");
                return;
            }

            parsedMatches.Add((match, scoreA, scoreB));
        }

        try
        {
            var startNumber = workbookService.GetNextMatchNumberForDate(selectedDate.Value);
            var roundNumberMap = new Dictionary<int, int>();
            var nextMatchNumber = startNumber;

            foreach (var round in parsedMatches.Select(m => m.Match.RoundNumber).Distinct().OrderBy(r => r))
            {
                roundNumberMap[round] = nextMatchNumber;
                nextMatchNumber++;
            }

            var rows = parsedMatches
                .OrderBy(m => m.Match.RoundNumber)
                .ThenBy(m => m.Match.MatchNumber)
                .Select(m => new MatchWriteModel
                {
                    Date = selectedDate.Value,
                    RoundNumber = m.Match.RoundNumber,
                    MatchNumber = roundNumberMap[m.Match.RoundNumber],
                    TerrainNumber = m.Match.TerrainNumber,
                    TeamAPlayer1 = m.Match.TeamAPlayer1,
                    TeamAPlayer2 = m.Match.TeamAPlayer2,
                    TeamBPlayer1 = m.Match.TeamBPlayer1,
                    TeamBPlayer2 = m.Match.TeamBPlayer2,
                    ScoreA = m.ScoreA,
                    ScoreB = m.ScoreB
                })
                .ToList();

            workbookService.AppendMatches(rows);
            workbookService.UpdatePlannedMatchScores(selectedDate.Value, rows);
            mainWindow.MarkSheetAsSaved();
            mainWindow.PlayersTabViewControl.ReloadPlayers(includeStats: mainWindow.PlayerStatsLoaded);
            if (mainWindow.ScoreTabLoaded)
            {
                mainWindow.ScoresTabViewControl.ReloadScores();
                mainWindow.PlayersTabViewControl.ApplySummaries(mainWindow.ScoreSummaries);
                mainWindow.PlayerStatsLoaded = true;
            }

            mainWindow.SetStatus($"{rows.Count} lignes de match enregistrées.");
            await mainWindow.ShowInfoAsync("Les scores ont été enregistrés dans le classeur.", "Succès");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur d'enregistrement");
        }
    }

    private async void SavePlanningButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null || workbookService is null)
        {
            return;
        }

        if (mainWindow.GeneratedMatches.Count == 0)
        {
            await mainWindow.ShowWarningAsync("Aucun match généré à enregistrer.", "Validation");
            return;
        }

        var selectedDate = MatchDatePicker.SelectedDate;
        if (selectedDate is null)
        {
            await mainWindow.ShowWarningAsync("Veuillez sélectionner une date.", "Validation");
            return;
        }

        try
        {
            workbookService.SavePlannedMatches(selectedDate.Value, mainWindow.GeneratedMatches.ToList());
            mainWindow.MarkSheetAsSaved();
            mainWindow.SetStatus($"Planning du {selectedDate.Value:dd/MM/yyyy} enregistré ({mainWindow.GeneratedMatches.Count} matchs).");
            await mainWindow.ShowInfoAsync("Planning sauvegardé. Vous pourrez le recharger plus tard pour saisir les scores.", "Succès");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur de sauvegarde du planning");
        }
    }

    private async void LoadPlanningButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null || workbookService is null)
        {
            return;
        }

        var selectedDate = MatchDatePicker.SelectedDate;
        if (selectedDate is null)
        {
            await mainWindow.ShowWarningAsync("Veuillez sélectionner une date.", "Validation");
            return;
        }

        try
        {
            var loadedFromScores = false;
            var plannedRows = workbookService.LoadPlannedMatches(selectedDate.Value);
            if (plannedRows.Count == 0)
            {
                plannedRows = workbookService.LoadMatchesFromScores(selectedDate.Value);
                if (plannedRows.Count == 0)
                {
                    await mainWindow.ShowInfoAsync("Aucun planning ni historique de matchs trouvé pour cette date.", "Information");
                    return;
                }

                loadedFromScores = true;
                mainWindow.SetStatus($"Aucun planning trouvé. Chargement depuis l'historique des scores ({plannedRows.Count} matchs).");
            }

            mainWindow.RunWithoutSheetChangeTracking(() =>
            {
                mainWindow.GeneratedMatches.Clear();
                foreach (var row in plannedRows)
                {
                    mainWindow.GeneratedMatches.Add(new UiMatch
                    {
                        RoundNumber = row.RoundNumber,
                        MatchNumber = row.TerrainNumber,
                        TerrainNumber = row.TerrainNumber,
                        TeamAPlayer1 = row.TeamAPlayer1,
                        TeamAPlayer2 = row.TeamAPlayer2,
                        TeamBPlayer1 = row.TeamBPlayer1,
                        TeamBPlayer2 = row.TeamBPlayer2,
                        ScoreA = row.ScoreA?.ToString() ?? string.Empty,
                        ScoreB = row.ScoreB?.ToString() ?? string.Empty
                    });
                }
            });

            RefreshPrintableGroups(mainWindow);
            mainWindow.MarkSheetAsSaved();
            if (loadedFromScores)
            {
                return;
            }

            mainWindow.SetStatus($"Planning du {selectedDate.Value:dd/MM/yyyy} chargé ({mainWindow.GeneratedMatches.Count} matchs).");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur de chargement du planning");
        }
    }

    private async void ExportSheetImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        if (mainWindow.GeneratedMatches.Count == 0)
        {
            await mainWindow.ShowWarningAsync("Aucun match à exporter.", "Validation");
            return;
        }

        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exporter la feuille en image",
            DefaultExtension = "png",
            SuggestedFileName = $"Feuille_matchs_{(MatchDatePicker.SelectedDate ?? DateTime.Today):yyyyMMdd}.png",
            FileTypeChoices = new[] { new FilePickerFileType("Image PNG") { Patterns = new[] { "*.png" } } }
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
            UpdatePrintableSheetHeader();
            ApplyPrintStyle(_currentPrintStyle);

            const double dpi = 300d;
            const double a4WidthInches = 8.27;
            const double a4HeightInches = 11.69;

            var pageWidthPx = (int)Math.Round(a4WidthInches * dpi);
            var pageHeightPx = (int)Math.Round(a4HeightInches * dpi);
            var pageWidthDip = pageWidthPx * 96d / dpi;

            const double tableMinContentWidth = 90d + 250d + 250d + 80d + 80d;
            var panelHPadding = PrintableSheetPanel.Padding.Left + PrintableSheetPanel.Padding.Right
                + PrintableSheetPanel.BorderThickness.Left + PrintableSheetPanel.BorderThickness.Right;
            var layoutWidth = Math.Max(pageWidthDip, tableMinContentWidth + panelHPadding);

            var previousWidth = PrintableSheetPanel.Width;
            var previousMinWidth = PrintableSheetPanel.MinWidth;
            var previousMaxWidth = PrintableSheetPanel.MaxWidth;

            try
            {
                PrintableSheetPanel.Width = layoutWidth;
                PrintableSheetPanel.MinWidth = layoutWidth;
                PrintableSheetPanel.MaxWidth = layoutWidth;
                PrintableSheetPanel.Measure(new Avalonia.Size(layoutWidth, double.PositiveInfinity));
                PrintableSheetPanel.Arrange(new Avalonia.Rect(0, 0, layoutWidth, PrintableSheetPanel.DesiredSize.Height));

                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

                var bitmap = new RenderTargetBitmap(
                    new Avalonia.PixelSize(pageWidthPx, pageHeightPx),
                    new Avalonia.Vector(dpi, dpi));
                bitmap.Render(PrintableSheetPanel);
                bitmap.Save(path);
            }
            finally
            {
                PrintableSheetPanel.Width = previousWidth;
                PrintableSheetPanel.MinWidth = previousMinWidth;
                PrintableSheetPanel.MaxWidth = previousMaxWidth;
                PrintableSheetPanel.InvalidateMeasure();
            }

            mainWindow.SetStatus($"Feuille exportée en image : {path}");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur d'export image");
        }
    }

    private async void PrintSheetButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        if (mainWindow.GeneratedMatches.Count == 0)
        {
            await mainWindow.ShowWarningAsync("Aucun match à imprimer.", "Validation");
            return;
        }

        try
        {
            UpdatePrintableSheetHeader();
            ApplyPrintStyle(_currentPrintStyle);

            var tempPath = Path.Combine(Path.GetTempPath(), $"PadelSheet_{DateTime.Now:yyyyMMddHHmmss}.png");

            const double dpi = 150d;
            const double a4WidthInches = 8.27;
            const double a4HeightInches = 11.69;
            var pageWidthPx = (int)Math.Round(a4WidthInches * dpi);
            var pageHeightPx = (int)Math.Round(a4HeightInches * dpi);

            var bitmap = new RenderTargetBitmap(
                new Avalonia.PixelSize(pageWidthPx, pageHeightPx),
                new Avalonia.Vector(dpi, dpi));
            bitmap.Render(PrintableSheetPanel);
            bitmap.Save(tempPath);

            OpenWithSystemDefault(tempPath);
            mainWindow.SetStatus("Feuille ouverte pour impression.");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur d'impression");
        }
    }

    private static void OpenWithSystemDefault(string filePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true
        };
        Process.Start(psi);
    }

    private void RefreshPrintableGroups(MainWindow mainWindow)
    {
        mainWindow.PrintableRotationGroups.Clear();
        foreach (var group in mainWindow.GeneratedMatches
                     .OrderBy(m => m.RoundNumber)
                     .ThenBy(m => m.TerrainNumber)
                     .GroupBy(m => m.RoundNumber))
        {
            mainWindow.PrintableRotationGroups.Add(new PrintableRotationGroup
            {
                RoundNumber = group.Key,
                Matches = group.OrderBy(m => m.TerrainNumber).ToList()
            });
        }

        Dispatcher.UIThread.Post(() =>
        {
            ApplyPrintStyle(_currentPrintStyle);
        }, DispatcherPriority.Loaded);
    }

    private void UpdatePrintableSheetHeader()
    {
        var date = MatchDatePicker.SelectedDate ?? DateTime.Today;
        PrintableSheetTitleTextBlock.Text = $"MATCH DU {date:dd/MM/yyyy}";
    }

    private void ApplyPrintStyle(IPrintStyle style)
    {
        PrintableSheetTitleTextBlock.Foreground = new SolidColorBrush(style.TitleForeground);
        PrintableSheetPanel.Background = new SolidColorBrush(style.SheetBackground);
        PrintableSheetPanel.BorderBrush = new SolidColorBrush(style.PanelBorderColor);
        UpdateStyleInVisualTree(PrintableSheetPanel, style);
    }

    private static void UpdateStyleInVisualTree(Avalonia.Visual parent, IPrintStyle style)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Border border && border.Tag is string tag)
            {
                if (tag == "RotationHeader")
                {
                    border.Background = new SolidColorBrush(style.RotationHeaderBackground);
                    if (border.Child is TextBlock headerText)
                    {
                        headerText.Foreground = new SolidColorBrush(style.RotationHeaderForeground);
                    }
                }
                else if (tag == "ColumnHeader")
                {
                    border.Background = new SolidColorBrush(style.ColumnHeaderBackground);
                    border.BorderBrush = new SolidColorBrush(style.CellBorderColor);
                    if (border.Child is TextBlock headerCellText)
                    {
                        headerCellText.Foreground = new SolidColorBrush(style.ColumnHeaderForeground);
                    }
                }
                else if (tag == "DataCell")
                {
                    border.Background = new SolidColorBrush(style.DataRowBackground);
                    border.BorderBrush = new SolidColorBrush(style.CellBorderColor);
                    if (border.Child is TextBlock cellText)
                    {
                        cellText.Foreground = new SolidColorBrush(style.DataRowForeground);
                    }
                }
            }

            if (child is Avalonia.Visual visualChild)
            {
                UpdateStyleInVisualTree(visualChild, style);
            }
        }
    }

    private async void GeneratedMatchesDataGrid_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        var source = e.Source as Avalonia.Visual;
        var row = source?.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is not UiMatch selectedMatch)
        {
            return;
        }

        var availablePlayerNames = mainWindow.Players.Select(p => p.Name).OrderBy(n => n).ToList();
        if (availablePlayerNames.Count < 4)
        {
            await mainWindow.ShowWarningAsync("Vous avez besoin d'au moins 4 joueurs pour modifier un match.", "Validation");
            return;
        }

        var originalTeamAPlayer1 = selectedMatch.TeamAPlayer1;
        var originalTeamAPlayer2 = selectedMatch.TeamAPlayer2;
        var originalTeamBPlayer1 = selectedMatch.TeamBPlayer1;
        var originalTeamBPlayer2 = selectedMatch.TeamBPlayer2;

        var dialog = new EditMatchDialog(availablePlayerNames, selectedMatch);
        var result = await dialog.ShowDialog<bool>(mainWindow);

        if (!result)
        {
            return;
        }

        var replacements = BuildReplacementMap(
            originalTeamAPlayer1, originalTeamAPlayer2, originalTeamBPlayer1, originalTeamBPlayer2,
            selectedMatch.TeamAPlayer1, selectedMatch.TeamAPlayer2, selectedMatch.TeamBPlayer1, selectedMatch.TeamBPlayer2);

        if (replacements.Count == 0)
        {
            return;
        }

        var applyScope = await mainWindow.ShowYesNoCancelAsync(
            "Voulez-vous remplacer ce joueur dans tous les matchs ?\n\nOui = tous les matchs\nNon = uniquement ce match\nAnnuler = revenir en arrière",
            "Portée du remplacement");

        if (applyScope is null)
        {
            selectedMatch.TeamAPlayer1 = originalTeamAPlayer1;
            selectedMatch.TeamAPlayer2 = originalTeamAPlayer2;
            selectedMatch.TeamBPlayer1 = originalTeamBPlayer1;
            selectedMatch.TeamBPlayer2 = originalTeamBPlayer2;
            return;
        }

        if (applyScope == true)
        {
            ApplyReplacementsToAllMatches(mainWindow, selectedMatch, replacements);
            RefreshPrintableGroups(mainWindow);
        }

        mainWindow.MarkSheetAsChanged();
    }

    public bool TrySaveCurrentSheetForExit(out string? errorMessage)
    {
        errorMessage = null;

        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null || workbookService is null)
        {
            return true;
        }

        if (mainWindow.GeneratedMatches.Count == 0)
        {
            mainWindow.MarkSheetAsSaved();
            return true;
        }

        var selectedDate = MatchDatePicker.SelectedDate;
        if (selectedDate is null)
        {
            errorMessage = "Veuillez sélectionner une date avant d'enregistrer le planning.";
            return false;
        }

        try
        {
            workbookService.SavePlannedMatches(selectedDate.Value, mainWindow.GeneratedMatches.ToList());
            mainWindow.MarkSheetAsSaved();
            mainWindow.SetStatus($"Planning du {selectedDate.Value:dd/MM/yyyy} enregistré avant fermeture.");
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static Dictionary<string, string> BuildReplacementMap(
        string oldA1, string oldA2, string oldB1, string oldB2,
        string newA1, string newA2, string newB1, string newB2)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddReplacementIfChanged(replacements, oldA1, newA1);
        AddReplacementIfChanged(replacements, oldA2, newA2);
        AddReplacementIfChanged(replacements, oldB1, newB1);
        AddReplacementIfChanged(replacements, oldB2, newB2);
        return replacements;
    }

    private static void AddReplacementIfChanged(Dictionary<string, string> replacements, string oldValue, string newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (replacements.TryGetValue(oldValue, out var existingNewValue)
            && !string.Equals(existingNewValue, newValue, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        replacements[oldValue] = newValue;
    }

    private static void ApplyReplacementsToAllMatches(MainWindow mainWindow, UiMatch editedMatch, Dictionary<string, string> replacements)
    {
        foreach (var match in mainWindow.GeneratedMatches)
        {
            if (ReferenceEquals(match, editedMatch))
            {
                continue;
            }

            match.TeamAPlayer1 = GetReplacedValue(match.TeamAPlayer1, replacements);
            match.TeamAPlayer2 = GetReplacedValue(match.TeamAPlayer2, replacements);
            match.TeamBPlayer1 = GetReplacedValue(match.TeamBPlayer1, replacements);
            match.TeamBPlayer2 = GetReplacedValue(match.TeamBPlayer2, replacements);
        }
    }

    private static string GetReplacedValue(string currentValue, IReadOnlyDictionary<string, string> replacements)
        => replacements.TryGetValue(currentValue, out var r) ? r : currentValue;

    private static int GetSelectedComboNumber(ComboBox comboBox, int defaultValue)
    {
        if (comboBox.SelectedItem is not ComboBoxItem item)
        {
            return defaultValue;
        }

        return int.TryParse(item.Content?.ToString(), out var number) ? number : defaultValue;
    }
}
