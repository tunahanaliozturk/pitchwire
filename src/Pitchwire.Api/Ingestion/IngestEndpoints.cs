using Microsoft.Extensions.Options;
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
        routes.MapPost("/ingest/events", (Delegate)HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        IngestRequest request,
        EventIngestor ingestor,
        IOptions<IngestOptions> options,
        CancellationToken cancellationToken)
    {
        var response = await ingestor.IngestAsync(options.Value.Provider, request.Events, cancellationToken);

        // Accepted rather than Created: the events are stored, but what a reader sees is derived from
        // them and the notifications they cause are still on their way.
        return Results.Accepted(value: response);
    }
}
