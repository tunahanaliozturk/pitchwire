namespace Pitchwire.Domain;

/// <summary>
/// The things that happen in a match, as this system understands them.
/// </summary>
/// <remarks>
/// Deliberately a separate type from the one on the wire. A provider's vocabulary is its own, and the
/// day one of them sends a kind nobody here has heard of, the translation has somewhere to refuse it
/// rather than letting an unknown value reach a reducer that has no case for it.
/// </remarks>
public enum MatchEventKind
{
    Goal,
    OwnGoal,
    PenaltyGoal,
    Yellow,
    Red,
    Substitution,
    PeriodStart,
    PeriodEnd,
}
