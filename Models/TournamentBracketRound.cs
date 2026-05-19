namespace Padel.Manager.Models;

public sealed class TournamentBracketRound
{
    public required int RoundNumber { get; init; }

    public required List<TournamentMatch> Matches { get; init; }
}
