using System.Security.Claims;
using Api.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Api.Infrastructure.Auth;

public static class AuthExtensions
{
    /// <summary>
    /// Password hashing, token issuing and bearer validation of our own tokens (with the key we hold; the other services
    /// fetch it from /.well-known/jwks.json instead).
    /// </summary>
    public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.Section))
            .Validate(o => o.Issuer != "" && o.Audience != "", "'Auth:Issuer' and 'Auth:Audience' must be configured.")
            .ValidateOnStart();

        services.AddSingleton(sp => SigningKey.Load(
            sp.GetRequiredService<IOptions<AuthOptions>>().Value.SigningKey,
            sp.GetRequiredService<IHostEnvironment>(),
            sp.GetRequiredService<ILogger<SigningKey>>()));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TokenIssuer>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SigningKey, IOptions<AuthOptions>>((options, key, auth) =>
            {
                options.MapInboundClaims = false; // keep raw claim names (sub, email, ...)
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = auth.Value.Issuer,
                    ValidAudience = auth.Value.Audience,
                    IssuerSigningKey = key.PublicKey,
                    ValidAlgorithms = [SigningKey.Algorithm]
                };
            });
        services.AddAuthorization();

        return services;
    }

    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out var id)
            ? id
            : throw new InvalidOperationException("Token has no valid 'sub' claim.");
}
