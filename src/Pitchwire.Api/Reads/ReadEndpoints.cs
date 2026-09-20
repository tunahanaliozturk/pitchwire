using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Pitchwire.Application.Paging;
using Pitchwire.Application.Reads;

namespace Pitchwire.Api.Reads;

internal static class ReadEndpoints
{
    private const string TopParameter = "$top";
    private const string SkipTokenParameter = "$skiptoken";

    public static void MapReads(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // Cast, because a Task<IResult> method group binds as a RequestDelegate, runs the handler and
        // throws the result away. The symptom is a 200 with an empty body and nothing in the log.
        routes.MapGet("/matches/live", (Delegate)LiveAsync).WithName("LiveMatches");
        routes.MapGet("/matches/{matchId:guid}", (Delegate)DetailAsync).WithName("MatchDetail");
        routes.MapGet("/matches/{matchId:guid}/events", (Delegate)TimelineAsync).WithName("MatchTimeline");
        routes.MapGet("/seasons/{seasonId:guid}/fixtures", (Delegate)FixturesAsync).WithName("Fixtures");
        routes.MapGet("/seasons/{seasonId:guid}/results", (Delegate)ResultsAsync).WithName("Results");
        routes.MapGet("/seasons/{seasonId:guid}/table", (Delegate)TableAsync).WithName("Table");
        routes.MapGet("/seasons/{seasonId:guid}/scorers", (Delegate)ScorersAsync).WithName("Scorers");
        routes.MapGet("/teams/{teamId:guid}/form", (Delegate)FormAsync).WithName("Form");
    }

    private static async Task<IResult> LiveAsync(
        HttpContext context,
        MatchReads reads,
        [FromQuery(Name = TopParameter)] int? top,
        [FromQuery(Name = SkipTokenParameter)] string? skipToken,
        CancellationToken cancellationToken)
    {
        if (!TryReadCursor(skipToken, MatchReads.LiveToken, out var cursor))
        {
            return MalformedToken();
        }

        var size = PageSize.Clamp(top);
        var page = await reads.LiveAsync(size, cursor, cancellationToken);

        return Results.Ok(ToPage(context, page, MatchReads.LiveToken));
    }

    private static async Task<IResult> FixturesAsync(
        HttpContext context,
        MatchReads reads,
        Guid seasonId,
        [FromQuery] int? round,
        [FromQuery(Name = TopParameter)] int? top,
        [FromQuery(Name = SkipTokenParameter)] string? skipToken,
        CancellationToken cancellationToken)
    {
        if (!TryReadCursor(skipToken, MatchReads.FixturesToken, out var cursor))
        {
            return MalformedToken();
        }

        var size = PageSize.Clamp(top);
        var page = await reads.FixturesAsync(seasonId, round, size, cursor, cancellationToken);

        return Results.Ok(ToPage(context, page, MatchReads.FixturesToken));
    }

    private static async Task<IResult> ResultsAsync(
        HttpContext context,
        MatchReads reads,
        Guid seasonId,
        [FromQuery(Name = TopParameter)] int? top,
        [FromQuery(Name = SkipTokenParameter)] string? skipToken,
        CancellationToken cancellationToken)
    {
        if (!TryReadCursor(skipToken, MatchReads.ResultsToken, out var cursor))
        {
            return MalformedToken();
        }

        var size = PageSize.Clamp(top);
        var page = await reads.ResultsAsync(seasonId, size, cursor, cancellationToken);

        return Results.Ok(ToPage(context, page, MatchReads.ResultsToken));
    }

    private static async Task<IResult> DetailAsync(
        MatchReads reads,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var detail = await reads.DetailAsync(matchId, cancellationToken);

        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> TimelineAsync(
        MatchReads reads,
        Guid matchId,
        [FromQuery] int? from,
        CancellationToken cancellationToken) =>
        Results.Ok(await reads.TimelineAsync(matchId, from is > 0 ? from.Value : 1, cancellationToken));

    private static async Task<IResult> TableAsync(
        SeasonReads reads,
        Guid seasonId,
        CancellationToken cancellationToken) =>
        Results.Ok(await reads.TableAsync(seasonId, cancellationToken));

    private static async Task<IResult> ScorersAsync(
        SeasonReads reads,
        Guid seasonId,
        [FromQuery(Name = TopParameter)] int? top,
        CancellationToken cancellationToken) =>
        Results.Ok(await reads.ScorersAsync(seasonId, PageSize.Clamp(top), cancellationToken));

    private static async Task<IResult> FormAsync(
        MatchReads reads,
        Guid teamId,
        [FromQuery] Guid seasonId,
        [FromQuery] int? count,
        CancellationToken cancellationToken)
    {
        // Five is what a form guide means. Anything longer stops being form and becomes the season.
        var wanted = count is > 0 and <= 10 ? count.Value : 5;

        return Results.Ok(await reads.FormAsync(teamId, seasonId, wanted, cancellationToken));
    }

    private static bool TryReadCursor(string? skipToken, string kind, out MatchCursor? cursor)
    {
        cursor = null;

        if (string.IsNullOrEmpty(skipToken))
        {
            return true;
        }

        if (!SkipToken.TryDecode<MatchCursor>(skipToken, kind, out var decoded))
        {
            return false;
        }

        cursor = decoded;
        return true;
    }

    private static IResult MalformedToken() =>
        // Refused rather than treated as the first page. A client paging through a list would
        // otherwise start again at the top and repeat everything it had already read, with nothing
        // anywhere saying that it had.
        Results.Problem(
            title: "The continuation token could not be read.",
            detail: $"Send back the {SkipTokenParameter} from a nextLink exactly as it was given.",
            statusCode: StatusCodes.Status400BadRequest);

    private static Page<MatchSummary> ToPage(HttpContext context, MatchPage page, string kind) =>
        new(page.Matches, page.Next is null ? null : NextLink(context.Request, SkipToken.Encode(kind, page.Next)));

    private static string NextLink(HttpRequest request, string token)
    {
        var parameters = QueryHelpers.ParseQuery(request.QueryString.Value)
            .ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString(), StringComparer.Ordinal);

        parameters[SkipTokenParameter] = token;

        return QueryHelpers.AddQueryString(
            $"{request.Scheme}://{request.Host}{request.Path}",
            parameters);
    }
}
