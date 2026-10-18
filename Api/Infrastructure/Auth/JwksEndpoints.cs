using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Auth;

public static class JwksEndpoints
{
    /// <summary>
    /// OpenID discovery document and JWKS: the other services read the token signing key from here. Cluster-internal
    /// (the gateway does not route /.well-known).
    /// </summary>
    public static void MapJwks(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/openid-configuration", (HttpRequest request, IOptions<AuthOptions> options) =>
            Results.Json(new
            {
                issuer = options.Value.Issuer,
                jwks_uri = $"{request.Scheme}://{request.Host}{request.PathBase}/.well-known/jwks.json",
                id_token_signing_alg_values_supported = new[] { SigningKey.Algorithm }
            }));

        app.MapGet("/.well-known/jwks.json", (SigningKey key) => Results.Json(new { keys = new[] { key.Jwk } }));
    }
}
