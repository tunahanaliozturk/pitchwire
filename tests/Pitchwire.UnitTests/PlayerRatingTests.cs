using Pitchwire.Domain;

namespace Pitchwire.UnitTests;

/// <summary>
/// How a match rating follows from what a player did.
/// </summary>
/// <remarks>
/// The rating is a model rather than an opinion, so what is worth pinning down is that it behaves like
/// one: the same match always gives the same number, every term moves it in the direction a reader
/// would expect, and it refuses to judge a player it has nothing to judge.
/// </remarks>
public sealed class PlayerRatingTests
{
    private static PlayerMatchLine Line(
        Position position = Position.Midfielder,
        int minutes = 90,
        int goals = 0,
        int penalties = 0,
        int ownGoals = 0,
        int assists = 0,
        int yellows = 0,
        int reds = 0,
        int scored = 1,
        int conceded = 1) =>
        new(Guid.NewGuid(), position, minutes, goals, penalties, ownGoals, assists, yellows, reds, scored, conceded);

    [Fact]
    public void A_full_match_in_a_draw_with_nothing_recorded_is_the_baseline()
    {
        PlayerRating.Calculate(Line()).ShouldBe(PlayerRating.Baseline);
    }

    [Fact]
    public void A_player_on_for_a_cameo_is_not_rated_at_all()
    {
        // A 6.0 for three minutes of stoppage time would claim to know something the log does not.
        PlayerRating.Calculate(Line(minutes: 12)).ShouldBeNull();
        PlayerRating.Calculate(Line(minutes: PlayerRating.MinimumMinutes)).ShouldNotBeNull();
    }

    [Fact]
    public void A_goal_from_open_play_is_worth_more_than_a_penalty()
    {
        var openPlay = PlayerRating.Calculate(Line(goals: 1));
        var penalty = PlayerRating.Calculate(Line(goals: 1, penalties: 1));

        openPlay.ShouldNotBeNull();
        penalty.ShouldNotBeNull();
        openPlay.Value.ShouldBeGreaterThan(penalty.Value);
    }

    [Fact]
    public void Every_contribution_moves_the_rating_the_way_a_reader_would_expect()
    {
        var baseline = PlayerRating.Calculate(Line())!.Value;

        PlayerRating.Calculate(Line(goals: 1))!.Value.ShouldBeGreaterThan(baseline);
        PlayerRating.Calculate(Line(assists: 1))!.Value.ShouldBeGreaterThan(baseline);
        PlayerRating.Calculate(Line(yellows: 1))!.Value.ShouldBeLessThan(baseline);
        PlayerRating.Calculate(Line(reds: 1))!.Value.ShouldBeLessThan(baseline);
        PlayerRating.Calculate(Line(ownGoals: 1))!.Value.ShouldBeLessThan(baseline);
    }

    [Fact]
    public void A_red_card_costs_more_than_a_yellow()
    {
        PlayerRating.Calculate(Line(reds: 1))!.Value
            .ShouldBeLessThan(PlayerRating.Calculate(Line(yellows: 1))!.Value);
    }

    [Fact]
    public void Winning_lifts_everybody_and_losing_lowers_everybody()
    {
        var draw = PlayerRating.Calculate(Line(scored: 1, conceded: 1))!.Value;

        PlayerRating.Calculate(Line(scored: 2, conceded: 1))!.Value.ShouldBeGreaterThan(draw);
        PlayerRating.Calculate(Line(scored: 0, conceded: 1))!.Value.ShouldBeLessThan(draw);
    }

    [Fact]
    public void A_clean_sheet_counts_for_the_back_line_and_not_for_a_forward()
    {
        var keeper = PlayerRating.Calculate(Line(Position.Goalkeeper, scored: 0, conceded: 0))!.Value;
        var forward = PlayerRating.Calculate(Line(Position.Forward, scored: 0, conceded: 0))!.Value;

        keeper.ShouldBeGreaterThan(forward);
    }

    [Fact]
    public void A_clean_sheet_is_only_credited_to_somebody_who_was_there_for_most_of_it()
    {
        // Coming on for the last half hour of a nil nil is not keeping a clean sheet.
        var wholeMatch = PlayerRating.Calculate(Line(Position.Defender, minutes: 90, scored: 0, conceded: 0))!.Value;
        var lateOn = PlayerRating.Calculate(Line(Position.Defender, minutes: 30, scored: 0, conceded: 0))!.Value;

        wholeMatch.ShouldBeGreaterThan(lateOn);
    }

    [Fact]
    public void Goals_conceded_count_against_the_back_line()
    {
        var one = PlayerRating.Calculate(Line(Position.Defender, scored: 1, conceded: 1))!.Value;
        var four = PlayerRating.Calculate(Line(Position.Defender, scored: 1, conceded: 4))!.Value;

        four.ShouldBeLessThan(one);
    }

    [Fact]
    public void The_rating_stays_on_the_scale()
    {
        // A hat trick of red cards in a heavy defeat is not a rating of minus four, and five goals
        // is not a fourteen.
        PlayerRating.Calculate(Line(Position.Defender, reds: 3, ownGoals: 2, scored: 0, conceded: 8))
            .ShouldBe(PlayerRating.Floor);
        PlayerRating.Calculate(Line(Position.Forward, goals: 5, assists: 3, scored: 8, conceded: 0))
            .ShouldBe(PlayerRating.Ceiling);
    }

    [Fact]
    public void The_same_match_always_gives_the_same_number()
    {
        var line = Line(Position.Midfielder, goals: 1, assists: 1, yellows: 1, scored: 2, conceded: 1);

        PlayerRating.Calculate(line).ShouldBe(PlayerRating.Calculate(line));
    }

    [Fact]
    public void Ratings_are_given_to_one_decimal_place()
    {
        var rating = PlayerRating.Calculate(Line(assists: 1, yellows: 1, scored: 2, conceded: 1))!.Value;

        (rating * 10 % 1).ShouldBe(0m);
    }
}
