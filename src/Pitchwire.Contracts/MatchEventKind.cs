namespace Pitchwire.Contracts;

/// <summary>
/// The event types a provider reports during a match.
/// </summary>
/// <remarks>
/// A penalty goal is a separate kind rather than a flag on a goal, because the read model shows it
/// differently and a flag nobody reads is a field that drifts out of date.
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
