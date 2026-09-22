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

    public int Between(int inclusiveLow, int inclusiveHigh) => inclusiveLow + Next(inclusiveHigh - inclusiveLow + 1);

    public bool Chance(double probability) => Next() / (double)ulong.MaxValue < probability;
}

/// <summary>
/// Everything the provider will say about one match, worked out before it kicks off.
/// </summary>
public sealed record MatchPlan(
    IReadOnlyList<MatchEventPayload> Events,
    IReadOnlyList<LineupPayload> Lineups,
    IReadOnlyList<StatisticsPayload> Statistics);

/// <summary>
/// The whole match, written before a ball is kicked.
/// </summary>
/// <remarks>
/// Generating the entire match up front rather than at each tick is what makes the provider's ledger
/// and its repair endpoint simple: the events exist before anyone asks for them, so the answer to
/// "what did you send me between 12 and 18" never has to be invented on the spot.
/// <para>
/// Who is on the pitch is tracked as the match is written, so a goal is always scored by somebody who
/// was playing at that minute. A scorer who had already been substituted, or who never came off the
/// bench, would make every rating derived from the log wrong in a way that is obvious to a reader and
/// invisible to a test that only counts goals.
/// </para>
/// </remarks>
public static class MatchScript
{
    private const int FirstHalfMinutes = 45;
    private const int SecondHalfMinutes = 90;
    private const int MaxGoalsPerSide = 4;
    private const int SubstitutionsPerSide = 3;

    /// <summary>Defenders, midfielders and forwards. The goalkeeper is not up for discussion.</summary>
    private static readonly (string Name, int Defenders, int Midfielders, int Forwards)[] Formations =
    [
        ("4-3-3", 4, 3, 3),
        ("4-4-2", 4, 4, 2),
        ("4-2-3-1", 4, 5, 1),
        ("3-5-2", 3, 5, 2),
    ];

    public static MatchPlan For(CatalogueFixture fixture, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var rolls = new Rolls(seed ^ (ulong)fixture.Id.GetHashCode());

        var home = TeamSheet.Pick(fixture.Home, ref rolls);
        var away = TeamSheet.Pick(fixture.Away, ref rolls);

        var moments = new List<Moment>();

        AddGoals(ref rolls, moments, forHome: true);
        AddGoals(ref rolls, moments, forHome: false);
        AddCards(ref rolls, moments);
        AddSubstitutions(ref rolls, moments);

        var firstHalfStoppage = rolls.Next(4);
        var secondHalfStoppage = 1 + rolls.Next(6);
        var finalMinute = SecondHalfMinutes + secondHalfStoppage;

        var timeline = new List<Moment>
        {
            new(0, MatchEventKind.PeriodStart, true),
        };

        timeline.AddRange(moments.Where(m => m.Minute <= FirstHalfMinutes).OrderBy(m => m.Minute));
        timeline.Add(new(FirstHalfMinutes + firstHalfStoppage, MatchEventKind.PeriodEnd, true));
        timeline.Add(new(FirstHalfMinutes + 1, MatchEventKind.PeriodStart, true));
        timeline.AddRange(moments.Where(m => m.Minute > FirstHalfMinutes).OrderBy(m => m.Minute));
        timeline.Add(new(finalMinute, MatchEventKind.PeriodEnd, true));

        var events = new List<MatchEventPayload>(timeline.Count);
        var homeGoals = 0;
        var awayGoals = 0;

        for (var index = 0; index < timeline.Count; index++)
        {
            var moment = timeline[index];
            var sheet = moment.ForHome ? home : away;

            events.Add(ToPayload(fixture, sheet, ref rolls, moment, index + 1));

            if (moment.Kind is MatchEventKind.Goal or MatchEventKind.PenaltyGoal)
            {
                if (moment.ForHome)
                {
                    homeGoals++;
                }
                else
                {
                    awayGoals++;
                }
            }
            else if (moment.Kind == MatchEventKind.OwnGoal)
            {
                // Recorded against the side that put it in, so it counts for the other one.
                if (moment.ForHome)
                {
                    awayGoals++;
                }
                else
                {
                    homeGoals++;
                }
            }
        }

        return new MatchPlan(
            events,
            [home.ToPayload(fixture.Id), away.ToPayload(fixture.Id)],
            Statistics.Build(fixture, ref rolls, homeGoals, awayGoals, finalMinute));
    }

