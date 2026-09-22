using Pitchwire.Domain;

namespace Pitchwire.UnitTests;

/// <summary>
/// How long each player was on the pitch, worked out from the team sheet and the log.
/// </summary>
/// <remarks>
/// The rating depends on it, so an error here moves a number every reader can see. The cases that go
/// wrong are the ones where a player's time on the pitch ends early: a substitution, a red card, and
/// a substitute who is then substituted himself.
/// </remarks>
public sealed class ParticipationTests
{
    private static readonly Guid MatchId = Guid.NewGuid();
    private static readonly Guid TeamId = Guid.NewGuid();

    private static MatchLineupEntry Named(Guid playerId, bool starter) => new()
    {
        MatchId = MatchId,
        TeamId = TeamId,
        PlayerId = playerId,
        ShirtNumber = 1,
        Position = Position.Midfielder,
        IsStarter = starter,
    };

    private static MatchEvent Event(int sequence, int minute, MatchEventKind kind, Guid? player, Guid? replaced = null) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = MatchId,
        Provider = "simulator",
        ProviderEventId = $"evt-{sequence}",
        Sequence = sequence,
        Minute = minute,
        Kind = kind,
        TeamId = TeamId,
        PlayerId = player,
        ReplacedPlayerId = replaced,
    };

    [Fact]
    public void A_starter_who_stays_on_plays_the_whole_match()
    {
        var starter = Guid.NewGuid();

        var minutes = Participation.MinutesPlayed([Named(starter, starter: true)], [], finalMinute: 94);

        minutes[starter].ShouldBe(94);
    }

    [Fact]
    public void A_substitution_ends_one_players_match_and_starts_another()
    {
        var off = Guid.NewGuid();
        var on = Guid.NewGuid();

        var minutes = Participation.MinutesPlayed(
            [Named(off, starter: true), Named(on, starter: false)],
            [Event(1, 62, MatchEventKind.Substitution, on, replaced: off)],
            finalMinute: 92);

        minutes[off].ShouldBe(62);
        minutes[on].ShouldBe(30);
    }

    [Fact]
    public void A_red_card_ends_a_match_early()
    {
        var dismissed = Guid.NewGuid();

        var minutes = Participation.MinutesPlayed(
            [Named(dismissed, starter: true)],
            [Event(1, 38, MatchEventKind.Red, dismissed)],
            finalMinute: 95);

        minutes[dismissed].ShouldBe(38);
    }

    [Fact]
    public void A_substitute_who_is_taken_off_again_is_counted_from_on_to_off()
    {
        var starter = Guid.NewGuid();
        var substitute = Guid.NewGuid();
        var second = Guid.NewGuid();

        var minutes = Participation.MinutesPlayed(
            [Named(starter, true), Named(substitute, false), Named(second, false)],
            [
                Event(1, 46, MatchEventKind.Substitution, substitute, replaced: starter),
                Event(2, 80, MatchEventKind.Substitution, second, replaced: substitute),
            ],
            finalMinute: 93);

        minutes[starter].ShouldBe(46);
        minutes[substitute].ShouldBe(34);
        minutes[second].ShouldBe(13);
    }

    [Fact]
    public void A_bench_player_who_never_came_on_did_not_play()
    {
        var unused = Guid.NewGuid();

        var minutes = Participation.MinutesPlayed([Named(unused, starter: false)], [], finalMinute: 90);

        minutes.ContainsKey(unused).ShouldBeFalse();
    }

    [Fact]
    public void Events_delivered_out_of_order_are_read_in_sequence()
    {
        // The log is ordered by sequence and the provider does not always deliver in it. Reading the
        // second substitution first would take off a player who had not come on yet.
        var starter = Guid.NewGuid();
        var substitute = Guid.NewGuid();
        var second = Guid.NewGuid();

        var minutes = Participation.MinutesPlayed(
            [Named(starter, true), Named(substitute, false), Named(second, false)],
            [
                Event(2, 80, MatchEventKind.Substitution, second, replaced: substitute),
                Event(1, 46, MatchEventKind.Substitution, substitute, replaced: starter),
            ],
            finalMinute: 93);

        minutes[substitute].ShouldBe(34);
    }
}
