using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using IoPath = System.IO.Path;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Padel.Manager.Models;
using Padel.Manager.Services;
using SkiaSharp;

namespace Padel.Manager.Views;

public partial class TournamentTabView : UserControl
{
    private readonly Random _random = new();
    private readonly List<IPrintStyle> _availablePrintStyles = new();
    private readonly List<Line> _bracketLines = new();
    private bool _isSynchronizingRoundDates;
    private bool _isHandlingPanelSizeChange;
    private bool _isPropagatingTournamentResults;
    private IPrintStyle _currentPrintStyle = new ColoredPrintStyle();

    public TournamentTabView()
    {
        InitializeComponent();
        Loaded += TournamentTabView_OnLoaded;
    }

    private MainWindow? GetMainWindow() => TopLevel.GetTopLevel(this) as MainWindow;

    private void TournamentTabView_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (TournamentDatePicker.SelectedDate is null)
        {
            TournamentDatePicker.SelectedDate = DateTime.Today;
        }

        InitializePrintStyles();
        TournamentPrintablePanel.SizeChanged -= TournamentPrintablePanel_OnSizeChanged;
        TournamentPrintablePanel.SizeChanged += TournamentPrintablePanel_OnSizeChanged;
        RefreshBracketPreview();

        if (GetMainWindow() is { } mainWindow)
        {
            mainWindow.TournamentMatches.CollectionChanged -= TournamentMatches_OnCollectionChanged;
            mainWindow.TournamentMatches.CollectionChanged += TournamentMatches_OnCollectionChanged;
        }
    }

    private void TournamentPrintablePanel_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_isHandlingPanelSizeChange)
        {
            return;
        }

        if (!e.WidthChanged)
        {
            return;
        }

        var mainWindow = GetMainWindow();
        if (mainWindow is null || mainWindow.TournamentMatches.Count == 0)
        {
            return;
        }

        _isHandlingPanelSizeChange = true;
        try
        {
            RenderBracketCanvas(mainWindow.TournamentMatches.ToList());
            ApplyPrintStyle(_currentPrintStyle);
        }
        finally
        {
            _isHandlingPanelSizeChange = false;
        }
    }

    private void TournamentDatePicker_OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateTitle();
    }

    private void SelectAllPlayersButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetMainWindow() is not { } mainWindow)
        {
            return;
        }

        foreach (var selectablePlayer in mainWindow.TournamentPlayerPool)
        {
            selectablePlayer.IsSelected = true;
        }
    }

    private void ClearPlayersButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetMainWindow() is not { } mainWindow)
        {
            return;
        }

        foreach (var selectablePlayer in mainWindow.TournamentPlayerPool)
        {
            selectablePlayer.IsSelected = false;
        }
    }

    private async void GenerateBracketButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null || workbookService is null)
        {
            return;
        }

        var selectedDate = TournamentDatePicker.SelectedDate?.Date;
        if (selectedDate is null)
        {
            await mainWindow.ShowWarningAsync("Veuillez selectionner une date de tournoi.", "Validation");
            return;
        }

        var selectedPlayers = mainWindow.TournamentPlayerPool
            .Where(p => p.IsSelected)
            .Select(p => p.Player)
            .ToList();

        if (selectedPlayers.Count < 4)
        {
            await mainWindow.ShowWarningAsync("Selectionnez au moins 4 joueurs.", "Validation");
            return;
        }

        if (selectedPlayers.Count % 4 != 0)
        {
            await mainWindow.ShowWarningAsync("Le nombre de joueurs du tournoi doit etre un multiple de 4.", "Validation");
            return;
        }

        var averagePoints = workbookService.LoadAveragePointsByPlayer();
        var firstRoundMatches = MatchGenerationService.BuildBalancedMatches(selectedPlayers, averagePoints, 1, _random)
            .OrderBy(m => m.MatchNumber)
            .ToList();

        var fixedTeams = new List<string>(firstRoundMatches.Count * 2);
        foreach (var match in firstRoundMatches)
        {
            fixedTeams.Add($"{match.TeamAPlayer1} & {match.TeamAPlayer2}");
            fixedTeams.Add($"{match.TeamBPlayer1} & {match.TeamBPlayer2}");
        }

        mainWindow.TournamentMatches.Clear();

        var firstRoundMatchNumber = 1;
        for (var i = 0; i < fixedTeams.Count; i += 2)
        {
            mainWindow.TournamentMatches.Add(new TournamentMatch
            {
                TournamentDate = selectedDate.Value,
                MatchDate = selectedDate.Value,
                RoundNumber = 1,
                MatchNumber = firstRoundMatchNumber,
                TeamA = fixedTeams[i],
                TeamB = fixedTeams[i + 1],
                SourceTeamA = null,
                SourceTeamB = null,
                ScoreA = string.Empty,
                ScoreB = string.Empty
            });

            firstRoundMatchNumber++;
        }

        var firstRoundMatchCount = firstRoundMatches.Count;
        var winnerSeeds = Enumerable.Range(1, firstRoundMatchCount)
            .Select(i => $"Vainqueur Q1-M{i}")
            .ToList();
        var loserSeeds = Enumerable.Range(1, firstRoundMatchCount)
            .Select(i => $"Perdant Q1-M{i}")
            .ToList();

        var secondRoundQualifiedSeeds = new List<string>();
        var round2MatchNumber = 1;

        var winnerPairing = BuildPairingsWithCarry(winnerSeeds);
        foreach (var pair in winnerPairing.Pairs)
        {
            mainWindow.TournamentMatches.Add(new TournamentMatch
            {
                TournamentDate = selectedDate.Value,
                MatchDate = selectedDate.Value,
                RoundNumber = 2,
                MatchNumber = round2MatchNumber,
                TeamA = string.Empty,
                TeamB = string.Empty,
                SourceTeamA = pair.TeamA,
                SourceTeamB = pair.TeamB,
                ScoreA = string.Empty,
                ScoreB = string.Empty
            });

            secondRoundQualifiedSeeds.Add($"Vainqueur Q2-M{round2MatchNumber}");
            round2MatchNumber++;
        }

        secondRoundQualifiedSeeds.AddRange(winnerPairing.CarriedSeeds);

        var loserPairing = BuildPairingsWithCarry(loserSeeds);
        foreach (var pair in loserPairing.Pairs)
        {
            mainWindow.TournamentMatches.Add(new TournamentMatch
            {
                TournamentDate = selectedDate.Value,
                MatchDate = selectedDate.Value,
                RoundNumber = 2,
                MatchNumber = round2MatchNumber,
                TeamA = string.Empty,
                TeamB = string.Empty,
                SourceTeamA = pair.TeamA,
                SourceTeamB = pair.TeamB,
                ScoreA = string.Empty,
                ScoreB = string.Empty
            });

            secondRoundQualifiedSeeds.Add($"Vainqueur Q2-M{round2MatchNumber}");
            round2MatchNumber++;
        }

        secondRoundQualifiedSeeds.AddRange(loserPairing.CarriedSeeds);

        var roundNumber = 3;
        var currentSeeds = secondRoundQualifiedSeeds;
        while (currentSeeds.Count > 1)
        {
            var pairing = BuildPairingsWithCarry(currentSeeds);
            var nextSeeds = new List<string>();
            var matchNumber = 1;

            foreach (var pair in pairing.Pairs)
            {
                mainWindow.TournamentMatches.Add(new TournamentMatch
                {
                    TournamentDate = selectedDate.Value,
                    MatchDate = selectedDate.Value,
                    RoundNumber = roundNumber,
                    MatchNumber = matchNumber,
                    TeamA = string.Empty,
                    TeamB = string.Empty,
                    SourceTeamA = pair.TeamA,
                    SourceTeamB = pair.TeamB,
                    ScoreA = string.Empty,
                    ScoreB = string.Empty
                });

                nextSeeds.Add($"Vainqueur R{roundNumber}-M{matchNumber}");
                matchNumber++;
            }

            nextSeeds.AddRange(pairing.CarriedSeeds);
            currentSeeds = nextSeeds;
            roundNumber++;
        }

        PropagateTournamentProgression();
        RefreshBracketPreview();
        mainWindow.SetStatus($"Bracket genere: {selectedPlayers.Count} joueurs, equipes fixes, minimum 2 matchs garantis par joueur.");
    }

    private static (List<(string TeamA, string TeamB)> Pairs, List<string> CarriedSeeds) BuildPairingsWithCarry(IReadOnlyList<string> seeds)
    {
        var pairs = new List<(string TeamA, string TeamB)>();
        var carried = new List<string>();

        for (var i = 0; i < seeds.Count; i += 2)
        {
            if (i + 1 >= seeds.Count)
            {
                carried.Add(seeds[i]);
                continue;
            }

            pairs.Add((seeds[i], seeds[i + 1]));
        }

        return (pairs, carried);
    }

    private async void LoadTournamentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null || workbookService is null)
        {
            return;
        }

        var selectedDate = TournamentDatePicker.SelectedDate?.Date;
        if (selectedDate is null)
        {
            await mainWindow.ShowWarningAsync("Veuillez selectionner une date de tournoi.", "Validation");
            return;
        }

        try
        {
            var loaded = workbookService.LoadTournament(selectedDate.Value);
            if (loaded.Count == 0)
            {
                await mainWindow.ShowInfoAsync("Aucun tournoi enregistre pour cette date.", "Information");
                return;
            }

            mainWindow.TournamentMatches.Clear();
            foreach (var match in loaded)
            {
                mainWindow.TournamentMatches.Add(match);
            }

            PropagateTournamentProgression();
            RefreshBracketPreview();
            mainWindow.SetStatus($"Tournoi du {selectedDate:dd/MM/yyyy} charge ({loaded.Count} matchs).");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur de chargement du tournoi");
        }
    }

    private async void SaveTournamentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null || workbookService is null)
        {
            return;
        }

        if (mainWindow.TournamentMatches.Count == 0)
        {
            await mainWindow.ShowWarningAsync("Aucun tournoi a enregistrer.", "Validation");
            return;
        }

        var selectedDate = TournamentDatePicker.SelectedDate?.Date;
        if (selectedDate is null)
        {
            await mainWindow.ShowWarningAsync("Veuillez selectionner une date de tournoi.", "Validation");
            return;
        }

        try
        {
            workbookService.SaveTournament(selectedDate.Value, mainWindow.TournamentMatches.ToList());
            mainWindow.SetStatus($"Tournoi du {selectedDate:dd/MM/yyyy} enregistre.");
            await mainWindow.ShowInfoAsync("Le tournoi a ete sauvegarde.", "Succes");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur de sauvegarde du tournoi");
        }
    }

    private async void ApplyDateToAllMatchesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetMainWindow() is not { } mainWindow)
        {
            return;
        }

        var selectedDate = TournamentDatePicker.SelectedDate?.Date;
        if (selectedDate is null)
        {
            await mainWindow.ShowWarningAsync("Veuillez selectionner une date.", "Validation");
            return;
        }

        foreach (var match in mainWindow.TournamentMatches)
        {
            match.MatchDate = selectedDate.Value;
        }

        RefreshBracketPreview();
    }

    private async void EditDatesByCategoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        if (mainWindow.TournamentMatches.Count == 0)
        {
            await mainWindow.ShowWarningAsync("Aucun tournoi genere.", "Validation");
            return;
        }

        var roundItems = new ObservableCollection<RoundDateEditItem>(mainWindow.TournamentMatches
            .GroupBy(m => m.RoundNumber)
            .OrderBy(g => g.Key)
            .Select(g => new RoundDateEditItem
            {
                RoundNumber = g.Key,
                RoundLabel = GetRoundLabel(g.Key, mainWindow.TournamentMatches.Max(x => x.RoundNumber)),
                MatchDate = g.OrderBy(m => m.MatchNumber).First().MatchDate.Date
            }));

        var dialog = BuildRoundDateEditorDialog(roundItems);
        var confirmed = await dialog.ShowDialog<bool>(mainWindow);

        if (!confirmed)
        {
            return;
        }

        var byRound = roundItems.ToDictionary(x => x.RoundNumber, x => x.MatchDate.Date);
        foreach (var match in mainWindow.TournamentMatches)
        {
            if (byRound.TryGetValue(match.RoundNumber, out var roundDate))
            {
                match.MatchDate = roundDate;
            }
        }

        RefreshBracketPreview();
        mainWindow.SetStatus("Dates des categories mises a jour.");
    }

    private static Window BuildRoundDateEditorDialog(ObservableCollection<RoundDateEditItem> items)
    {
        var dialog = new Window
        {
            Title = "Modifier les dates par categorie",
            Width = 520,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Brushes.White
        };

        var root = new Grid { Margin = new Avalonia.Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var header = new TextBlock
        {
            Text = "Definir une date par categorie de match",
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            FontWeight = FontWeight.SemiBold
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = items
        };

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Categorie",
            Binding = new Avalonia.Data.Binding(nameof(RoundDateEditItem.RoundLabel)),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Date",
            Width = new DataGridLength(170),
            CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<RoundDateEditItem>((item, _) =>
                new TextBlock
                {
                    Text = item?.MatchDate.ToString("dd/MM/yyyy") ?? string.Empty,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Avalonia.Thickness(4, 0)
                }),
            CellEditingTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<RoundDateEditItem>((_, _) =>
            {
                var picker = new CalendarDatePicker();
                picker.Bind(CalendarDatePicker.SelectedDateProperty, new Avalonia.Data.Binding(nameof(RoundDateEditItem.MatchDate))
                {
                    Mode = Avalonia.Data.BindingMode.TwoWay
                });
                return picker;
            })
        });

        Grid.SetRow(grid, 1);
        root.Children.Add(grid);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 10, 0, 0)
        };

        var cancelButton = new Button
        {
            Content = "Annuler",
            MinWidth = 100,
            Margin = new Avalonia.Thickness(0, 0, 8, 0),
            Padding = new Avalonia.Thickness(12, 4, 12, 4)
        };
        cancelButton.Click += (_, _) => dialog.Close(false);

        var saveButton = new Button
        {
            Content = "Appliquer",
            MinWidth = 100,
            Padding = new Avalonia.Thickness(12, 4, 12, 4)
        };
        saveButton.Click += (_, _) => dialog.Close(true);

        actions.Children.Add(cancelButton);
        actions.Children.Add(saveButton);

        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        dialog.Content = root;
        return dialog;
    }

    private async void ExportBracketImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        if (mainWindow.TournamentMatches.Count == 0)
        {
            await mainWindow.ShowWarningAsync("Aucun bracket a exporter.", "Validation");
            return;
        }

        var selectedDate = TournamentDatePicker.SelectedDate ?? DateTime.Today;
        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exporter le bracket",
            DefaultExtension = "png",
            SuggestedFileName = $"Tournoi_{selectedDate:yyyyMMdd}.png",
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

        try
        {
            var title = $"TOURNOI DU {selectedDate:dd/MM/yyyy}";
            var matches = mainWindow.TournamentMatches.ToList();

            using var bitmap = BracketImageRenderer.Render(title, matches, _currentPrintStyle, dpi: 300f);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            await using var stream = File.Create(outputPath);
            data.SaveTo(stream);

            mainWindow.SetStatus($"Bracket exporte en image : {outputPath}");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur d'export image");
        }
    }

    private async void PrintBracketButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        if (mainWindow.TournamentMatches.Count == 0)
        {
            await mainWindow.ShowWarningAsync("Aucun bracket a imprimer.", "Validation");
            return;
        }

        try
        {
            var date = TournamentDatePicker.SelectedDate ?? DateTime.Today;
            var title = $"TOURNOI DU {date:dd/MM/yyyy}";
            var matches = mainWindow.TournamentMatches.ToList();
            var tempPath = IoPath.Combine(IoPath.GetTempPath(), $"Tournoi_{DateTime.Now:yyyyMMddHHmmss}.png");

            using var bitmap = BracketImageRenderer.Render(title, matches, _currentPrintStyle, dpi: 150f);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            await using var stream = File.Create(tempPath);
            data.SaveTo(stream);

            Process.Start(new ProcessStartInfo { FileName = tempPath, UseShellExecute = true });
            mainWindow.SetStatus("Bracket ouvert pour impression.");
        }
        catch (Exception ex)
        {
            await mainWindow.ShowErrorAsync(ex.Message, "Erreur d'impression");
        }
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

    private void PrintStyleComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PrintStyleComboBox.SelectedItem is IPrintStyle style)
        {
            _currentPrintStyle = style;
            ApplyPrintStyle(style);
        }
    }

    private void TournamentMatches_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var match in e.OldItems.OfType<TournamentMatch>())
            {
                match.PropertyChanged -= TournamentMatch_OnPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var match in e.NewItems.OfType<TournamentMatch>())
            {
                match.PropertyChanged += TournamentMatch_OnPropertyChanged;
            }
        }

        RefreshBracketPreview();
    }

    private void TournamentMatch_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isPropagatingTournamentResults)
        {
            return;
        }

        if (e.PropertyName == nameof(TournamentMatch.MatchDate) && sender is TournamentMatch changedMatch)
        {
            if (_isSynchronizingRoundDates)
            {
                return;
            }

            SynchronizeDateAcrossRound(changedMatch);
            RefreshBracketPreview();
            return;
        }

        if (e.PropertyName is nameof(TournamentMatch.TeamA)
            or nameof(TournamentMatch.TeamB)
            or nameof(TournamentMatch.ScoreA)
            or nameof(TournamentMatch.ScoreB))
        {
            if (e.PropertyName is nameof(TournamentMatch.ScoreA) or nameof(TournamentMatch.ScoreB))
            {
                PropagateTournamentProgression();
            }

            RefreshBracketPreview();
        }
    }

    private void PropagateTournamentProgression()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null || mainWindow.TournamentMatches.Count == 0)
        {
            return;
        }

        _isPropagatingTournamentResults = true;
        try
        {
            var tokenToTeam = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ordered = mainWindow.TournamentMatches
                .OrderBy(m => m.RoundNumber)
                .ThenBy(m => m.MatchNumber)
                .ToList();

            foreach (var match in ordered)
            {
                if (string.IsNullOrWhiteSpace(match.SourceTeamA) && IsProgressionToken(match.TeamA))
                {
                    match.SourceTeamA = match.TeamA;
                }

                if (string.IsNullOrWhiteSpace(match.SourceTeamB) && IsProgressionToken(match.TeamB))
                {
                    match.SourceTeamB = match.TeamB;
                }

                if (!string.IsNullOrWhiteSpace(match.SourceTeamA))
                {
                    match.TeamA = ResolveTeamFromToken(match.SourceTeamA!, tokenToTeam);
                }

                if (!string.IsNullOrWhiteSpace(match.SourceTeamB))
                {
                    match.TeamB = ResolveTeamFromToken(match.SourceTeamB!, tokenToTeam);
                }

                if (!TryGetWinnerLoser(match, out var winner, out var loser))
                {
                    RemoveResultTokens(tokenToTeam, match.RoundNumber, match.MatchNumber);
                    continue;
                }

                tokenToTeam[$"Vainqueur Q{match.RoundNumber}-M{match.MatchNumber}"] = winner;
                tokenToTeam[$"Perdant Q{match.RoundNumber}-M{match.MatchNumber}"] = loser;
                tokenToTeam[$"Vainqueur R{match.RoundNumber}-M{match.MatchNumber}"] = winner;
                tokenToTeam[$"Perdant R{match.RoundNumber}-M{match.MatchNumber}"] = loser;
            }
        }
        finally
        {
            _isPropagatingTournamentResults = false;
        }
    }

    private static void RemoveResultTokens(IDictionary<string, string> tokenToTeam, int roundNumber, int matchNumber)
    {
        tokenToTeam.Remove($"Vainqueur Q{roundNumber}-M{matchNumber}");
        tokenToTeam.Remove($"Perdant Q{roundNumber}-M{matchNumber}");
        tokenToTeam.Remove($"Vainqueur R{roundNumber}-M{matchNumber}");
        tokenToTeam.Remove($"Perdant R{roundNumber}-M{matchNumber}");
    }

    private static string ResolveTeamFromToken(string tokenOrTeam, IReadOnlyDictionary<string, string> tokenToTeam)
    {
        if (tokenToTeam.TryGetValue(tokenOrTeam, out var resolved))
        {
            return resolved;
        }

        return IsProgressionToken(tokenOrTeam) ? string.Empty : tokenOrTeam;
    }

    private static bool IsProgressionToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.StartsWith("Vainqueur ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Perdant ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetWinnerLoser(TournamentMatch match, out string winner, out string loser)
    {
        winner = string.Empty;
        loser = string.Empty;

        if (string.IsNullOrWhiteSpace(match.TeamA) || string.IsNullOrWhiteSpace(match.TeamB))
        {
            return false;
        }

        if (!int.TryParse(match.ScoreA, out var scoreA) || !int.TryParse(match.ScoreB, out var scoreB))
        {
            return false;
        }

        if (scoreA == scoreB)
        {
            return false;
        }

        if (scoreA > scoreB)
        {
            winner = match.TeamA;
            loser = match.TeamB;
            return true;
        }

        winner = match.TeamB;
        loser = match.TeamA;
        return true;
    }

    private void SynchronizeDateAcrossRound(TournamentMatch source)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        _isSynchronizingRoundDates = true;
        try
        {
            foreach (var match in mainWindow.TournamentMatches)
            {
                if (match.RoundNumber != source.RoundNumber || ReferenceEquals(match, source))
                {
                    continue;
                }

                if (match.MatchDate != source.MatchDate)
                {
                    match.MatchDate = source.MatchDate;
                }
            }
        }
        finally
        {
            _isSynchronizingRoundDates = false;
        }
    }

    private void RefreshBracketPreview()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            return;
        }

        mainWindow.TournamentBracketRounds.Clear();
        foreach (var group in mainWindow.TournamentMatches
                     .OrderBy(m => m.RoundNumber)
                     .ThenBy(m => m.MatchNumber)
                     .GroupBy(m => m.RoundNumber))
        {
            mainWindow.TournamentBracketRounds.Add(new TournamentBracketRound
            {
                RoundNumber = group.Key,
                Matches = group.ToList()
            });
        }

        RenderBracketCanvas(mainWindow.TournamentMatches.ToList());
        UpdateTitle();

        Dispatcher.UIThread.Post(() =>
        {
            ApplyPrintStyle(_currentPrintStyle);
        }, DispatcherPriority.Loaded);
    }

    private void UpdateTitle()
    {
        var date = TournamentDatePicker.SelectedDate ?? DateTime.Today;
        TournamentTitleTextBlock.Text = $"TOURNOI DU {date:dd/MM/yyyy}";
    }

    private void ApplyPrintStyle(IPrintStyle style)
    {
        TournamentTitleTextBlock.Foreground = new SolidColorBrush(style.TitleForeground);
        TournamentPrintablePanel.Background = new SolidColorBrush(style.SheetBackground);
        TournamentPrintablePanel.BorderBrush = new SolidColorBrush(style.PanelBorderColor);

        foreach (var line in _bracketLines)
        {
            line.Stroke = new SolidColorBrush(style.CellBorderColor);
        }

        UpdateStyleInVisualTree(TournamentPrintablePanel, style);
    }

    private void RenderBracketCanvas(List<TournamentMatch> matches)
    {
        BracketCanvas.Children.Clear();
        _bracketLines.Clear();

        if (matches.Count == 0)
        {
            BracketCanvas.Width = 900;
            BracketCanvas.Height = 420;
            return;
        }

        const double boxWidth = 240;
        const double boxHeight = 90;
        const double roundGap = 280;
        const double verticalGap = 30;
        const double headerHeight = 48;
        const double left = 12;

        var rounds = matches
            .GroupBy(m => m.RoundNumber)
            .OrderBy(g => g.Key)
            .Select(g => (RoundNumber: g.Key, Matches: g.OrderBy(m => m.MatchNumber).ToList()))
            .ToList();

        var positionsByRound = new Dictionary<int, List<double>>();
        var panelBottom = headerHeight + 16;

        for (var roundIndex = 0; roundIndex < rounds.Count; roundIndex++)
        {
            var round = rounds[roundIndex];
            var x = left + (roundIndex * roundGap);
            var roundDate = round.Matches.Select(m => m.MatchDate.Date).FirstOrDefault();

            var header = new Border
            {
                Tag = "RotationHeader",
                Width = boxWidth,
                Height = headerHeight,
                CornerRadius = new Avalonia.CornerRadius(4, 4, 0, 0),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = GetRoundLabel(round.RoundNumber, rounds.Count),
                            FontWeight = FontWeight.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = new SolidColorBrush(_currentPrintStyle.RotationHeaderForeground)
                        },
                        new TextBlock
                        {
                            Text = roundDate.ToString("dd/MM/yyyy"),
                            FontSize = 11,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = new SolidColorBrush(_currentPrintStyle.RotationHeaderForeground)
                        }
                    }
                }
            };

            Canvas.SetLeft(header, x);
            Canvas.SetTop(header, 0);
            BracketCanvas.Children.Add(header);

            var yPositions = ComputeRoundPositions(roundIndex, round.Matches.Count, rounds, positionsByRound, headerHeight + 16, boxHeight, verticalGap);
            positionsByRound[round.RoundNumber] = yPositions;

            for (var i = 0; i < round.Matches.Count; i++)
            {
                var match = round.Matches[i];
                var y = yPositions[i];

                var scoreText = string.IsNullOrWhiteSpace(match.ScoreA) && string.IsNullOrWhiteSpace(match.ScoreB)
                    ? "Score:        -        "
                    : $"Score: {match.ScoreA} - {match.ScoreB}";

                var card = new Border
                {
                    Tag = "DataCell",
                    Width = boxWidth,
                    Height = boxHeight,
                    BorderThickness = new Avalonia.Thickness(1),
                    Padding = new Avalonia.Thickness(7),
                    Child = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = string.IsNullOrWhiteSpace(match.TeamA) ? " " : match.TeamA, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap },
                            new TextBlock { Text = string.IsNullOrWhiteSpace(match.TeamB) ? " " : match.TeamB, Margin = new Avalonia.Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap },
                            new TextBlock { Text = scoreText, Margin = new Avalonia.Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center }
                        }
                    }
                };

                Canvas.SetLeft(card, x);
                Canvas.SetTop(card, y);
                BracketCanvas.Children.Add(card);
                panelBottom = Math.Max(panelBottom, y + boxHeight + 10);
            }
        }

        for (var roundIndex = 0; roundIndex < rounds.Count - 1; roundIndex++)
        {
            var current = rounds[roundIndex];
            var next = rounds[roundIndex + 1];

            if (current.RoundNumber <= 1)
            {
                continue;
            }

            var currentYs = positionsByRound[current.RoundNumber];
            var nextYs = positionsByRound[next.RoundNumber];

            var currentXRight = left + (roundIndex * roundGap) + boxWidth;
            var nextXLeft = left + ((roundIndex + 1) * roundGap);
            var mergeX = nextXLeft - 28;

            if (currentYs.Count == nextYs.Count)
            {
                for (var i = 0; i < currentYs.Count; i++)
                {
                    var y = currentYs[i] + (boxHeight / 2d);
                    AddLine(currentXRight, y, nextXLeft, y);
                }

                continue;
            }

            if (currentYs.Count == nextYs.Count * 2)
            {
                for (var i = 0; i < nextYs.Count; i++)
                {
                    var y1 = currentYs[i * 2] + (boxHeight / 2d);
                    var y2 = currentYs[(i * 2) + 1] + (boxHeight / 2d);
                    var yTarget = nextYs[i] + (boxHeight / 2d);

                    AddLine(currentXRight, y1, mergeX, y1);
                    AddLine(currentXRight, y2, mergeX, y2);
                    AddLine(mergeX, y1, mergeX, y2);
                    AddLine(mergeX, yTarget, nextXLeft, yTarget);
                }

                continue;
            }

            for (var i = 0; i < Math.Min(currentYs.Count, nextYs.Count); i++)
            {
                var y1 = currentYs[i] + (boxHeight / 2d);
                var y2 = nextYs[i] + (boxHeight / 2d);
                AddLine(currentXRight, y1, nextXLeft, y2);
            }
        }

        var rawWidth = left + (Math.Max(0, rounds.Count - 1) * roundGap) + boxWidth + 20;
        var rawHeight = panelBottom + 20;

        BracketCanvas.RenderTransform = null;
        BracketCanvas.Width = Math.Max(900, rawWidth);
        BracketCanvas.Height = Math.Max(420, rawHeight);

        var panelWidth = TournamentPrintablePanel.Bounds.Width;
        if (panelWidth <= 0 && !double.IsNaN(TournamentPrintablePanel.Width))
        {
            panelWidth = TournamentPrintablePanel.Width;
        }

        var availableWidth = panelWidth
            - TournamentPrintablePanel.Padding.Left
            - TournamentPrintablePanel.Padding.Right
            - TournamentPrintablePanel.BorderThickness.Left
            - TournamentPrintablePanel.BorderThickness.Right
            - 4d;

        if (availableWidth > 0 && rawWidth > availableWidth)
        {
            var scale = availableWidth / rawWidth;
            BracketCanvas.RenderTransform = new ScaleTransform(scale, scale);
        }
    }

    private static List<double> ComputeRoundPositions(
        int roundIndex,
        int matchCount,
        IReadOnlyList<(int RoundNumber, List<TournamentMatch> Matches)> rounds,
        IReadOnlyDictionary<int, List<double>> positionsByRound,
        double top,
        double boxHeight,
        double verticalGap)
    {
        if (roundIndex == 0)
        {
            return Enumerable.Range(0, matchCount)
                .Select(i => top + (i * (boxHeight + verticalGap)))
                .ToList();
        }

        var previous = rounds[roundIndex - 1];
        var previousPositions = positionsByRound[previous.RoundNumber];

        if (previousPositions.Count == matchCount)
        {
            return previousPositions.ToList();
        }

        if (previousPositions.Count == matchCount * 2)
        {
            var result = new List<double>(matchCount);
            for (var i = 0; i < matchCount; i++)
            {
                var center1 = previousPositions[i * 2] + (boxHeight / 2d);
                var center2 = previousPositions[(i * 2) + 1] + (boxHeight / 2d);
                var center = (center1 + center2) / 2d;
                result.Add(center - (boxHeight / 2d));
            }

            return result;
        }

        return Enumerable.Range(0, matchCount)
            .Select(i => top + (i * (boxHeight + verticalGap)))
            .ToList();
    }

    private void AddLine(double x1, double y1, double x2, double y2)
    {
        var line = new Line
        {
            StartPoint = new Avalonia.Point(x1, y1),
            EndPoint = new Avalonia.Point(x2, y2),
            StrokeThickness = 1.5
        };

        _bracketLines.Add(line);
        BracketCanvas.Children.Add(line);
    }

    private static string GetRoundLabel(int roundNumber, int totalRounds)
    {
        if (roundNumber <= 2)
        {
            return $"QUALIFICATION {roundNumber}";
        }

        var knockoutRoundIndex = roundNumber - 2;
        var knockoutRoundCount = Math.Max(1, totalRounds - 2);
        var roundsFromFinal = knockoutRoundCount - knockoutRoundIndex;

        return roundsFromFinal switch
        {
            0 => "FINALE",
            1 => "DEMI-FINALE",
            2 => "QUART DE FINALE",
            3 => "8ÈMES DE FINALE",
            4 => "16ÈMES DE FINALE",
            5 => "32ÈMES DE FINALE",
            _ => $"TOUR ELIMINATOIRE {knockoutRoundIndex}"
        };
    }

    private static void UpdateStyleInVisualTree(Avalonia.Visual parent, IPrintStyle style)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Border border)
            {
                var tag = border.Tag as string;
                if (tag == "RotationHeader")
                {
                    border.Background = new SolidColorBrush(style.RotationHeaderBackground);
                    border.BorderBrush = new SolidColorBrush(style.CellBorderColor);

                    if (border.Child is StackPanel headerStack)
                    {
                        foreach (var text in headerStack.Children.OfType<TextBlock>())
                        {
                            text.Foreground = new SolidColorBrush(style.RotationHeaderForeground);
                        }
                    }
                }
                else if (tag == "DataCell")
                {
                    border.Background = new SolidColorBrush(style.DataRowBackground);
                    border.BorderBrush = new SolidColorBrush(style.CellBorderColor);

                    if (border.Child is StackPanel stack)
                    {
                        foreach (var text in stack.Children.OfType<TextBlock>())
                        {
                            text.Foreground = new SolidColorBrush(style.DataRowForeground);
                        }
                    }
                }
            }

            if (child is Avalonia.Visual visualChild)
            {
                UpdateStyleInVisualTree(visualChild, style);
            }
        }
    }

    private sealed class RoundDateEditItem : INotifyPropertyChanged
    {
        private DateTime _matchDate;

        public required int RoundNumber { get; init; }
        public required string RoundLabel { get; init; }

        public DateTime MatchDate
        {
            get => _matchDate;
            set
            {
                _matchDate = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MatchDate)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
