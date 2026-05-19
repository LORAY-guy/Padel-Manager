namespace Padel.Manager.Models;

public sealed class PlayerScoreSummary
{
    public required string Name { get; init; }

    public required int Level { get; init; }

    public required int MatchesPlayed { get; init; }

    public required int MatchesWon { get; init; }

    public required int TotalPoints { get; init; }

    public required double AveragePoints { get; init; }

    public required int RecentMatchCount { get; init; }

    public required double RecentAverage { get; init; }

    public required double RecentDelta { get; init; }
}
