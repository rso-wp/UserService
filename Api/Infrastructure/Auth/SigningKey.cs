using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Api.Infrastructure.Auth;

/// <summary>
/// The RSA key access tokens are signed with. Its public half is published at /.well-known/jwks.json, where the other
/// services fetch it to validate tokens.
/// </summary>
public sealed class SigningKey
{
    public const string Algorithm = SecurityAlgorithms.RsaSha256;
    public const string DevKeyFile = "dev-signing-key.pem";

    private SigningKey(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);
        var keyId = Base64UrlEncoder.Encode(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()))[..16];

        Credentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = keyId }, Algorithm);
        PublicKey = new RsaSecurityKey(parameters) { KeyId = keyId };
        Jwk = new Dictionary<string, string>
        {
            ["kty"] = "RSA",
            ["use"] = "sig",
            ["alg"] = Algorithm,
            ["kid"] = keyId,
            ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
            ["e"] = Base64UrlEncoder.Encode(parameters.Exponent)
        };
    }

    public SigningCredentials Credentials { get; }
    public SecurityKey PublicKey { get; }

    /// <summary>The public key as a JSON Web Key.</summary>
    public IReadOnlyDictionary<string, string> Jwk { get; }

    /// <summary>
    /// Loads the configured PEM key. Without one, Development generates a key and keeps it in <see cref="DevKeyFile"/>
    /// (content root) so tokens survive restarts; if the file cannot be written the key only lives until the next restart.
    /// </summary>
    public static SigningKey Load(string? pem, IHostEnvironment environment, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(pem)) return FromPem(pem);
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("'Auth:SigningKey' (RSA private key, PEM) is not configured.");

        var path = System.IO.Path.Combine(environment.ContentRootPath, DevKeyFile);
        if (File.Exists(path)) return FromPem(File.ReadAllText(path));

        var rsa = RSA.Create(2048);
        try
        {
            File.WriteAllText(path, rsa.ExportRSAPrivateKeyPem());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                "Could not save the development signing key to {Path}; issued tokens stop working when UserService restarts.",
                path);
        }
        return new SigningKey(rsa);
    }

    private static SigningKey FromPem(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new SigningKey(rsa);
    }
}
