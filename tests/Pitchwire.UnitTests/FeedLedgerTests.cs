using Pitchwire.Contracts;
using Pitchwire.Feed;

namespace Pitchwire.UnitTests;

/// <summary>
/// What the provider is able to tell a consumer that asks.
/// </summary>
public sealed class FeedLedgerTests
{
    private static readonly CatalogueFixture Fixture = Catalogue.Fixtures[0];

    [Fact]
    public void The_ledger_knows_nothing_about_a_match_it_has_not_played()
    {
        var ledger = new FeedLedger();

        ledger.From(Fixture.Id, 1).ShouldBeEmpty();
    }

    [Fact]
    public void Asking_for_a_range_that_has_not_happened_hands_back_nothing()
    {
        // The provider held the whole script in advance once. A consumer repairing a gap then
        // received the rest of the match, including the final whistle, and finished a fixture in half
        // the time it should have taken. Events are known as they happen, and not before.
        var ledger = new FeedLedger();
        var script = MatchScript.For(Fixture, seed: 42).Events;

        ledger.Note(Fixture.Id, script[0]);
        ledger.Note(Fixture.Id, script[1]);

        ledger.From(Fixture.Id, 1).Count.ShouldBe(2);
        ledger.From(Fixture.Id, 3).ShouldBeEmpty();
    }

    [Fact]
    public void A_dropped_event_is_still_something_the_provider_can_hand_back()
    {
        // This is what makes a gap recoverable. An event the provider failed to deliver has to stay
        // in its own record, or the consumer asking for it would be told it never existed.
        var ledger = new FeedLedger();
        var script = MatchScript.For(Fixture, seed: 42).Events;

        ledger.Note(Fixture.Id, script[0]);
        ledger.Note(Fixture.Id, script[1]);
        ledger.Note(Fixture.Id, script[2]);

        ledger.From(Fixture.Id, script[1].Sequence).Select(e => e.Sequence)
            .ShouldBe([script[1].Sequence, script[2].Sequence]);
    }
}
