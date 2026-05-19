namespace Padel.Manager.Models;

public sealed class MatchWriteModel
{
    public required DateTime Date { get; init; }

    public required int RoundNumber { get; init; }

    public required int MatchNumber { get; init; }

    public required int TerrainNumber { get; init; }

    public required string TeamAPlayer1 { get; init; }

    public required string TeamAPlayer2 { get; init; }

    public required string TeamBPlayer1 { get; init; }

    public required string TeamBPlayer2 { get; init; }

    public required int ScoreA { get; init; }

    public required int ScoreB { get; init; }
}
