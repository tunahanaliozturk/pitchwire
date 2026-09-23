using Pitchwire.Application.Notifications;

namespace Pitchwire.Api.Devices;

/// <summary>Resolves a device before its write policy chooses a rate-limit partition.</summary>
internal sealed class DeviceWriteIdentityMiddleware(RequestDelegate next)
{
    internal static readonly object ContextKey = new();

    public async Task InvokeAsync(HttpContext context, DeviceRegistry registry)
    {
        var device = await registry.FindAsync(
            context.Request.Cookies[DeviceEndpoints.CookieName], context.RequestAborted);

        if (device is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Items[ContextKey] = device;
        await next(context);
    }
}
