namespace Api.Infrastructure.Auth;

/// <summary>Configuration section "Auth": the access tokens UserService issues.</summary>
public class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>`iss` of every token. The other services only accept tokens with this issuer.</summary>
    public string Issuer { get; set; } = "";

    /// <summary>`aud` of every token. The other services only accept tokens with this audience.</summary>
    public string Audience { get; set; } = "";

    /// <summary>RSA private key (PEM). When empty, Development generates one (see <see cref="SigningKey"/>).</summary>
    public string? SigningKey { get; set; }

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);
}
