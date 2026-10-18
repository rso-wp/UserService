using System.Net.Mail;
using Api.Domain;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Messaging;
using Api.Infrastructure.Persistance;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace Api.Features.Accounts;

public record AuthPayload(string AccessToken, DateTimeOffset ExpiresAt, User User);

/// <summary>Sign-up and log-in with email and password. Both return an access token for every service.</summary>
public class AccountService(IDbContextOutbox<UserDbContext> outbox, IPasswordHasher<User> hasher, TokenIssuer tokens)
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    // Verified against when the email is unknown, so a log-in takes as long whether or not the account exists.
    private static readonly User NoUser = new() { Email = "" };
    private static readonly string NoUserHash = new PasswordHasher<User>().HashPassword(NoUser, Guid.NewGuid().ToString());

    private UserDbContext Db => outbox.DbContext;

    /// <summary>
    /// Creates the user and publishes <see cref="UserCreated"/> in the same transaction (Wolverine outbox), so the message
    /// goes out only if the insert commits.
    /// </summary>
    public async Task<AuthPayload> SignUpAsync(string email, string password, CancellationToken ct)
    {
        email = NormalizeEmail(email);
        if (!IsValidEmail(email)) throw AuthErrors.InvalidEmail();
        if (password.Length is < MinPasswordLength or > MaxPasswordLength) throw AuthErrors.InvalidPassword();
        if (await Db.Users.AnyAsync(u => u.Email == email && u.PasswordHash != null, ct)) throw AuthErrors.EmailTaken();

        var user = new User { Id = Guid.CreateVersion7(), Email = email };
        user.PasswordHash = hasher.HashPassword(user, password);
        Db.Users.Add(user);
        await outbox.PublishAsync(new UserCreated(user.Id, user.Email));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent sign-up took the email first.
            throw AuthErrors.EmailTaken();
        }

        return Payload(user);
    }

    public async Task<AuthPayload> LogInAsync(string email, string password, CancellationToken ct)
    {
        email = NormalizeEmail(email);
        var user = await Db.Users.SingleOrDefaultAsync(u => u.Email == email && u.PasswordHash != null, ct);
        if (user is null)
        {
            hasher.VerifyHashedPassword(NoUser, NoUserHash, password);
            throw AuthErrors.InvalidCredentials();
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash!, password);
        if (result == PasswordVerificationResult.Failed) throw AuthErrors.InvalidCredentials();
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            await Db.SaveChangesAsync(ct);
        }

        return Payload(user);
    }

    private AuthPayload Payload(User user)
    {
        var token = tokens.Issue(user);
        return new AuthPayload(token.Token, token.ExpiresAt, user);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email) =>
        email.Length <= 320 && MailAddress.TryCreate(email, out var address) && address.Address == email;
}

/// <summary>GraphQL errors of sign-up / log-in. `code` is for the client, `message` can be shown to the user.</summary>
public static class AuthErrors
{
    public static GraphQLException InvalidEmail() => Error("INVALID_EMAIL", "Enter a valid email address.");

    public static GraphQLException InvalidPassword() => Error(
        "INVALID_PASSWORD",
        $"Use a password of {AccountService.MinPasswordLength} to {AccountService.MaxPasswordLength} characters.");

    public static GraphQLException EmailTaken() => Error("EMAIL_TAKEN", "An account with this email already exists.");

    public static GraphQLException InvalidCredentials() => Error("INVALID_CREDENTIALS", "Wrong email or password.");

    public static GraphQLException UserNotFound() => Error("USER_NOT_FOUND", "This account no longer exists.");

    private static GraphQLException Error(string code, string message) =>
        new(ErrorBuilder.New().SetMessage(message).SetCode(code).Build());
}
