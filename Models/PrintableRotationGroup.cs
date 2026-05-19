namespace Padel.Manager.Models;

public sealed class PrintableRotationGroup
{
    public required int RoundNumber { get; init; }

    public required List<UiMatch> Matches { get; init; }
}
