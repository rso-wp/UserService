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

public record AuthUser(Guid Id, string Email);

/// <summary>What sign-up and log-in return. Only the id and email of the user, never the password hash.</summary>
public record AuthPayload(string AccessToken, DateTimeOffset ExpiresAt, AuthUser User);

/// <summary>Sign-up and log-in with email and password (REST, see <see cref="AccountEndpoints"/>). Both return an access token for every service.</summary>
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
        if (!IsValidEmail(email)) throw AccountErrors.InvalidEmail();
        if (password.Length is < MinPasswordLength or > MaxPasswordLength) throw AccountErrors.InvalidPassword();
        if (await Db.Users.AnyAsync(u => u.Email == email && u.PasswordHash != null, ct)) throw AccountErrors.EmailTaken();

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
            throw AccountErrors.EmailTaken();
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
            throw AccountErrors.InvalidCredentials();
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash!, password);
        if (result == PasswordVerificationResult.Failed) throw AccountErrors.InvalidCredentials();
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
        return new AuthPayload(token.Token, token.ExpiresAt, new AuthUser(user.Id, user.Email));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email) =>
        email.Length <= 320 && MailAddress.TryCreate(email, out var address) && address.Address == email;
}

/// <summary>A sign-up / log-in failure. <see cref="Code"/> is for the client, the message can be shown to the user.</summary>
public class AccountException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

/// <summary>Errors of sign-up / log-in (REST: a problem response with the status and `code`).</summary>
public static class AccountErrors
{
    public static AccountException InvalidEmail() =>
        new("INVALID_EMAIL", "Enter a valid email address.", StatusCodes.Status400BadRequest);

    public static AccountException InvalidPassword() => new(
        "INVALID_PASSWORD",
        $"Use a password of {AccountService.MinPasswordLength} to {AccountService.MaxPasswordLength} characters.",
        StatusCodes.Status400BadRequest);

    public static AccountException EmailTaken() =>
        new("EMAIL_TAKEN", "An account with this email already exists.", StatusCodes.Status409Conflict);

    public static AccountException InvalidCredentials() =>
        new("INVALID_CREDENTIALS", "Wrong email or password.", StatusCodes.Status401Unauthorized);
}

/// <summary>GraphQL errors of the profile. `code` is for the client, `message` can be shown to the user.</summary>
public static class AuthErrors
{
    public static GraphQLException DuplicateNameInCompany() => Error(
        "DUPLICATE_NAME_IN_COMPANY", "Someone with this name and last name is already registered in this company.");

    public static GraphQLException UserNotFound() => Error("USER_NOT_FOUND", "This account no longer exists.");

    private static GraphQLException Error(string code, string message) =>
        new(ErrorBuilder.New().SetMessage(message).SetCode(code).Build());
}
