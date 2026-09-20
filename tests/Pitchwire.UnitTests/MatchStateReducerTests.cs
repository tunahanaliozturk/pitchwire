using Pitchwire.Api.Domain;
using Pitchwire.Api.Ingestion;
using Pitchwire.Contracts;

namespace Pitchwire.UnitTests;

/// <summary>
/// How a match score follows from its events.
/// </summary>
/// <remarks>
/// A wrong score is the one failure a user of this product notices immediately and cannot work around.
/// The cases that produce one quietly are an own goal credited to the wrong side, a card counted as a
/// goal, and a late delivery reversed with the wrong sign, so all three are pinned here.
/// </remarks>
public sealed class MatchStateReducerTests
{
    [Fact]
    public void A_goal_for_the_home_team_raises_the_home_score()
    {
        var match = Fixtures.Match();

        MatchStateReducer.Apply(match, Fixtures.Goal(sequence: 1, minute: 12, forHome: true));

        match.HomeScore.ShouldBe(1);
        match.AwayScore.ShouldBe(0);
        match.Minute.ShouldBe(12);
        match.LastEventSequence.ShouldBe(1);
    }

    [Fact]
    public void A_goal_for_the_away_team_raises_the_away_score()
    {
        var match = Fixtures.Match();

        MatchStateReducer.Apply(match, Fixtures.Goal(sequence: 1, minute: 12, forHome: false));

        match.HomeScore.ShouldBe(0);
        match.AwayScore.ShouldBe(1);
    }

    [Fact]
    public void An_own_goal_credits_the_other_team()
    {
        // The event names the team that put the ball in its own net. Crediting the scoring side by
        // reflex is the single most likely way to show a wrong score.
        var match = Fixtures.Match();

        MatchStateReducer.Apply(match, Fixtures.Event(1, 20, MatchEventKind.OwnGoal, forHome: true));

        match.HomeScore.ShouldBe(0);
        match.AwayScore.ShouldBe(1);
    }

    [Fact]
    public void A_penalty_goal_counts_as_a_goal()
    {
        var match = Fixtures.Match();

        MatchStateReducer.Apply(match, Fixtures.Event(1, 20, MatchEventKind.PenaltyGoal, forHome: true));

        match.HomeScore.ShouldBe(1);
    }

    [Fact]
    public void Cards_and_substitutions_leave_the_score_alone()
    {
        var match = Fixtures.Match();

        MatchStateReducer.Apply(match, Fixtures.Event(1, 20, MatchEventKind.Yellow, forHome: true));
        MatchStateReducer.Apply(match, Fixtures.Event(2, 30, MatchEventKind.Red, forHome: false));
        MatchStateReducer.Apply(match, Fixtures.Event(3, 60, MatchEventKind.Substitution, forHome: true));

        match.HomeScore.ShouldBe(0);
        match.AwayScore.ShouldBe(0);
        match.Minute.ShouldBe(60);
        match.LastEventSequence.ShouldBe(3);
    }

    [Fact]
    public void Period_start_moves_the_match_to_live()
    {
        var match = Fixtures.Match();

        MatchStateReducer.Apply(match, Fixtures.Event(1, 0, MatchEventKind.PeriodStart, forHome: true));

        match.Status.ShouldBe(MatchStatus.Live);
    }

    [Fact]
    public void A_period_ending_in_the_first_half_is_half_time_and_a_later_one_is_full_time()
    {
        var match = Fixtures.Match();
        MatchStateReducer.Apply(match, Fixtures.Event(1, 0, MatchEventKind.PeriodStart, forHome: true));

        MatchStateReducer.Apply(match, Fixtures.Event(2, 47, MatchEventKind.PeriodEnd, forHome: true));
        match.Status.ShouldBe(MatchStatus.Halftime);

        MatchStateReducer.Apply(match, Fixtures.Event(3, 48, MatchEventKind.PeriodStart, forHome: true));
        match.Status.ShouldBe(MatchStatus.Live);

        MatchStateReducer.Apply(match, Fixtures.Event(4, 93, MatchEventKind.PeriodEnd, forHome: true));
        match.Status.ShouldBe(MatchStatus.Finished);
    }

