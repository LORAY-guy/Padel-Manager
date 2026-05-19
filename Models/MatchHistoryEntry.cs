namespace Padel.Manager.Models;

public sealed class MatchHistoryEntry
{
    private string? _equipeA;
    private string? _equipeB;

    public required DateTime Date { get; init; }

    public required int MatchNumber { get; init; }

    public required string TeamAPlayer1 { get; init; }

    public required string TeamAPlayer2 { get; init; }

    public required string TeamBPlayer1 { get; init; }

    public required string TeamBPlayer2 { get; init; }

    public required int ScoreA { get; init; }

    public required int ScoreB { get; init; }

    public required int TeamAPoints { get; init; }

    public required int TeamBPoints { get; init; }

    public string EquipeA => _equipeA ??= $"{TeamAPlayer1} & {TeamAPlayer2}";

    public string EquipeB => _equipeB ??= $"{TeamBPlayer1} & {TeamBPlayer2}";
}
