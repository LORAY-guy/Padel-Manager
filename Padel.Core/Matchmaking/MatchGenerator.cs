using Padel.Core.Model;

namespace Padel.Core.Matchmaking;

/// <summary>A generated (unplayed) match: a round, a terrain, and the four player ids.</summary>
public sealed record GeneratedMatch(int Round, int Terrain, int A1, int A2, int B1, int B2);

/// <summary>
/// Balanced rotation generator, ported from the desktop's MatchGenerationService so
/// the web produces the same schedules. Pairs players to keep team levels/averages
/// close and avoid repeating the same pairs across rounds.
/// </summary>
public static class MatchGenerator
{
    private const double DefaultAverage = 2d;

    /// <summary>Average points per player id, computed from played matches (for balancing).</summary>
    public static Dictionary<int, double> AveragePointsById(PadelDataFile data)
    {
        var totals = new Dictionary<int, (double Sum, int Count)>();

        void Add(int id, int points)
        {
            totals.TryGetValue(id, out var t);
            totals[id] = (t.Sum + points, t.Count + 1);
        }

        foreach (var s in data.ScoreEntries)
        {
            Add(s.TeamAPlayer1Id, s.TeamAPoints);
            Add(s.TeamAPlayer2Id, s.TeamAPoints);
            Add(s.TeamBPlayer1Id, s.TeamBPoints);
            Add(s.TeamBPlayer2Id, s.TeamBPoints);
        }

        return totals.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count == 0 ? 0d : kvp.Value.Sum / kvp.Value.Count);
    }

    public static List<GeneratedMatch> Generate(
        IReadOnlyList<PlayerEntry> selectedPlayers,
        IReadOnlyDictionary<int, double> averageById,
        int rounds,
        Random random)
    {
        var output = new List<GeneratedMatch>();
        if (selectedPlayers.Count < 4 || rounds < 1)
        {
            return output;
        }

        var usedTeams = new HashSet<string>();
        var orderedBase = selectedPlayers
            .OrderByDescending(p => p.Level)
            .ThenByDescending(p => Avg(p.Id, averageById))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var round = 1; round <= rounds; round++)
        {
            var roundPlayers = Rotate(orderedBase, round - 1, random);
            for (var i = 0; i + 4 <= roundPlayers.Count; i += 4)
            {
                var group = roundPlayers.GetRange(i, 4);
                var best = ChooseBestPairing(group, averageById, usedTeams);
                usedTeams.Add(TeamKey(best.A1.Id, best.A2.Id));
                usedTeams.Add(TeamKey(best.B1.Id, best.B2.Id));

                output.Add(new GeneratedMatch(
                    round, (i / 4) + 1,
                    best.A1.Id, best.A2.Id, best.B1.Id, best.B2.Id));
            }
        }

        return output;
    }

    private static List<PlayerEntry> Rotate(List<PlayerEntry> players, int rotation, Random random)
    {
        var copy = players.ToList();
        if (rotation == 0)
        {
            return copy;
        }

        for (var i = 0; i < rotation; i++)
        {
            var first = copy[0];
            copy.RemoveAt(0);
            copy.Add(first);
        }

        for (var i = copy.Count - 1; i > 0; i--)
        {
            var swap = random.Next(i + 1);
            (copy[i], copy[swap]) = (copy[swap], copy[i]);
        }

        return copy;
    }

    private static (PlayerEntry A1, PlayerEntry A2, PlayerEntry B1, PlayerEntry B2) ChooseBestPairing(
        List<PlayerEntry> g,
        IReadOnlyDictionary<int, double> avg,
        HashSet<string> usedTeams)
    {
        var candidates = new[]
        {
            (g[0], g[1], g[2], g[3]),
            (g[0], g[2], g[1], g[3]),
            (g[0], g[3], g[1], g[2])
        };

        var bestScore = double.MaxValue;
        var best = candidates[0];

        foreach (var c in candidates)
        {
            var penalty = (usedTeams.Contains(TeamKey(c.Item1.Id, c.Item2.Id)) ? 1000d : 0d)
                        + (usedTeams.Contains(TeamKey(c.Item3.Id, c.Item4.Id)) ? 1000d : 0d);
            var levelDiff = Math.Abs((c.Item1.Level + c.Item2.Level) - (c.Item3.Level + c.Item4.Level));
            var avgDiff = Math.Abs(
                (Avg(c.Item1.Id, avg) + Avg(c.Item2.Id, avg)) - (Avg(c.Item3.Id, avg) + Avg(c.Item4.Id, avg)));

            var score = penalty + (levelDiff * 10d) + avgDiff;
            if (score < bestScore)
            {
                bestScore = score;
                best = c;
            }
        }

        return best;
    }

    private static double Avg(int id, IReadOnlyDictionary<int, double> avg)
        => avg.TryGetValue(id, out var v) ? v : DefaultAverage;

    private static string TeamKey(int a, int b)
        => a <= b ? $"{a}|{b}" : $"{b}|{a}";
}
