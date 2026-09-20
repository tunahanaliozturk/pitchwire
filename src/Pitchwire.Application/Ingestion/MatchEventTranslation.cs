using Pitchwire.Domain;
using Wire = Pitchwire.Contracts;

namespace Pitchwire.Application.Ingestion;

/// <summary>
/// Turns what a provider said into what this system understands.
/// </summary>
/// <remarks>
/// The wire enum and the domain enum happen to line up today. They are still translated rather than
/// shared, because the moment a provider adds a kind, or renames one, the change belongs here instead
/// of in the rules of the game.
/// </remarks>
public static class MatchEventTranslation
{
    public static bool TryToDomain(Wire.MatchEventKind wire, out MatchEventKind kind)
    {
        switch (wire)
        {
            case Wire.MatchEventKind.Goal: kind = MatchEventKind.Goal; return true;
            case Wire.MatchEventKind.OwnGoal: kind = MatchEventKind.OwnGoal; return true;
            case Wire.MatchEventKind.PenaltyGoal: kind = MatchEventKind.PenaltyGoal; return true;
            case Wire.MatchEventKind.Yellow: kind = MatchEventKind.Yellow; return true;
            case Wire.MatchEventKind.Red: kind = MatchEventKind.Red; return true;
            case Wire.MatchEventKind.Substitution: kind = MatchEventKind.Substitution; return true;
            case Wire.MatchEventKind.PeriodStart: kind = MatchEventKind.PeriodStart; return true;
            case Wire.MatchEventKind.PeriodEnd: kind = MatchEventKind.PeriodEnd; return true;
            default:
                // A value outside the enum, which a provider can send simply by inventing one.
                kind = default;
                return false;
        }
    }
}
