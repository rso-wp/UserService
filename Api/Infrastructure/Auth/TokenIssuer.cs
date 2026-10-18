using Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Infrastructure.Auth;

public record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Issues the access token (RS256 JWT) every service accepts: `sub` = user id, `email`.</summary>
public class TokenIssuer(SigningKey key, IOptions<AuthOptions> options, TimeProvider time)
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken Issue(User user)
    {
        var auth = options.Value;
        var now = time.GetUtcNow();
        var expiresAt = now + auth.TokenLifetime;

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = auth.Issuer,
            Audience = auth.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString()
            },
            SigningCredentials = key.Credentials
        });

        return new AccessToken(token, expiresAt);
    }
}
