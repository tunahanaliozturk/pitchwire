using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pitchwire.Application.Notifications;
using Pitchwire.Domain;
using Pitchwire.Infrastructure.Notifications;

namespace Pitchwire.Api.Devices;

public sealed record DeviceSettings(
    Guid Id,
    string TimeZoneId,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    bool NotifyOnGoal,
    bool NotifyOnRedCard,
    bool NotifyOnKickoff,
    bool NotifyOnFullTime,
    IReadOnlyList<Guid> Favourites);

public sealed record PreferencesRequest(
    string TimeZoneId,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    bool NotifyOnGoal,
    bool NotifyOnRedCard,
    bool NotifyOnKickoff,
    bool NotifyOnFullTime);

public sealed record FavouritesRequest(IReadOnlyList<Guid> TeamIds);

public sealed record PushKey(string PublicKey);

public sealed record SubscriptionKeys(string P256dh, string Auth);

public sealed record SubscriptionRequest(string Endpoint, SubscriptionKeys Keys);

/// <summary>
/// Everything a browser does for itself: identify, follow teams, and say how it wants to be told.
/// </summary>
public static class DeviceEndpoints
{
    public const string CookieName = "pitchwire-device";

    public static void MapDevices(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // The public half of the signing key, which a browser needs before it can subscribe. It is
        // public by design: the private half is what proves a message came from this service.
        routes.MapGet("/push/public-key", (VapidKeys keys) => TypedResults.Ok(new PushKey(keys.PublicKey)))
            .WithName("PushPublicKey");

        routes.MapPost("/devices", (Delegate)IssueAsync).WithName("IssueDevice");
        routes.MapGet("/devices/me", (Delegate)MeAsync).WithName("CurrentDevice");
        routes.MapPut("/devices/me/preferences", (Delegate)PreferencesAsync).WithName("UpdatePreferences")
            .RequireRateLimiting(ApiRateLimits.DeviceWrite).ProducesProblem(StatusCodes.Status429TooManyRequests);
        routes.MapPut("/devices/me/favourites", (Delegate)FavouritesAsync).WithName("UpdateFavourites")
            .RequireRateLimiting(ApiRateLimits.DeviceWrite).ProducesProblem(StatusCodes.Status429TooManyRequests);
        routes.MapPost("/devices/me/push-subscriptions", (Delegate)SubscribeAsync).WithName("Subscribe")
            .RequireRateLimiting(ApiRateLimits.DeviceWrite).ProducesProblem(StatusCodes.Status429TooManyRequests);
        routes.MapDelete("/devices/me/push-subscriptions", (Delegate)UnsubscribeAsync).WithName("Unsubscribe")
            .RequireRateLimiting(ApiRateLimits.DeviceWrite).ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<Created<DeviceSettings>> IssueAsync(
        HttpContext context,
        DeviceRegistry registry,
        CancellationToken cancellationToken)
    {
        var (device, token) = await registry.IssueAsync(cancellationToken);

        context.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            // Not readable by script, not sent across sites, and not sent in clear. A token that any
            // injected script can read is a token that identifies somebody else tomorrow.
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(365),
            Path = "/",
        });

