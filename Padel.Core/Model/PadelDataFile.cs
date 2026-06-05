namespace Padel.Core.Model;

/// <summary>
/// Canonical, serializable representation of a Padel dataset. This is the exact
/// JSON shape persisted to local <c>.padel</c> files today and the same payload
/// stored/synced by the server. Property names must stay byte-for-byte stable so
/// existing files keep deserializing — do not add naming policies or rename.
/// </summary>
public sealed class PadelDataFile
{
    public int Version { get; set; } = 1;

    public int NextPlayerId { get; set; } = 1;

    public int LeaderboardPlayersPerSheet { get; set; } = 20;

    public bool AmericanoMode { get; set; } = false;

    public List<PlayerEntry> Players { get; set; } = new();

    public List<ScoreEntry> ScoreEntries { get; set; } = new();

    public List<PlannedEntry> PlannedEntries { get; set; } = new();

    public List<TournamentEntry> TournamentEntries { get; set; } = new();
}

public sealed class PlayerEntry
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Level { get; set; }
}

public sealed class ScoreEntry
{
    public DateTime Date { get; set; }

    public int MatchNumber { get; set; }

    public int RoundNumber { get; set; }

    public int TerrainNumber { get; set; }

    public int TeamAPlayer1Id { get; set; }

    public int TeamAPlayer2Id { get; set; }

    public int TeamBPlayer1Id { get; set; }

    public int TeamBPlayer2Id { get; set; }

    public int ScoreA { get; set; }

    public int ScoreB { get; set; }

    public int TeamAPoints { get; set; }

    public int TeamBPoints { get; set; }
}

public sealed class PlannedEntry
{
    public DateTime Date { get; set; }

    public int RoundNumber { get; set; }

    public int TerrainNumber { get; set; }

    public int TeamAPlayer1Id { get; set; }

    public int TeamAPlayer2Id { get; set; }

    public int TeamBPlayer1Id { get; set; }

    public int TeamBPlayer2Id { get; set; }

    public int? ScoreA { get; set; }

    public int? ScoreB { get; set; }
}

public sealed class TournamentEntry
{
    public DateTime TournamentDate { get; set; }

    public DateTime MatchDate { get; set; }

    public int RoundNumber { get; set; }

    public int MatchNumber { get; set; }

    public string TeamA { get; set; } = string.Empty;

    public string TeamB { get; set; } = string.Empty;

    public string? SourceTeamA { get; set; }

    public string? SourceTeamB { get; set; }

    public int? ScoreA { get; set; }

    public int? ScoreB { get; set; }
}
