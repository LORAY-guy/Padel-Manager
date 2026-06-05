using Padel.Core.Model;

namespace Padel.Core.Editing;

/// <summary>
/// Pure mutations on a <see cref="PadelDataFile"/>. Kept in Core so the web app
/// records matches with the exact same point rules as the desktop app.
/// </summary>
public static class PadelEditor
{
    /// <summary>Padel points: win = 3, loss = 1, draw = 2 each.</summary>
    public static (int TeamA, int TeamB) ComputePoints(int scoreA, int scoreB)
    {
        if (scoreA > scoreB) return (3, 1);
        if (scoreA < scoreB) return (1, 3);
        return (2, 2);
    }

    public static int NextMatchNumber(PadelDataFile data, DateTime date)
    {
        var target = date.Date;
        return data.ScoreEntries
            .Where(s => s.Date.Date == target)
            .Select(s => s.MatchNumber)
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    /// <summary>Adds a player. Returns false if the name is blank or already exists.</summary>
    public static bool AddPlayer(PadelDataFile data, string name, int level)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (data.Players.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var nextId = Math.Max(data.NextPlayerId, (data.Players.Count == 0 ? 0 : data.Players.Max(p => p.Id)) + 1);
        data.Players.Add(new PlayerEntry { Id = nextId, Name = name, Level = level });
        data.NextPlayerId = nextId + 1;
        return true;
    }

    public static bool SetPlayerLevel(PadelDataFile data, int playerId, int level)
    {
        var player = data.Players.FirstOrDefault(p => p.Id == playerId);
        if (player is null)
        {
            return false;
        }

        player.Level = level;
        return true;
    }

    /// <summary>Removes the match(es) identified by date + match number. Returns the count removed.</summary>
    public static int DeleteScore(PadelDataFile data, DateTime date, int matchNumber)
        => data.ScoreEntries.RemoveAll(s => s.Date.Date == date.Date && s.MatchNumber == matchNumber);

    /// <summary>Updates a match's scores (and recomputes points). Returns false if not found.</summary>
    public static bool UpdateScore(PadelDataFile data, DateTime date, int matchNumber, int scoreA, int scoreB)
    {
        var entries = data.ScoreEntries
            .Where(s => s.Date.Date == date.Date && s.MatchNumber == matchNumber)
            .ToList();
        if (entries.Count == 0)
        {
            return false;
        }

        var points = ComputePoints(scoreA, scoreB);
        foreach (var entry in entries)
        {
            entry.ScoreA = scoreA;
            entry.ScoreB = scoreB;
            entry.TeamAPoints = points.TeamA;
            entry.TeamBPoints = points.TeamB;
        }

        return true;
    }

    /// <summary>Replaces the planned (unplayed) matches for a date with the given schedule.</summary>
    public static void ReplacePlanned(
        PadelDataFile data, DateTime date,
        IEnumerable<(int Round, int Terrain, int A1, int A2, int B1, int B2)> matches)
    {
        var d = date.Date;
        data.PlannedEntries.RemoveAll(p => p.Date.Date == d);
        foreach (var m in matches)
        {
            data.PlannedEntries.Add(new PlannedEntry
            {
                Date = d,
                RoundNumber = m.Round,
                TerrainNumber = m.Terrain,
                TeamAPlayer1Id = m.A1,
                TeamAPlayer2Id = m.A2,
                TeamBPlayer1Id = m.B1,
                TeamBPlayer2Id = m.B2
            });
        }
    }

    /// <summary>
    /// Records the score of a planned match: appends a played match and removes the
    /// planned entry (it has now been played). Returns false if not found.
    /// </summary>
    public static bool RecordPlannedScore(PadelDataFile data, DateTime date, int round, int terrain, int scoreA, int scoreB)
    {
        var d = date.Date;
        var planned = data.PlannedEntries.FirstOrDefault(
            p => p.Date.Date == d && p.RoundNumber == round && p.TerrainNumber == terrain);
        if (planned is null)
        {
            return false;
        }

        AddScore(data, d, planned.TeamAPlayer1Id, planned.TeamAPlayer2Id, planned.TeamBPlayer1Id, planned.TeamBPlayer2Id, scoreA, scoreB);
        data.PlannedEntries.Remove(planned);
        return true;
    }

    /// <summary>Appends a played match (auto-numbered for its date) and returns it.</summary>
    public static ScoreEntry AddScore(
        PadelDataFile data,
        DateTime date,
        int teamAPlayer1Id, int teamAPlayer2Id,
        int teamBPlayer1Id, int teamBPlayer2Id,
        int scoreA, int scoreB)
    {
        var points = ComputePoints(scoreA, scoreB);
        var entry = new ScoreEntry
        {
            Date = date.Date,
            MatchNumber = NextMatchNumber(data, date),
            RoundNumber = 0,
            TerrainNumber = 0,
            TeamAPlayer1Id = teamAPlayer1Id,
            TeamAPlayer2Id = teamAPlayer2Id,
            TeamBPlayer1Id = teamBPlayer1Id,
            TeamBPlayer2Id = teamBPlayer2Id,
            ScoreA = scoreA,
            ScoreB = scoreB,
            TeamAPoints = points.TeamA,
            TeamBPoints = points.TeamB
        };
        data.ScoreEntries.Add(entry);
        return entry;
    }
}