    private static void AddGoals(ref Rolls rolls, List<Moment> moments, bool forHome)
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
                moments.Add(new(minute, MatchEventKind.OwnGoal, !forHome));
                continue;
            }

            moments.Add(new(minute, kind, forHome));
        }
    }

    private static void AddCards(ref Rolls rolls, List<Moment> moments)
    {
        var yellows = 1 + rolls.Next(5);

        for (var card = 0; card < yellows; card++)
        {
            moments.Add(new(10 + rolls.Next(80), MatchEventKind.Yellow, rolls.Chance(0.5)));
        }

        if (rolls.Chance(0.15))
        {
            moments.Add(new(55 + rolls.Next(35), MatchEventKind.Red, rolls.Chance(0.5)));
        }
    }

    private static void AddSubstitutions(ref Rolls rolls, List<Moment> moments)
    {
        foreach (var forHome in (bool[])[true, false])
        {
            for (var change = 0; change < SubstitutionsPerSide; change++)
            {
                moments.Add(new(55 + rolls.Next(35), MatchEventKind.Substitution, forHome));
            }
        }
    }

    private static MatchEventPayload ToPayload(
        CatalogueFixture fixture,
        TeamSheet sheet,
        ref Rolls rolls,
        Moment moment,
        int sequence)
    {
        Guid? playerId = null;
        Guid? assistId = null;
        Guid? replacedId = null;

        switch (moment.Kind)
        {
            case MatchEventKind.PeriodStart:
            case MatchEventKind.PeriodEnd:
                break;

            case MatchEventKind.Substitution:
                (playerId, replacedId) = sheet.Substitute(ref rolls);
                break;

            case MatchEventKind.OwnGoal:
                playerId = sheet.OnPitch(ref rolls, Weights.OwnGoal);
                break;

            case MatchEventKind.Goal:
            case MatchEventKind.PenaltyGoal:
                playerId = sheet.OnPitch(ref rolls, Weights.Goal);

                if (moment.Kind == MatchEventKind.Goal && rolls.Chance(0.6))
                {
                    var assist = sheet.OnPitch(ref rolls, Weights.Assist);
                    assistId = assist == playerId ? null : assist;
                }

                break;

            case MatchEventKind.Red:
                playerId = sheet.SendOff(ref rolls);
                break;

            default:
                playerId = sheet.OnPitch(ref rolls, Weights.Card);
                break;
        }

        return new MatchEventPayload(
            ProviderEventId: $"{fixture.Id:N}-{sequence}",
            MatchId: fixture.Id,
            Sequence: sequence,
            Minute: moment.Minute,
            Kind: moment.Kind,
            TeamId: sheet.Team.Id,
            PlayerId: playerId,
            AssistPlayerId: assistId,
            OccurredAt: fixture.KickoffUtc.AddMinutes(moment.Minute),
            ReplacedPlayerId: replacedId);
    }

    private readonly record struct Moment(int Minute, MatchEventKind Kind, bool ForHome);

    /// <summary>
    /// How likely each kind of player is to be the one an event is about.
    /// </summary>
    /// <remarks>
    /// A goalkeeper scoring from open play happens about once a decade, and a forward being booked
    /// for a foul happens less than a defender is. The weights are not a model of football, only
    /// enough of one that a scorer list and a set of ratings read like a league rather than a
    /// shuffle.
    /// </remarks>
    private static class Weights
    {
        public static readonly int[] Goal = [0, 15, 35, 50];
        public static readonly int[] Assist = [0, 20, 45, 35];
        public static readonly int[] OwnGoal = [10, 60, 30, 0];
        public static readonly int[] Card = [5, 35, 40, 20];
    }

    /// <summary>
    /// One side's team sheet, and who is on the pitch as the match is written.
    /// </summary>
    private sealed class TeamSheet
    {
        private readonly List<CataloguePlayer> _onPitch;

        /// <summary>
        /// Who is left to bring on. Separate from <see cref="Bench"/>, which is the sheet as it was
        /// published: the team sheet a reader sees names everybody who started on the bench, whether
        /// or not they later came on.
        /// </summary>
        private readonly List<CataloguePlayer> _available;

        private TeamSheet(CatalogueTeam team, string formation, List<CataloguePlayer> starters, List<CataloguePlayer> bench)
        {
            Team = team;
            Formation = formation;
            Starters = starters;
            Bench = bench;
            _onPitch = [.. starters];
            _available = [.. bench];
        }

        public CatalogueTeam Team { get; }

        public string Formation { get; }

        public IReadOnlyList<CataloguePlayer> Starters { get; }

        public IReadOnlyList<CataloguePlayer> Bench { get; }

        public static TeamSheet Pick(CatalogueTeam team, ref Rolls rolls)
        {
            var (formation, defenders, midfielders, forwards) = Formations[rolls.Next(Formations.Length)];

            var starters = new List<CataloguePlayer>(11);
            starters.AddRange(Take(team, LineupPosition.Goalkeeper, 1));
            starters.AddRange(Take(team, LineupPosition.Defender, defenders));
            starters.AddRange(Take(team, LineupPosition.Midfielder, midfielders));
            starters.AddRange(Take(team, LineupPosition.Forward, forwards));

            var bench = team.Players.Where(player => !starters.Contains(player)).ToList();

            return new TeamSheet(team, formation, starters, bench);
        }

        public LineupPayload ToPayload(Guid matchId) => new(
            matchId,
            Team.Id,
            Formation,
            [
                .. Starters.Select(player => new LineupPlayerPayload(player.Id, player.ShirtNumber, player.Position, true)),
                .. Bench.Select(player => new LineupPlayerPayload(player.Id, player.ShirtNumber, player.Position, false)),
            ]);

        /// <summary>Somebody who is playing right now, chosen by what the event is.</summary>
        public Guid OnPitch(ref Rolls rolls, int[] weights)
        {
            var total = _onPitch.Sum(player => weights[(int)player.Position]);

            if (total == 0)
            {
                return _onPitch[rolls.Next(_onPitch.Count)].Id;
            }

            var target = rolls.Next(total);

            foreach (var player in _onPitch)
            {
                target -= weights[(int)player.Position];

                if (target < 0)
                {
                    return player.Id;
                }
            }

            return _onPitch[^1].Id;
        }

        /// <summary>A substitute comes on, an outfield player goes off, and the pitch changes.</summary>
        public (Guid? OnId, Guid? OffId) Substitute(ref Rolls rolls)
        {
            var leaving = _onPitch.Where(player => player.Position != LineupPosition.Goalkeeper).ToList();

            if (_available.Count == 0 || leaving.Count == 0)
            {
                // A provider that sends a fourth substitution with an empty bench is a provider with a
                // problem. This one simply does not.
                return (null, null);
            }

            var off = leaving[rolls.Next(leaving.Count)];
            var on = _available[rolls.Next(_available.Count)];

            _available.Remove(on);
            _onPitch.Remove(off);
            _onPitch.Add(on);

            return (on.Id, off.Id);
        }

        /// <summary>A dismissal takes a player off for good, and nobody replaces them.</summary>
        public Guid? SendOff(ref Rolls rolls)
        {
            if (_onPitch.Count <= 7)
            {
                // Seven is where a referee abandons a match. The simulator stops short of it.
                return null;
            }

            var dismissed = _onPitch[rolls.Next(_onPitch.Count)];
            _onPitch.Remove(dismissed);

            return dismissed.Id;
        }

        private static IEnumerable<CataloguePlayer> Take(CatalogueTeam team, LineupPosition position, int count) =>
            team.Players.Where(player => player.Position == position).Take(count);
    }

    /// <summary>
    /// The numbers a match page shows next to the score.
    /// </summary>
    /// <remarks>
    /// Built backwards from the result, so they agree with it: a side that scored three cannot have
    /// had two shots on target. The snapshots along the way are the same totals at their share of the
    /// match, which is what a provider sending cumulative statistics actually produces.
    /// </remarks>
    private static class Statistics
    {
        private static readonly int[] SnapshotMinutes = [15, 30, 45, 60, 75, 90];

        public static List<StatisticsPayload> Build(
            CatalogueFixture fixture,
            ref Rolls rolls,
            int homeGoals,
            int awayGoals,
            int finalMinute)
        {
            var homePossession = rolls.Between(38, 62);

            var home = FinalFor(ref rolls, homeGoals, homePossession);
            var away = FinalFor(ref rolls, awayGoals, 100 - homePossession);

            var snapshots = new List<StatisticsPayload>();

            foreach (var minute in SnapshotMinutes)
            {
                var share = Math.Min(1d, minute / (double)finalMinute);
                var asOf = Math.Min(minute, finalMinute);

                snapshots.Add(At(fixture.Id, fixture.Home.Id, home, share, asOf));
                snapshots.Add(At(fixture.Id, fixture.Away.Id, away, share, asOf));
            }

            // The last pair is the final total rather than a share of it, so the numbers a reader sees
            // after full time are the ones the match actually finished with.
            snapshots.Add(At(fixture.Id, fixture.Home.Id, home, 1d, finalMinute));
            snapshots.Add(At(fixture.Id, fixture.Away.Id, away, 1d, finalMinute));

            return snapshots;
        }

        private static Totals FinalFor(ref Rolls rolls, int goals, int possession)
        {
            var onTarget = goals + rolls.Between(1, 5);

            return new Totals(
                possession,
                onTarget + rolls.Between(2, 9),
                onTarget,
                rolls.Between(1, 11),
                rolls.Between(6, 18),
                rolls.Between(0, 5));
        }

        private static StatisticsPayload At(Guid matchId, Guid teamId, Totals totals, double share, int minute) => new(
            matchId,
            teamId,
            minute,
            totals.Possession,
            Part(totals.Shots, share),
            Part(totals.ShotsOnTarget, share),
            Part(totals.Corners, share),
            Part(totals.Fouls, share),
            Part(totals.Offsides, share));

        private static int Part(int total, double share) => (int)Math.Round(total * share, MidpointRounding.AwayFromZero);

        private readonly record struct Totals(int Possession, int Shots, int ShotsOnTarget, int Corners, int Fouls, int Offsides);
    }
}
