using Pitchwire.Contracts;

namespace Pitchwire.Feed;

internal static class FeedEndpoints
{
    public static void MapFeed(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // The half of a provider that a push feed cannot do on its own. Without it, an event this
        // service dropped would be lost to the consumer for good.
        routes.MapGet("/matches/{matchId:guid}/events", (Guid matchId, FeedLedger ledger, int from = 1) =>
            Results.Ok(ledger.From(matchId, from)));

        // What a provider publishes so a consumer can set up its fixture list. Both sides derive the
        // same identifiers from the catalogue, so this is here for a reader rather than for the API.
        routes.MapGet("/fixtures", () => Results.Ok(Catalogue.Fixtures.Select(fixture => new
        {
            fixture.Id,
            fixture.Round,
            fixture.KickoffUtc,
            Home = fixture.Home.Name,
            Away = fixture.Away.Name,
        })));
    }
}
