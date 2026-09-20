using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Pitchwire.Infrastructure.Notifications;

/// <summary>
/// The key pair that signs push messages, taken from configuration or made on the spot.
/// </summary>
/// <remarks>
/// Nothing is committed. A private key in a compose file is still a private key in a repository, and
/// the habit is worse than the inconvenience it saves. When none is configured the service generates
/// a pair so a demo works out of the box, and says clearly what that costs: the keys change on every
/// restart, and a browser that subscribed under the old ones can no longer be reached.
/// </remarks>
public sealed partial class VapidKeys
{
    private readonly WebPushOptions _options;

    public VapidKeys(IOptions<WebPushOptions> options, ILogger<VapidKeys> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.PublicKey) && !string.IsNullOrWhiteSpace(_options.PrivateKey))
        {
            return;
        }

        var (publicKey, privateKey) = Generate();
        _options.PublicKey = publicKey;
        _options.PrivateKey = privateKey;

        GeneratedKeys(logger);
    }

    public string PublicKey => _options.PublicKey;

    /// <summary>
    /// A P-256 pair in the form the Web Push protocol asks for: the public key as an uncompressed
    /// point, the private key as the scalar, both base64url without padding.
    /// </summary>
    private static (string PublicKey, string PrivateKey) Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);

        var point = new byte[65];
        point[0] = 0x04;
        parameters.Q.X!.CopyTo(point, 1);
        parameters.Q.Y!.CopyTo(point, 33);

        return (Base64Url.EncodeToString(point), Base64Url.EncodeToString(parameters.D!));
    }

    [LoggerMessage(EventId = 1303, Level = LogLevel.Warning,
        Message = "No VAPID keys were configured, so a pair was generated for this run. Push subscriptions made now stop working when this process restarts.")]
    private static partial void GeneratedKeys(ILogger logger);
}
