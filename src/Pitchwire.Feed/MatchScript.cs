using Pitchwire.Contracts;

namespace Pitchwire.Feed;

/// <summary>
/// A small deterministic generator, written out rather than taken from the framework.
/// </summary>
/// <remarks>
/// <see cref="Random"/> with a seed is reproducible within one runtime version, and the runtime is
/// free to change the algorithm between versions. A simulator whose match cannot be replayed a year
/// later is a simulator that cannot be debugged, so the algorithm lives here where it cannot move.
/// </remarks>
public struct Rolls(ulong seed)
{
    private ulong _state = seed == 0 ? 0x9E3779B97F4A7C15 : seed;

    /// <summary>xorshift64star, from Vigna's paper. Fast, tiny, and good enough to place goals.</summary>
    public ulong Next()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 0x2545F4914F6CDD1D;
    }

    public int Next(int exclusiveBound) => (int)(Next() % (ulong)exclusiveBound);

    public bool Chance(double probability) => Next() / (double)ulong.MaxValue < probability;
}

/// <summary>
/// The whole match, written before a ball is kicked.
/// </summary>
/// <remarks>
/// Generating the entire event list up front rather than at each tick is what makes the provider's
/// ledger and its repair endpoint simple: the events exist before anyone asks for them, so the
/// answer to "what did you send me between 12 and 18" never has to be invented on the spot.
/// </remarks>
public static class MatchScript
{
    private const int FirstHalfMinutes = 45;
    private const int SecondHalfMinutes = 90;
    private const int MaxGoalsPerSide = 4;

    public static IReadOnlyList<MatchEventPayload> For(CatalogueFixture fixture, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var rolls = new Rolls(seed ^ (ulong)fixture.Id.GetHashCode());
        var moments = new List<(int Minute, MatchEventKind Kind, bool ForHome)>();

        AddGoals(ref rolls, moments, forHome: true);
        AddGoals(ref rolls, moments, forHome: false);
        AddCards(ref rolls, moments);
        AddSubstitutions(ref rolls, moments);

        var firstHalfStoppage = rolls.Next(4);
        var secondHalfStoppage = 1 + rolls.Next(6);

        var timeline = new List<(int Minute, MatchEventKind Kind, bool ForHome)>
        {
            (0, MatchEventKind.PeriodStart, true),
        };

        timeline.AddRange(moments.Where(m => m.Minute <= FirstHalfMinutes).OrderBy(m => m.Minute));
        timeline.Add((FirstHalfMinutes + firstHalfStoppage, MatchEventKind.PeriodEnd, true));
        timeline.Add((FirstHalfMinutes + 1, MatchEventKind.PeriodStart, true));
        timeline.AddRange(moments.Where(m => m.Minute > FirstHalfMinutes).OrderBy(m => m.Minute));
        timeline.Add((SecondHalfMinutes + secondHalfStoppage, MatchEventKind.PeriodEnd, true));

        return [.. timeline.Select((moment, index) => ToPayload(fixture, ref rolls, moment, index + 1))];
    }

    private static void AddGoals(ref Rolls rolls, List<(int, MatchEventKind, bool)> moments, bool forHome)
    {
        // Weighted towards the low numbers a real scoreline has. Four is the ceiling because a 7-6
        // every other match would make the demo look like a random number generator, which it is.
        var goals = rolls.Next(10) switch
        {
            < 3 => 0,
            < 6 => 1,
            < 8 => 2,
            < 9 => 3,
            _ => MaxGoalsPerSide,
        };

        for (var scored = 0; scored < goals; scored++)
        {
            var minute = 1 + rolls.Next(SecondHalfMinutes);
            var kind = rolls.Chance(0.12) ? MatchEventKind.PenaltyGoal : MatchEventKind.Goal;

            // An own goal is recorded against the side that conceded it, which is how a provider sends
            // it, so the consumer has to be the one to credit the other team.
            if (rolls.Chance(0.05))
            {
                moments.Add((minute, MatchEventKind.OwnGoal, !forHome));
                continue;
            }

            moments.Add((minute, kind, forHome));
        }
    }

    private static void AddCards(ref Rolls rolls, List<(int, MatchEventKind, bool)> moments)
    {
        var yellows = 1 + rolls.Next(5);
        for (var card = 0; card < yellows; card++)
        {
            moments.Add((10 + rolls.Next(80), MatchEventKind.Yellow, rolls.Chance(0.5)));
        }

        if (rolls.Chance(0.15))
        {
            moments.Add((55 + rolls.Next(35), MatchEventKind.Red, rolls.Chance(0.5)));
        }
    }

    private static void AddSubstitutions(ref Rolls rolls, List<(int, MatchEventKind, bool)> moments)
    {
        foreach (var forHome in (bool[])[true, false])
        {
            var substitutions = 2 + rolls.Next(2);
            for (var change = 0; change < substitutions; change++)
            {
                moments.Add((60 + rolls.Next(30), MatchEventKind.Substitution, forHome));
            }
        }
    }

    private static MatchEventPayload ToPayload(
        CatalogueFixture fixture,
        ref Rolls rolls,
        (int Minute, MatchEventKind Kind, bool ForHome) moment,
        int sequence)
    {
        var team = moment.ForHome ? fixture.Home : fixture.Away;
        var scorer = team.Players[rolls.Next(team.Players.Count)];

        Guid? playerId = moment.Kind is MatchEventKind.PeriodStart or MatchEventKind.PeriodEnd
            ? null
            : scorer.Id;

        Guid? assistId = moment.Kind is MatchEventKind.Goal && rolls.Chance(0.6)
            ? team.Players[rolls.Next(team.Players.Count)].Id
            : null;

        return new MatchEventPayload(
            ProviderEventId: $"{fixture.Id:N}-{sequence}",
            MatchId: fixture.Id,
            Sequence: sequence,
            Minute: moment.Minute,
            Kind: moment.Kind,
            TeamId: team.Id,
            PlayerId: playerId,
            AssistPlayerId: assistId == playerId ? null : assistId,
            OccurredAt: fixture.KickoffUtc.AddMinutes(moment.Minute));
    }
}