    [Fact]
    public void Recompute_agrees_with_the_incrementally_applied_score()
    {
        var events = new[]
        {
            Fixtures.Event(1, 0, MatchEventKind.PeriodStart, forHome: true),
            Fixtures.Goal(2, 12, forHome: true),
            Fixtures.Event(3, 33, MatchEventKind.Yellow, forHome: false),
            Fixtures.Goal(4, 58, forHome: false),
            Fixtures.Event(5, 71, MatchEventKind.OwnGoal, forHome: false),
        };

        var incremental = Fixtures.Match();
        foreach (var @event in events)
        {
            MatchStateReducer.Apply(incremental, @event);
        }

        var rebuilt = MatchStateReducer.Recompute(Fixtures.Match(), events);

        rebuilt.HomeScore.ShouldBe(incremental.HomeScore);
        rebuilt.AwayScore.ShouldBe(incremental.AwayScore);
        rebuilt.Minute.ShouldBe(incremental.Minute);
        rebuilt.Status.ShouldBe(incremental.Status);
        rebuilt.LastEventSequence.ShouldBe(incremental.LastEventSequence);
    }

    [Fact]
    public void Recompute_ignores_the_order_the_events_arrived_in()
    {
        var events = new[]
        {
            Fixtures.Goal(1, 12, forHome: true),
            Fixtures.Goal(2, 31, forHome: false),
            Fixtures.Goal(3, 44, forHome: true),
        };

        var inOrder = MatchStateReducer.Recompute(Fixtures.Match(), events);
        var shuffled = MatchStateReducer.Recompute(Fixtures.Match(), [events[2], events[0], events[1]]);

        shuffled.ShouldBe(inOrder);
        inOrder.HomeScore.ShouldBe(2);
        inOrder.AwayScore.ShouldBe(1);
        inOrder.Minute.ShouldBe(44);
    }

    [Fact]
    public void Recompute_never_lowers_a_score_when_a_missing_event_turns_up_late()
    {
        // This is the path a late delivery takes. Rebuilding from the log can only add the event that
        // was missing, where reversing the last change needs a correct inverse for every event type
        // and turns one sign error into a score nobody can explain afterwards.
        var delivered = new[] { Fixtures.Goal(1, 12, forHome: true), Fixtures.Goal(3, 44, forHome: true) };
        var before = MatchStateReducer.Recompute(Fixtures.Match(), delivered);

        var repaired = MatchStateReducer.Recompute(
            Fixtures.Match(),
            [.. delivered, Fixtures.Goal(2, 31, forHome: false)]);

        repaired.HomeScore.ShouldBe(before.HomeScore);
        repaired.AwayScore.ShouldBe(before.AwayScore + 1);
        repaired.LastEventSequence.ShouldBe(3);
    }

    [Fact]
    public void A_win_is_three_points_and_a_draw_is_one()
    {
        var winner = Fixtures.Standing();
        var loser = Fixtures.Standing();
        StandingsMath.ApplyFinishedMatch(winner, loser, homeScore: 2, awayScore: 0);

        winner.Points.ShouldBe(3);
        winner.Won.ShouldBe(1);
        loser.Points.ShouldBe(0);
        loser.Lost.ShouldBe(1);

        var first = Fixtures.Standing();
        var second = Fixtures.Standing();
        StandingsMath.ApplyFinishedMatch(first, second, homeScore: 1, awayScore: 1);

        first.Points.ShouldBe(1);
        second.Points.ShouldBe(1);
        first.Drawn.ShouldBe(1);
        second.Drawn.ShouldBe(1);
    }

    [Fact]
    public void Goals_for_and_against_are_recorded_from_each_side()
    {
        var home = Fixtures.Standing();
        var away = Fixtures.Standing();

        StandingsMath.ApplyFinishedMatch(home, away, homeScore: 3, awayScore: 1);

        home.GoalsFor.ShouldBe(3);
        home.GoalsAgainst.ShouldBe(1);
        home.GoalDifference.ShouldBe(2);
        away.GoalsFor.ShouldBe(1);
        away.GoalsAgainst.ShouldBe(3);
        away.GoalDifference.ShouldBe(-2);
        home.Played.ShouldBe(1);
        away.Played.ShouldBe(1);
    }
}
