using Padel.Manager.Models;

namespace Padel.Manager.Services;

public static class MatchGenerationService
{
    public static List<UiMatch> BuildBalancedMatches(
        IReadOnlyList<PlayerProfile> selectedPlayers,
        IReadOnlyDictionary<string, double> averagePoints,
        int rounds,
        Random random)
    {
        if (selectedPlayers.Count < 4)
        {
            return new List<UiMatch>();
        }

        var output = new List<UiMatch>();
        var usedTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedBase = selectedPlayers
            .OrderByDescending(p => p.Level)
            .ThenByDescending(p => GetAverage(p.Name, averagePoints))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var round = 1; round <= rounds; round++)
        {
            var roundPlayers = RotatePlayers(orderedBase, round - 1, random);
            for (var i = 0; i < roundPlayers.Count; i += 4)
            {
                var group = roundPlayers.Skip(i).Take(4).ToList();
                if (group.Count < 4)
                {
                    continue;
                }

                var best = ChooseBestPairing(group, averagePoints, usedTeams);
                usedTeams.Add(TeamKey(best.A1.Name, best.A2.Name));
                usedTeams.Add(TeamKey(best.B1.Name, best.B2.Name));

                output.Add(new UiMatch
                {
                    RoundNumber = round,
                    MatchNumber = (i / 4) + 1,
                    TerrainNumber = (i / 4) + 1,
                    TeamAPlayer1 = best.A1.Name,
                    TeamAPlayer2 = best.A2.Name,
                    TeamBPlayer1 = best.B1.Name,
                    TeamBPlayer2 = best.B2.Name
                });
            }
        }

        return output;
    }

    private static List<PlayerProfile> RotatePlayers(List<PlayerProfile> players, int rotation, Random random)
    {
        if (rotation == 0)
        {
            return players.ToList();
        }

        var copy = players.ToList();
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

    private static (PlayerProfile A1, PlayerProfile A2, PlayerProfile B1, PlayerProfile B2) ChooseBestPairing(
        List<PlayerProfile> players,
        IReadOnlyDictionary<string, double> averagePoints,
        HashSet<string> usedTeams)
    {
        var p1 = players[0];
        var p2 = players[1];
        var p3 = players[2];
        var p4 = players[3];

        var candidates = new List<(PlayerProfile A1, PlayerProfile A2, PlayerProfile B1, PlayerProfile B2)>
        {
            (p1, p2, p3, p4),
            (p1, p3, p2, p4),
            (p1, p4, p2, p3)
        };

        var bestScore = double.MaxValue;
        (PlayerProfile A1, PlayerProfile A2, PlayerProfile B1, PlayerProfile B2) best = candidates[0];

        foreach (var candidate in candidates)
        {
            var teamAKey = TeamKey(candidate.A1.Name, candidate.A2.Name);
            var teamBKey = TeamKey(candidate.B1.Name, candidate.B2.Name);
            var repetitionPenalty = (usedTeams.Contains(teamAKey) ? 1000d : 0d) + (usedTeams.Contains(teamBKey) ? 1000d : 0d);

            var teamALevel = candidate.A1.Level + candidate.A2.Level;
            var teamBLevel = candidate.B1.Level + candidate.B2.Level;

            var teamAAvg = GetAverage(candidate.A1.Name, averagePoints) + GetAverage(candidate.A2.Name, averagePoints);
            var teamBAvg = GetAverage(candidate.B1.Name, averagePoints) + GetAverage(candidate.B2.Name, averagePoints);

            var levelDiff = Math.Abs(teamALevel - teamBLevel);
            var avgDiff = Math.Abs(teamAAvg - teamBAvg);

            var score = repetitionPenalty + (levelDiff * 10d) + avgDiff;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static double GetAverage(string playerName, IReadOnlyDictionary<string, double> averagePoints)
    {
        return averagePoints.TryGetValue(playerName, out var avg) ? avg : 2d;
    }

    private static string TeamKey(string player1, string player2)
    {
        return string.Compare(player1, player2, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{player1}|{player2}"
            : $"{player2}|{player1}";
    }
}