        return TypedResults.Created($"/devices/{device.Id}", await SettingsAsync(registry, device, cancellationToken));
    }

    private static async Task<Results<Ok<DeviceSettings>, UnauthorizedHttpResult>> MeAsync(
        HttpContext context,
        DeviceRegistry registry,
        CancellationToken cancellationToken)
    {
        var device = await CurrentAsync(context, registry, cancellationToken);

        return device is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(await SettingsAsync(registry, device, cancellationToken));
    }

    private static async Task<Results<Ok<DeviceSettings>, UnauthorizedHttpResult, ProblemHttpResult>> PreferencesAsync(
        HttpContext context,
        DeviceRegistry registry,
        PreferencesRequest request,
        CancellationToken cancellationToken)
    {
        var device = await CurrentAsync(context, registry, cancellationToken);

        if (device is null)
        {
            return TypedResults.Unauthorized();
        }

        // Checked here rather than when a goal is scored. A zone this machine cannot resolve would
        // otherwise turn into quiet hours read in UTC, which silences notifications at times the
        // person who set them could never explain.
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZoneId, out _))
        {
            return TypedResults.Problem(
                title: "That time zone is not one this service knows.",
                detail: "Send an IANA identifier, such as Europe/Istanbul.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if ((request.QuietHoursStart is null) != (request.QuietHoursEnd is null))
        {
            return TypedResults.Problem(
                title: "Quiet hours need both ends or neither.",
                detail: "Send a start and an end, or leave both empty to be told at any hour.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        device.TimeZoneId = request.TimeZoneId;
        device.QuietHoursStart = request.QuietHoursStart;
        device.QuietHoursEnd = request.QuietHoursEnd;
        device.NotifyOnGoal = request.NotifyOnGoal;
        device.NotifyOnRedCard = request.NotifyOnRedCard;
        device.NotifyOnKickoff = request.NotifyOnKickoff;
        device.NotifyOnFullTime = request.NotifyOnFullTime;

        await registry.SaveAsync(cancellationToken);

        return TypedResults.Ok(await SettingsAsync(registry, device, cancellationToken));
    }

    private static async Task<Results<Ok<DeviceSettings>, UnauthorizedHttpResult>> FavouritesAsync(
        HttpContext context,
        DeviceRegistry registry,
        FavouritesRequest request,
        CancellationToken cancellationToken)
    {
        var device = await CurrentAsync(context, registry, cancellationToken);

        if (device is null)
        {
            return TypedResults.Unauthorized();
        }

        await registry.SetFavouritesAsync(device.Id, request.TeamIds, cancellationToken);

        return TypedResults.Ok(await SettingsAsync(registry, device, cancellationToken));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, Conflict>> SubscribeAsync(
        HttpContext context,
        DeviceRegistry registry,
        SubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var device = await CurrentAsync(context, registry, cancellationToken);

        if (device is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await registry.SubscribeAsync(
                device.Id, request.Endpoint, request.Keys.P256dh, request.Keys.Auth, cancellationToken))
        {
            return TypedResults.Conflict();
        }

        return TypedResults.NoContent();
    }

    // The endpoint arrives in the query string rather than a body. A DELETE with a body is refused
    // outright by minimal APIs, and it is a shape proxies and caches disagree about anyway.
    private static async Task<Results<NoContent, UnauthorizedHttpResult>> UnsubscribeAsync(
        HttpContext context,
        DeviceRegistry registry,
        [FromQuery] string endpoint,
        CancellationToken cancellationToken)
    {
        var device = await CurrentAsync(context, registry, cancellationToken);

        if (device is null)
        {
            return TypedResults.Unauthorized();
        }

        await registry.UnsubscribeAsync(device.Id, endpoint, cancellationToken);

        return TypedResults.NoContent();
    }

    private static Task<Device?> CurrentAsync(
        HttpContext context,
        DeviceRegistry registry,
        CancellationToken cancellationToken) =>
        context.Items.TryGetValue(DeviceWriteIdentityMiddleware.ContextKey, out var identity)
            ? Task.FromResult(identity as Device)
            : registry.FindAsync(context.Request.Cookies[CookieName], cancellationToken);

    private static async Task<DeviceSettings> SettingsAsync(
        DeviceRegistry registry,
        Device device,
        CancellationToken cancellationToken) =>
        new(
            device.Id,
            device.TimeZoneId,
            device.QuietHoursStart,
            device.QuietHoursEnd,
            device.NotifyOnGoal,
            device.NotifyOnRedCard,
            device.NotifyOnKickoff,
            device.NotifyOnFullTime,
            await registry.FavouritesAsync(device.Id, cancellationToken));
}
