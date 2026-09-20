using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;
using Pitchwire.Application.Notifications;
using Domain = Pitchwire.Domain;

namespace Pitchwire.Infrastructure.Notifications;

/// <summary>
/// How a notification is signed and where it is sent.
/// </summary>
public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    /// <summary>The public half, which the browser is given when it subscribes.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>The private half. Supplied by the environment and never committed.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// Who to contact about this service. Push services require it, and some of them use it when
    /// something is going wrong at their end rather than ours.
    /// </summary>
    public string Subject { get; set; } = "mailto:hello@pitchwire.invalid";
}

/// <summary>
/// Delivers one notification to one browser endpoint.
/// </summary>
/// <remarks>
/// The interesting part is what counts as final. A push service answers 404 or 410 when the
/// subscription no longer exists, and retrying either is a slow leak that ends with a table full of
/// work that can never succeed. Everything else is worth another attempt.
/// </remarks>
internal sealed class WebPushSender(PushServiceClient client, IOptions<WebPushOptions> options) : IPushSender
{
    public async Task<PushOutcome> SendAsync(
        Domain.PushSubscription subscription,
        string title,
        string body,
        string url,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.PrivateKey))
        {
            // Without keys there is nothing to sign with. Treated as retryable rather than final,
            // because the fix is configuration and the notification should still be there afterwards.
            return PushOutcome.Retry;
        }

        var target = new PushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);

        var payload = JsonSerializer.Serialize(new { title, body, url });

        try
        {
            await client.RequestPushMessageDeliveryAsync(
                target,
                new PushMessage(payload),
                new VapidAuthentication(settings.PublicKey, settings.PrivateKey) { Subject = settings.Subject },
                cancellationToken);

            return PushOutcome.Delivered;
        }
        catch (PushServiceClientException failure)
            when (failure.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushOutcome.Gone;
        }
        catch (PushServiceClientException)
        {
            return PushOutcome.Retry;
        }
        catch (HttpRequestException)
        {
            // The push service could not be reached at all, which says nothing about the subscription.
            return PushOutcome.Retry;
        }
    }
}
