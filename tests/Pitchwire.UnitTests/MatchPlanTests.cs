using Pitchwire.Contracts;
using Pitchwire.Feed;

namespace Pitchwire.UnitTests;

/// <summary>
/// The team sheets and statistics the provider publishes alongside the events.
/// </summary>
/// <remarks>
/// A rating is derived from the log, so the log has to describe a match that could have happened. A
/// goal scored by somebody who had already been substituted, or a side with three goals and two shots
/// on target, is invisible to a test that only counts goals and obvious to anybody reading the page.
/// </remarks>
public sealed class MatchPlanTests
{
    private static readonly CatalogueFixture Fixture = Catalogue.Fixtures[0];

    [Fact]
    public void Both_sides_name_eleven_starters_and_a_bench()
    {
        var plan = MatchScript.For(Fixture, seed: 31);

        plan.Lineups.Count.ShouldBe(2);

        foreach (var lineup in plan.Lineups)
        {
            lineup.Players.Count(player => player.Starter).ShouldBe(11);
            lineup.Players.Count(player => !player.Starter).ShouldBeGreaterThan(0);
            lineup.Players.Select(player => player.PlayerId).Distinct().Count().ShouldBe(lineup.Players.Count);
            lineup.Formation.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void A_starting_eleven_has_exactly_one_goalkeeper()
    {
        var plan = MatchScript.For(Fixture, seed: 37);

        foreach (var lineup in plan.Lineups)
        {
            lineup.Players
                .Count(player => player.Starter && player.Position == LineupPosition.Goalkeeper)
                .ShouldBe(1);
        }
    }

    [Fact]
    public void Every_goal_is_scored_by_somebody_who_was_on_the_pitch()
    {
        // The guarantee the whole rating rests on. Checked against the same events a consumer sees,
        // by replaying the substitutions and dismissals the way the consumer would.
        foreach (var seed in (ulong[])[3, 11, 42, 99, 1337])
        {
            var plan = MatchScript.For(Fixture, seed);
            var onPitch = OnPitchBySide(plan);

            foreach (var scored in plan.Events.Where(IsGoal))
            {
                Apply(plan, onPitch, scored);

                scored.PlayerId.ShouldNotBeNull($"a goal in seed {seed} named nobody");
                onPitch[scored.TeamId].ShouldContain(
                    scored.PlayerId!.Value,
                    $"seed {seed}, minute {scored.Minute}: the scorer was not playing");
            }
        }
    }

    [Fact]
    public void A_substitution_brings_on_somebody_from_the_bench_for_somebody_who_was_playing()
    {
        var plan = MatchScript.For(Fixture, seed: 77);
        var onPitch = OnPitchBySide(plan);
        var bench = plan.Lineups.ToDictionary(
            lineup => lineup.TeamId,
            lineup => lineup.Players.Where(player => !player.Starter).Select(player => player.PlayerId).ToHashSet());

        var seen = 0;

        foreach (var change in plan.Events.Where(e => e.Kind == MatchEventKind.Substitution))
        {
            if (change.PlayerId is null)
            {
                // An empty bench. The simulator stops rather than inventing a fourth substitute.
                continue;
            }

            bench[change.TeamId].ShouldContain(change.PlayerId.Value);
            change.ReplacedPlayerId.ShouldNotBeNull();
            onPitch[change.TeamId].ShouldContain(change.ReplacedPlayerId!.Value);

            Apply(plan, onPitch, change);
            bench[change.TeamId].Remove(change.PlayerId.Value);
            seen++;
        }

        seen.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_side_never_has_more_than_eleven_on_the_pitch()
    {
        var plan = MatchScript.For(Fixture, seed: 51);
        var onPitch = OnPitchBySide(plan);

        foreach (var @event in plan.Events)
        {
            Apply(plan, onPitch, @event);

            foreach (var side in onPitch.Values)
            {
                side.Count.ShouldBeLessThanOrEqualTo(11);
            }
        }
    }

    [Fact]
    public void Possession_adds_up_to_a_hundred()
    {
        var plan = MatchScript.For(Fixture, seed: 64);

        foreach (var pair in plan.Statistics.GroupBy(s => s.AsOfMinute))
        {
            pair.Sum(snapshot => snapshot.Possession).ShouldBe(100);
        }
    }

    [Fact]
    public void A_side_never_has_fewer_shots_on_target_than_goals()
    {
        // Two shots on target and three goals is the kind of number a reader spots immediately.
        foreach (var seed in (ulong[])[5, 23, 61, 404])
        {
            var plan = MatchScript.For(Fixture, seed);
            var final = plan.Statistics.GroupBy(s => s.TeamId).ToDictionary(g => g.Key, g => g.MaxBy(s => s.AsOfMinute)!);

            foreach (var side in final)
            {
                var goals = plan.Events.Count(e =>
                    e.TeamId == side.Key && e.Kind is MatchEventKind.Goal or MatchEventKind.PenaltyGoal);

                side.Value.ShotsOnTarget.ShouldBeGreaterThanOrEqualTo(goals);
                side.Value.Shots.ShouldBeGreaterThanOrEqualTo(side.Value.ShotsOnTarget);
            }
        }
    }

    [Fact]
    public void Statistics_only_ever_go_up()
    {
        // They are cumulative, so a snapshot that went backwards would mean the consumer had to
        // decide which one to believe.
        var plan = MatchScript.For(Fixture, seed: 88);

        foreach (var side in plan.Statistics.GroupBy(s => s.TeamId))
        {
            var ordered = side.OrderBy(s => s.AsOfMinute).ToList();

            for (var index = 1; index < ordered.Count; index++)
            {
                ordered[index].Shots.ShouldBeGreaterThanOrEqualTo(ordered[index - 1].Shots);
                ordered[index].Corners.ShouldBeGreaterThanOrEqualTo(ordered[index - 1].Corners);
                ordered[index].Fouls.ShouldBeGreaterThanOrEqualTo(ordered[index - 1].Fouls);
            }
        }
    }

    [Fact]
    public void The_same_seed_produces_the_same_team_sheets_and_statistics()
    {
        var first = MatchScript.For(Fixture, seed: 2026);
        var second = MatchScript.For(Fixture, seed: 2026);

        // Compared field by field: a record holding a list compares that list by reference, so record
        // equality here would pass for two sheets that had nothing in common.
        Flatten(second).ShouldBe(Flatten(first));
        second.Statistics.ShouldBe(first.Statistics);
    }

    private static List<string> Flatten(MatchPlan plan) =>
    [
        .. plan.Lineups.SelectMany(lineup =>
            lineup.Players.Select(player =>
                $"{lineup.TeamId}|{lineup.Formation}|{player.PlayerId}|{player.ShirtNumber}|{player.Position}|{player.Starter}")),
    ];

    private static bool IsGoal(MatchEventPayload @event) =>
        @event.Kind is MatchEventKind.Goal or MatchEventKind.PenaltyGoal or MatchEventKind.OwnGoal;

    private static Dictionary<Guid, HashSet<Guid>> OnPitchBySide(MatchPlan plan) =>
        plan.Lineups.ToDictionary(
            lineup => lineup.TeamId,
            lineup => lineup.Players.Where(player => player.Starter).Select(player => player.PlayerId).ToHashSet());

    private static void Apply(MatchPlan plan, Dictionary<Guid, HashSet<Guid>> onPitch, MatchEventPayload @event)
    {
        _ = plan;

        switch (@event.Kind)
        {
            case MatchEventKind.Substitution when @event.PlayerId is { } on && @event.ReplacedPlayerId is { } off:
                onPitch[@event.TeamId].Remove(off);
                onPitch[@event.TeamId].Add(on);
                break;

            case MatchEventKind.Red when @event.PlayerId is { } dismissed:
                onPitch[@event.TeamId].Remove(dismissed);
                break;

            default:
                break;
        }
    }
}
