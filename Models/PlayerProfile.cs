using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Padel.Manager.Models;

public sealed class PlayerProfile : INotifyPropertyChanged
{
    private int _level;
    private int _matchesPlayed;
    private int _matchesWon;
    private int _totalPoints;
    private double _averagePoints;
    private int _recentMatchCount;
    private double _recentAverage;
    private double _recentDelta;

    public required string Name { get; init; }

    public required int Level
    {
        get => _level;
        set
        {
            if (_level == value)
            {
                return;
            }

            _level = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int MatchesPlayed
    {
        get => _matchesPlayed;
        set
        {
            if (_matchesPlayed == value)
            {
                return;
            }

            _matchesPlayed = value;
            OnPropertyChanged();
        }
    }

    public int MatchesWon
    {
        get => _matchesWon;
        set
        {
            if (_matchesWon == value)
            {
                return;
            }

            _matchesWon = value;
            OnPropertyChanged();
        }
    }

    public int TotalPoints
    {
        get => _totalPoints;
        set
        {
            if (_totalPoints == value)
            {
                return;
            }

            _totalPoints = value;
            OnPropertyChanged();
        }
    }

    public double AveragePoints
    {
        get => _averagePoints;
        set
        {
            if (Math.Abs(_averagePoints - value) < 0.0001d)
            {
                return;
            }

            _averagePoints = value;
            OnPropertyChanged();
        }
    }

    public int RecentMatchCount
    {
        get => _recentMatchCount;
        set
        {
            if (_recentMatchCount == value)
            {
                return;
            }

            _recentMatchCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecentFormDisplay));
        }
    }

    public double RecentAverage
    {
        get => _recentAverage;
        set
        {
            if (Math.Abs(_recentAverage - value) < 0.0001d)
            {
                return;
            }

            _recentAverage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecentFormDisplay));
        }
    }

    public double RecentDelta
    {
        get => _recentDelta;
        set
        {
            if (Math.Abs(_recentDelta - value) < 0.0001d)
            {
                return;
            }

            _recentDelta = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecentFormDisplay));
        }
    }

    public string RecentFormDisplay
    {
        get
        {
            if (_recentMatchCount == 0)
            {
                return "—";
            }

            var arrow = _recentDelta > 0.05 ? "▲" : _recentDelta < -0.05 ? "▼" : "▶";
            var sign = _recentDelta >= 0 ? "+" : "";
            return $"{_recentAverage:F2} {arrow} ({sign}{_recentDelta:F2})";
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
