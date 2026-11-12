using System.Security.Cryptography;
using Api.Domain;
using Api.Features.Accounts;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Messaging;
using Api.Infrastructure.Persistance;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace Test;

/// <summary>
/// Sign-up and log-in against an in-memory database. The database does not enforce the unique index, so the concurrent
/// sign-up race (index violation) is not covered here, only the "email already registered" check.
/// </summary>
public class AccountServiceTests : IDisposable
{
    private const string Issuer = "https://users.test";
    private const string Audience = "rso";

    private readonly UserDbContext db = new(
        new DbContextOptionsBuilder<UserDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly IDbContextOutbox<UserDbContext> outbox = Substitute.For<IDbContextOutbox<UserDbContext>>();
    private readonly SigningKey key;
    private readonly AccountService service;

    public AccountServiceTests()
    {
        // The outbox saves the row and the message together; here saving the row is what matters.
        outbox.DbContext.Returns(db);
        outbox.SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>()).Returns(_ => db.SaveChangesAsync());

        using var rsa = RSA.Create(2048);
        key = SigningKey.Load(rsa.ExportRSAPrivateKeyPem(), Substitute.For<IHostEnvironment>(), NullLogger.Instance);
        var tokens = new TokenIssuer(
            key, Options.Create(new AuthOptions { Issuer = Issuer, Audience = Audience }), TimeProvider.System);

        service = new AccountService(outbox, new PasswordHasher<User>(), tokens);
    }

    public void Dispose() => db.Dispose();

    private async Task<JsonWebToken> ValidatedAsync(string token)
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = key.PublicKey,
            ValidAlgorithms = [SigningKey.Algorithm]
        });
        Assert.True(result.IsValid, result.Exception?.Message);
        return (JsonWebToken)result.SecurityToken;
    }

    // ---- sign up ----

    [Fact]
    public async Task SignUp_NewEmail_CreatesTheUser_AndReturnsAValidToken()
    {
        var payload = await service.SignUpAsync("jane@acme.test", "password123", CancellationToken.None);

        var user = await db.Users.SingleAsync();
        Assert.Equal("jane@acme.test", user.Email);
        Assert.Equal(user.Id, payload.User.Id);
        Assert.Equal("jane@acme.test", payload.User.Email);

        var jwt = await ValidatedAsync(payload.AccessToken);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal("jane@acme.test", jwt.GetClaim(JwtRegisteredClaimNames.Email).Value);
        Assert.True(payload.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task SignUp_StoresAPasswordHash_NotThePassword()
    {
        await service.SignUpAsync("jane@acme.test", "password123", CancellationToken.None);

        var user = await db.Users.SingleAsync();
        Assert.NotNull(user.PasswordHash);
        Assert.DoesNotContain("password123", user.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash!, "password123"));
    }

    [Fact]
    public async Task SignUp_NormalizesTheEmail()
    {
        await service.SignUpAsync("  Jane@ACME.test ", "password123", CancellationToken.None);

        Assert.Equal("jane@acme.test", (await db.Users.SingleAsync()).Email);
    }

    [Fact]
    public async Task SignUp_PublishesUserCreated_ThroughTheOutbox()
    {
        var payload = await service.SignUpAsync("jane@acme.test", "password123", CancellationToken.None);

        await outbox.Received(1).PublishAsync(
            Arg.Is<UserCreated>(m => m.UserId == payload.User.Id && m.Email == "jane@acme.test"),
            Arg.Any<DeliveryOptions?>());
        await outbox.Received(1).SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("jane@acme.test")]
    [InlineData("JANE@acme.test")]
    [InlineData("  jane@acme.test  ")]
    public async Task SignUp_EmailAlreadyRegistered_Fails_AndCreatesNothing(string secondEmail)
    {
        await service.SignUpAsync("jane@acme.test", "password123", CancellationToken.None);
        outbox.ClearReceivedCalls();

        var ex = await Assert.ThrowsAsync<AccountException>(
            () => service.SignUpAsync(secondEmail, "another-password", CancellationToken.None));

        Assert.Equal("EMAIL_TAKEN", ex.Code);
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(1, await db.Users.CountAsync());
        await outbox.DidNotReceiveWithAnyArgs().PublishAsync(default(UserCreated)!, default);
    }

    [Theory]
    [InlineData("not-an-email", "password123", "INVALID_EMAIL")]
    [InlineData("", "password123", "INVALID_EMAIL")]
    [InlineData("jane@acme.test", "short", "INVALID_PASSWORD")]
    public async Task SignUp_InvalidInput_Fails(string email, string password, string code)
    {
        var ex = await Assert.ThrowsAsync<AccountException>(() => service.SignUpAsync(email, password, CancellationToken.None));

        Assert.Equal(code, ex.Code);
        Assert.Equal(400, ex.StatusCode);
        Assert.Empty(db.Users);
    }

    // ---- log in ----

    [Fact]
    public async Task LogIn_RightPassword_ReturnsAValidJwtForTheUser()
    {
        var signedUp = await service.SignUpAsync("jane@acme.test", "password123", CancellationToken.None);

        var payload = await service.LogInAsync("Jane@acme.test", "password123", CancellationToken.None);

        Assert.Equal(signedUp.User.Id, payload.User.Id);
        var jwt = await ValidatedAsync(payload.AccessToken);
        Assert.Equal(signedUp.User.Id.ToString(), jwt.Subject);
        Assert.Equal("jane@acme.test", jwt.GetClaim(JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Contains(Audience, jwt.Audiences);
        Assert.True(payload.ExpiresAt > DateTimeOffset.UtcNow);
        // A new token every time.
        Assert.NotEqual(signedUp.AccessToken, payload.AccessToken);
    }

    [Fact]
    public async Task LogIn_WrongPassword_Fails()
    {
        await service.SignUpAsync("jane@acme.test", "password123", CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AccountException>(
            () => service.LogInAsync("jane@acme.test", "wrong-password", CancellationToken.None));

        Assert.Equal("INVALID_CREDENTIALS", ex.Code);
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task LogIn_UnknownEmail_FailsWithTheSameError()
    {
        var ex = await Assert.ThrowsAsync<AccountException>(
            () => service.LogInAsync("nobody@acme.test", "password123", CancellationToken.None));

        Assert.Equal("INVALID_CREDENTIALS", ex.Code);
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task LogIn_AccountWithoutPassword_CannotLogIn()
    {
        db.Users.Add(new User { Id = Guid.NewGuid(), Email = "legacy@acme.test" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<AccountException>(
            () => service.LogInAsync("legacy@acme.test", "password123", CancellationToken.None));

        Assert.Equal("INVALID_CREDENTIALS", ex.Code);
    }
}
