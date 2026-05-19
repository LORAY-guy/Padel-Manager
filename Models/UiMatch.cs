using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Padel.Manager.Models;

public sealed class UiMatch : INotifyPropertyChanged
{
    private int _terrainNumber;
    private string _scoreA = string.Empty;
    private string _scoreB = string.Empty;
    private string? _teamA;
    private string? _teamB;
    private string _teamAPlayer1 = string.Empty;
    private string _teamAPlayer2 = string.Empty;
    private string _teamBPlayer1 = string.Empty;
    private string _teamBPlayer2 = string.Empty;

    public required int RoundNumber { get; init; }

    public required int MatchNumber { get; init; }

    public int TerrainNumber
    {
        get => _terrainNumber;
        set
        {
            if (_terrainNumber == value)
            {
                return;
            }

            _terrainNumber = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TerrainNumberText));
        }
    }

    public string TerrainNumberText
    {
        get => TerrainNumber <= 0 ? string.Empty : TerrainNumber.ToString();
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                TerrainNumber = 0;
                return;
            }

            if (!int.TryParse(value, out var parsed) || parsed < 0)
            {
                return;
            }

            TerrainNumber = parsed;
        }
    }

    public string TeamAPlayer1
    {
        get => _teamAPlayer1;
        set
        {
            if (_teamAPlayer1 == value)
            {
                return;
            }

            _teamAPlayer1 = value;
            _teamA = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TeamA));
        }
    }

    public string TeamAPlayer2
    {
        get => _teamAPlayer2;
        set
        {
            if (_teamAPlayer2 == value)
            {
                return;
            }

            _teamAPlayer2 = value;
            _teamA = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TeamA));
        }
    }

    public string TeamBPlayer1
    {
        get => _teamBPlayer1;
        set
        {
            if (_teamBPlayer1 == value)
            {
                return;
            }

            _teamBPlayer1 = value;
            _teamB = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TeamB));
        }
    }

    public string TeamBPlayer2
    {
        get => _teamBPlayer2;
        set
        {
            if (_teamBPlayer2 == value)
            {
                return;
            }

            _teamBPlayer2 = value;
            _teamB = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TeamB));
        }
    }

    public string TeamA => _teamA ??= $"{TeamAPlayer1} & {TeamAPlayer2}";

    public string TeamB => _teamB ??= $"{TeamBPlayer1} & {TeamBPlayer2}";

    public string ScoreA
    {
        get => _scoreA;
        set
        {
            if (_scoreA == value)
            {
                return;
            }

            _scoreA = value;
            OnPropertyChanged();
        }
    }

    public string ScoreB
    {
        get => _scoreB;
        set
        {
            if (_scoreB == value)
            {
                return;
            }

            _scoreB = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
