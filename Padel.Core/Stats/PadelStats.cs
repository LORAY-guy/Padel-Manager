using Padel.Core.Model;

namespace Padel.Core.Stats;

/// <summary>One player's standing in the leaderboard.</summary>
public sealed record LeaderboardRow(
    string Name,
    int Level,
    int MatchesPlayed,
    int MatchesWon,
    int TotalPoints,
    int TotalScoredGames,
    double AveragePoints);

/// <summary>One played match with resolved player names.</summary>
public sealed record MatchRow(
    DateTime Date,
    int MatchNumber,
    string TeamAPlayer1,
    string TeamAPlayer2,
    string TeamBPlayer1,
    string TeamBPlayer2,
    int ScoreA,
    int ScoreB,
    int TeamAPoints,
    int TeamBPoints);

/// <summary>
/// Pure leaderboard / match-history computation over a <see cref="PadelDataFile"/>.
/// Shared by the desktop app and the web app so both show identical numbers.
/// </summary>
public static class PadelStats
{
    public static IReadOnlyList<LeaderboardRow> Leaderboard(PadelDataFile data)
    {
        var namesById = data.Players.ToDictionary(p => p.Id, p => p.Name);
        var levelByName = data.Players
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Level, StringComparer.OrdinalIgnoreCase);

        var stats = new Dictionary<string, Acc>(StringComparer.OrdinalIgnoreCase);
        foreach (var player in data.Players)
        {
            stats.TryAdd(player.Name, new Acc());
        }

        foreach (var score in data.ScoreEntries.OrderBy(s => s.Date).ThenBy(s => s.MatchNumber))
        {
            if (!TryNames(namesById, score, out var a1, out var a2, out var b1, out var b2))
            {
                continue;
            }

            Add(stats, a1, score.TeamAPoints, score.ScoreA);
            Add(stats, a2, score.TeamAPoints, score.ScoreA);
            Add(stats, b1, score.TeamBPoints, score.ScoreB);
            Add(stats, b2, score.TeamBPoints, score.ScoreB);
        }

        return stats
            .Select(kvp =>
            {
                var s = kvp.Value;
                var avg = s.Played == 0 ? 0d : (double)s.Points / s.Played;
                var level = levelByName.TryGetValue(kvp.Key, out var lvl) ? lvl : 0;
                return new LeaderboardRow(kvp.Key, level, s.Played, s.Won, s.Points, s.ScoredGames, avg);
            })
            .OrderByDescending(r => r.TotalPoints)
            .ThenByDescending(r => r.MatchesWon)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<MatchRow> History(PadelDataFile data)
    {
        var namesById = data.Players.ToDictionary(p => p.Id, p => p.Name);
        var rows = new List<MatchRow>();

        foreach (var score in data.ScoreEntries)
        {
            if (!TryNames(namesById, score, out var a1, out var a2, out var b1, out var b2))
            {
                continue;
            }

            rows.Add(new MatchRow(
                score.Date, score.MatchNumber,
                a1, a2, b1, b2,
                score.ScoreA, score.ScoreB,
                score.TeamAPoints, score.TeamBPoints));
        }

        return rows
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.MatchNumber)
            .ToList();
    }

    private static bool TryNames(
        IReadOnlyDictionary<int, string> namesById,
        ScoreEntry score,
        out string a1, out string a2, out string b1, out string b2)
    {
        a1 = a2 = b1 = b2 = string.Empty;
        if (!namesById.TryGetValue(score.TeamAPlayer1Id, out var n1)
            || !namesById.TryGetValue(score.TeamAPlayer2Id, out var n2)
            || !namesById.TryGetValue(score.TeamBPlayer1Id, out var n3)
            || !namesById.TryGetValue(score.TeamBPlayer2Id, out var n4))
        {
            return false;
        }

        a1 = n1; a2 = n2; b1 = n3; b2 = n4;
        return true;
    }

    private static void Add(IDictionary<string, Acc> stats, string name, int points, int scoredGames)
    {
        if (!stats.TryGetValue(name, out var acc))
        {
            acc = new Acc();
            stats[name] = acc;
        }

        acc.Played++;
        if (points == 3) acc.Won++;
        acc.Points += points;
        acc.ScoredGames += scoredGames;
    }

    private sealed class Acc
    {
        public int Played;
        public int Won;
        public int Points;
        public int ScoredGames;
    }
}
