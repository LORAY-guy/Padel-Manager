using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Padel.Manager.Models;

public sealed class TournamentMatch : INotifyPropertyChanged
{
    private DateTime _matchDate;
    private string _teamA = string.Empty;
    private string _teamB = string.Empty;
    private string _scoreA = string.Empty;
    private string _scoreB = string.Empty;

    public required DateTime TournamentDate { get; init; }

    public required int RoundNumber { get; init; }

    public required int MatchNumber { get; init; }

    public DateTime MatchDate
    {
        get => _matchDate;
        set
        {
            var normalized = value.Date;
            if (_matchDate == normalized)
            {
                return;
            }

            _matchDate = normalized;
            OnPropertyChanged();
        }
    }

    public string TeamA
    {
        get => _teamA;
        set
        {
            if (_teamA == value)
            {
                return;
            }

            _teamA = value;
            OnPropertyChanged();
        }
    }

    public string TeamB
    {
        get => _teamB;
        set
        {
            if (_teamB == value)
            {
                return;
            }

            _teamB = value;
            OnPropertyChanged();
        }
    }

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

    // Optional source tokens (e.g. "Vainqueur Q1-M1") used to recompute progression.
    public string? SourceTeamA { get; set; }

    // Optional source tokens (e.g. "Perdant Q1-M1") used to recompute progression.
    public string? SourceTeamB { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
