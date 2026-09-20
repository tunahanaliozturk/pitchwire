using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Pitchwire.Api;

/// <summary>
/// Tightens the generated document where the default is wider than this API actually is.
/// </summary>
/// <remarks>
/// An integer property comes out declared as an integer or a string, which is a faithful description
/// of what System.Text.Json can be configured to read and a poor description of what this service
/// does. It is not configured that way, and every generated client pays for the difference: a score
/// arrives typed as a number or a string, and the caller has to narrow a union that can never happen.
/// </remarks>
internal static class OpenApiSchemas
{
    public static void NarrowNumbersToNumbers(OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddSchemaTransformer((schema, _, _) =>
        {
            if (schema.Type is { } type && type.HasFlag(JsonSchemaType.String)
                && (type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number)))
            {
                schema.Type = type & ~JsonSchemaType.String;

                // The pattern only exists to describe the string form that has just been removed.
                schema.Pattern = null;
            }

            return Task.CompletedTask;
        });
    }
}
