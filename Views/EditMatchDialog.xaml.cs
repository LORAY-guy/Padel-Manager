using Avalonia.Controls;
using Avalonia.Interactivity;
using Padel.Manager.Models;

namespace Padel.Manager.Views;

public partial class EditMatchDialog : Window
{
    private List<string> _availablePlayerNames = new();
    private UiMatch? _match;

    public EditMatchDialog() => InitializeComponent();

    public EditMatchDialog(List<string> availablePlayerNames, UiMatch match)
    {
        InitializeComponent();
        _availablePlayerNames = availablePlayerNames.OrderBy(n => n).ToList();
        _match = match;

        InitializeComboBoxes();
        UpdateCurrentLabels();
    }

    private void InitializeComboBoxes()
    {
        if (_match is null)
        {
            return;
        }

        TeamAPlayer1ComboBox.ItemsSource = _availablePlayerNames;
        TeamAPlayer1ComboBox.SelectedItem = _match.TeamAPlayer1;

        TeamAPlayer2ComboBox.ItemsSource = _availablePlayerNames;
        TeamAPlayer2ComboBox.SelectedItem = _match.TeamAPlayer2;

        TeamBPlayer1ComboBox.ItemsSource = _availablePlayerNames;
        TeamBPlayer1ComboBox.SelectedItem = _match.TeamBPlayer1;

        TeamBPlayer2ComboBox.ItemsSource = _availablePlayerNames;
        TeamBPlayer2ComboBox.SelectedItem = _match.TeamBPlayer2;
    }

    private void UpdateCurrentLabels()
    {
        if (_match is null)
        {
            return;
        }

        TeamACurrentLabel.Text = $"Équipe actuelle: {_match.TeamA}";
        TeamBCurrentLabel.Text = $"Équipe actuelle: {_match.TeamB}";
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void ApplyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_match is null)
        {
            Close(false);
            return;
        }

        var p1A = TeamAPlayer1ComboBox.SelectedItem as string;
        var p2A = TeamAPlayer2ComboBox.SelectedItem as string;
        var p1B = TeamBPlayer1ComboBox.SelectedItem as string;
        var p2B = TeamBPlayer2ComboBox.SelectedItem as string;

        if (string.IsNullOrEmpty(p1A) || string.IsNullOrEmpty(p2A) ||
            string.IsNullOrEmpty(p1B) || string.IsNullOrEmpty(p2B))
        {
            var owner = Owner as MainWindow;
            if (owner is not null)
            {
                await owner.ShowWarningAsync("Veuillez sélectionner tous les joueurs.", "Validation");
            }
            return;
        }

        if (p1A == p2A || p1A == p1B || p1A == p2B ||
            p2A == p1B || p2A == p2B || p1B == p2B)
        {
            var owner = Owner as MainWindow;
            if (owner is not null)
            {
                await owner.ShowWarningAsync("Les mêmes joueurs ne peuvent pas se rencontrer deux fois.", "Validation");
            }
            return;
        }

        _match.TeamAPlayer1 = p1A;
        _match.TeamAPlayer2 = p2A;
        _match.TeamBPlayer1 = p1B;
        _match.TeamBPlayer2 = p2B;

        Close(true);
    }
}
