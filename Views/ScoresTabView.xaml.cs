using Avalonia.Controls;
using Avalonia.Interactivity;
using Padel.Manager.Models;

namespace Padel.Manager.Views;

public partial class ScoresTabView : UserControl
{
    public ScoresTabView()
    {
        InitializeComponent();
    }

    private MainWindow? GetMainWindow() => TopLevel.GetTopLevel(this) as MainWindow;

    private async void DeleteSelectedMatchButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null)
        {
            return;
        }

        if (MatchHistoryDataGrid.SelectedItem is not MatchHistoryEntry selectedMatch)
        {
            await mainWindow.ShowWarningAsync("Sélectionnez un match à supprimer.", "Validation");
            return;
        }

        var confirm = await mainWindow.ShowYesNoAsync(
            $"Supprimer le match {selectedMatch.MatchNumber} du {selectedMatch.Date:dd/MM/yyyy} ?\n\n{selectedMatch.EquipeA} vs {selectedMatch.EquipeB}",
            "Confirmation");

        if (!confirm)
        {
            return;
        }

        if (workbookService is not null)
        {
            var removed = workbookService.DeleteScoreEntry(selectedMatch.Date, selectedMatch.MatchNumber);
            if (removed > 0)
            {
                mainWindow.MatchHistory.Remove(selectedMatch);
                mainWindow.SetStatus("Match supprimé. Les points ont été recalculés.");
                ReloadScores();
            }
            else
            {
                await mainWindow.ShowErrorAsync("Le match n'a pas pu être supprimé.", "Erreur");
            }
        }
    }

    public void ReloadScores(MainWindow? mainWindow = null)
    {
        mainWindow ??= GetMainWindow();
        var workbookService = mainWindow?.WorkbookService;
        if (mainWindow is null || workbookService is null)
        {
            return;
        }

        var scoreData = workbookService.LoadScoreTabData();
        FillCollection(mainWindow.MatchHistory, scoreData.History);
        FillCollection(mainWindow.ScoreSummaries, scoreData.Summaries);
        mainWindow.ScoreTabLoaded = true;
    }

    private static void FillCollection<T>(System.Collections.ObjectModel.ObservableCollection<T> destination, IEnumerable<T> source)
    {
        destination.Clear();
        foreach (var item in source)
        {
            destination.Add(item);
        }
    }
}
