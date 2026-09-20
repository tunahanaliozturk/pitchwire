using Pitchwire.Contracts;
using Pitchwire.Feed;

namespace Pitchwire.UnitTests;

/// <summary>
/// What the simulated provider produces before anything is sent.
/// </summary>
/// <remarks>
/// A simulator that cannot be replayed cannot be debugged. When a run produces a score nobody
/// expected, the first question is what the provider actually generated, and the only way to answer
/// it is for the same seed to give the same match every time.
/// </remarks>
public sealed class MatchScriptTests
{
    private static readonly CatalogueFixture Fixture = Catalogue.Fixtures[0];

    [Fact]
    public void The_same_seed_produces_the_same_match()
    {
        var first = MatchScript.For(Fixture, seed: 42);
        var second = MatchScript.For(Fixture, seed: 42);

        second.ShouldBe(first);
    }

    [Fact]
    public void A_different_seed_produces_a_different_match()
    {
        var first = MatchScript.For(Fixture, seed: 42);
        var second = MatchScript.For(Fixture, seed: 43);

        second.ShouldNotBe(first);
    }

    [Fact]
    public void Sequences_start_at_one_and_never_repeat()
    {
        var script = MatchScript.For(Fixture, seed: 7);

        script.Select(e => e.Sequence).ShouldBe(Enumerable.Range(1, script.Count));
    }

    [Fact]
    public void Provider_event_ids_are_unique_within_a_match()
    {
        // The consumer keys idempotency on this string. Two events sharing one would make the second
        // look like a redelivery of the first, and it would be discarded.
        var script = MatchScript.For(Fixture, seed: 7);

        script.Select(e => e.ProviderEventId).Distinct().Count().ShouldBe(script.Count);
    }

    [Fact]
    public void Every_match_opens_with_a_period_start_and_closes_with_a_period_end()
    {
        var script = MatchScript.For(Fixture, seed: 11);

        script[0].Kind.ShouldBe(MatchEventKind.PeriodStart);
        script[0].Minute.ShouldBe(0);
        script[^1].Kind.ShouldBe(MatchEventKind.PeriodEnd);
        script[^1].Minute.ShouldBeGreaterThan(90);
        script.Count(e => e.Kind == MatchEventKind.PeriodStart).ShouldBe(2);
        script.Count(e => e.Kind == MatchEventKind.PeriodEnd).ShouldBe(2);
    }

    [Fact]
    public void Every_event_names_one_of_the_two_teams_on_the_pitch()
    {
        var script = MatchScript.For(Fixture, seed: 19);

        script.ShouldAllBe(e => e.TeamId == Fixture.Home.Id || e.TeamId == Fixture.Away.Id);
    }

    [Fact]
    public void A_goal_names_a_player_from_the_team_it_is_recorded_against()
    {
        // An own goal is recorded against the side that conceded it, so the player on the event
        // belongs to that side too. Crediting the goal to the other team is the consumer's job, and
        // this is what makes it possible to check that it did.
        var script = MatchScript.For(Fixture, seed: 23);

        foreach (var goal in script.Where(e => e.Kind is MatchEventKind.Goal or MatchEventKind.OwnGoal or MatchEventKind.PenaltyGoal))
        {
            var squad = goal.TeamId == Fixture.Home.Id ? Fixture.Home.Players : Fixture.Away.Players;

            squad.ShouldContain(player => player.Id == goal.PlayerId);
        }
    }

    [Fact]
    public void An_assist_is_never_credited_to_the_scorer()
    {
        var script = MatchScript.For(Fixture, seed: 29);

        script.ShouldAllBe(e => e.AssistPlayerId == null || e.AssistPlayerId != e.PlayerId);
    }

    [Fact]
    public void The_catalogue_gives_every_pair_of_teams_exactly_one_fixture()
    {
        var pairs = Catalogue.Fixtures
            .Select(f => f.Home.Id.CompareTo(f.Away.Id) < 0 ? (f.Home.Id, f.Away.Id) : (f.Away.Id, f.Home.Id))
            .ToList();

        pairs.Distinct().Count().ShouldBe(pairs.Count);
        Catalogue.Fixtures.Count.ShouldBe(Catalogue.Teams.Count * (Catalogue.Teams.Count - 1) / 2);
    }

    [Fact]
    public void No_team_plays_itself()
    {
        Catalogue.Fixtures.ShouldAllBe(f => f.Home.Id != f.Away.Id);
    }
}
