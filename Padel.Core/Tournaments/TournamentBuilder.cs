using Padel.Core.Model;

namespace Padel.Core.Tournaments;

/// <summary>
/// Bracket generation + progression, ported from the desktop tournament view. A
/// bracket is a flat list of <see cref="TournamentEntry"/> whose later matches
/// reference earlier ones through "Vainqueur/Perdant Qx-My" source tokens;
/// <see cref="Propagate"/> resolves those into team names as scores are filled in.
/// </summary>
public static class TournamentBuilder
{
    /// <summary>Builds a bracket for the given fixed first-round teams.</summary>
    public static List<TournamentEntry> Generate(IReadOnlyList<string> teams, DateTime date)
    {
        var entries = new List<TournamentEntry>();
        var d = date.Date;

        // Round 1: pair the fixed teams in order.
        var m1 = 1;
        for (var i = 0; i + 1 < teams.Count; i += 2)
        {
            entries.Add(new TournamentEntry
            {
                TournamentDate = d, MatchDate = d, RoundNumber = 1, MatchNumber = m1,
                TeamA = teams[i], TeamB = teams[i + 1]
            });
            m1++;
        }

        var firstRoundCount = entries.Count;
        var winnerSeeds = Enumerable.Range(1, firstRoundCount).Select(i => $"Vainqueur Q1-M{i}").ToList();
        var loserSeeds = Enumerable.Range(1, firstRoundCount).Select(i => $"Perdant Q1-M{i}").ToList();

        var round2Qualified = new List<string>();
        var m2 = 1;

        var winnerPairs = BuildPairings(winnerSeeds, out var winnerCarry);
        foreach (var pair in winnerPairs)
        {
            entries.Add(Pending(d, 2, m2, pair.A, pair.B));
            round2Qualified.Add($"Vainqueur Q2-M{m2}");
            m2++;
        }
        round2Qualified.AddRange(winnerCarry);

        var loserPairs = BuildPairings(loserSeeds, out var loserCarry);
        foreach (var pair in loserPairs)
        {
            entries.Add(Pending(d, 2, m2, pair.A, pair.B));
            round2Qualified.Add($"Vainqueur Q2-M{m2}");
            m2++;
        }
        round2Qualified.AddRange(loserCarry);

        var round = 3;
        var current = round2Qualified;
        while (current.Count > 1)
        {
            var next = new List<string>();
            var m = 1;
            var pairs = BuildPairings(current, out var carry);
            foreach (var pair in pairs)
            {
                entries.Add(Pending(d, round, m, pair.A, pair.B));
                next.Add($"Vainqueur R{round}-M{m}");
                m++;
            }
            next.AddRange(carry);
            current = next;
            round++;
        }

        Propagate(entries);
        return entries;
    }

    /// <summary>Resolves source tokens into team names based on entered scores (mutates in place).</summary>
    public static void Propagate(List<TournamentEntry> entries)
    {
        var tokenToTeam = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in entries.OrderBy(e => e.RoundNumber).ThenBy(e => e.MatchNumber))
        {
            if (string.IsNullOrWhiteSpace(m.SourceTeamA) && IsToken(m.TeamA)) m.SourceTeamA = m.TeamA;
            if (string.IsNullOrWhiteSpace(m.SourceTeamB) && IsToken(m.TeamB)) m.SourceTeamB = m.TeamB;

            if (!string.IsNullOrWhiteSpace(m.SourceTeamA)) m.TeamA = Resolve(m.SourceTeamA!, tokenToTeam);
            if (!string.IsNullOrWhiteSpace(m.SourceTeamB)) m.TeamB = Resolve(m.SourceTeamB!, tokenToTeam);

            if (!TryWinnerLoser(m, out var winner, out var loser))
            {
                RemoveTokens(tokenToTeam, m.RoundNumber, m.MatchNumber);
                continue;
            }

            tokenToTeam[$"Vainqueur Q{m.RoundNumber}-M{m.MatchNumber}"] = winner;
            tokenToTeam[$"Perdant Q{m.RoundNumber}-M{m.MatchNumber}"] = loser;
            tokenToTeam[$"Vainqueur R{m.RoundNumber}-M{m.MatchNumber}"] = winner;
            tokenToTeam[$"Perdant R{m.RoundNumber}-M{m.MatchNumber}"] = loser;
        }
    }

    /// <summary>The tournament champion, if the final match has a result; otherwise null.</summary>
    public static string? Champion(List<TournamentEntry> entries)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        var final = entries.OrderBy(e => e.RoundNumber).ThenBy(e => e.MatchNumber).Last();
        return TryWinnerLoser(final, out var winner, out _) ? winner : null;
    }

    private static TournamentEntry Pending(DateTime d, int round, int match, string sourceA, string sourceB) => new()
    {
        TournamentDate = d, MatchDate = d, RoundNumber = round, MatchNumber = match,
        TeamA = string.Empty, TeamB = string.Empty, SourceTeamA = sourceA, SourceTeamB = sourceB
    };

    private static List<(string A, string B)> BuildPairings(IReadOnlyList<string> seeds, out List<string> carried)
    {
        var pairs = new List<(string A, string B)>();
        carried = new List<string>();
        for (var i = 0; i < seeds.Count; i += 2)
        {
            if (i + 1 >= seeds.Count)
            {
                carried.Add(seeds[i]);
                continue;
            }
            pairs.Add((seeds[i], seeds[i + 1]));
        }
        return pairs;
    }

    private static bool IsToken(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && (value!.StartsWith("Vainqueur ", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("Perdant ", StringComparison.OrdinalIgnoreCase));

    private static string Resolve(string tokenOrTeam, IReadOnlyDictionary<string, string> map)
    {
        if (map.TryGetValue(tokenOrTeam, out var resolved))
        {
            return resolved;
        }
        return IsToken(tokenOrTeam) ? string.Empty : tokenOrTeam;
    }

    private static bool TryWinnerLoser(TournamentEntry m, out string winner, out string loser)
    {
        winner = loser = string.Empty;
        if (string.IsNullOrWhiteSpace(m.TeamA) || string.IsNullOrWhiteSpace(m.TeamB))
        {
            return false;
        }
        if (m.ScoreA is not int a || m.ScoreB is not int b || a == b)
        {
            return false;
        }
        (winner, loser) = a > b ? (m.TeamA, m.TeamB) : (m.TeamB, m.TeamA);
        return true;
    }

    private static void RemoveTokens(IDictionary<string, string> map, int round, int match)
    {
        map.Remove($"Vainqueur Q{round}-M{match}");
        map.Remove($"Perdant Q{round}-M{match}");
        map.Remove($"Vainqueur R{round}-M{match}");
        map.Remove($"Perdant R{round}-M{match}");
    }
}
