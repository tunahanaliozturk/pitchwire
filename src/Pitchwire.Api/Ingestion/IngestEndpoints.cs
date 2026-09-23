using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Pitchwire.Application.Ingestion;
using Pitchwire.Contracts;

namespace Pitchwire.Api.Ingestion;

internal static class IngestEndpoints
{
    public static void MapIngestion(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // The handler is cast because a Task<IResult> method group binds as a RequestDelegate, which
        // runs the method and then throws the result away. The symptom is an endpoint that answers 200
        // with an empty body and no error anywhere.
        routes.MapPost("/ingest/events", (Delegate)HandleAsync)
            .RequireRateLimiting(ApiRateLimits.SignedIngest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<Accepted<IngestResponse>> HandleAsync(
        IngestRequest request,
        EventIngestor ingestor,
        SnapshotIngestor snapshots,
        IOptions<IngestOptions> options,
        CancellationToken cancellationToken)
    {
        // Team sheets first: a goal that arrives in the same batch as the lineup should find the
        // scorer already named, so the rating for that match is complete the moment it is asked for.
        var (lineups, statistics) = await snapshots.StoreAsync(
            request.Lineups ?? [],
            request.Statistics ?? [],
            cancellationToken);

        var events = await ingestor.IngestAsync(options.Value.Provider, request.Events, cancellationToken);

        var response = events with { LineupsStored = lineups, StatisticsStored = statistics };

        // Accepted rather than Created: the events are stored, but what a reader sees is derived from
        // them and the notifications they cause are still on their way.
        return TypedResults.Accepted((string?)null, response);
    }
}
