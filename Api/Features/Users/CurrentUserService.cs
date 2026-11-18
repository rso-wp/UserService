using System.Security.Claims;
using Api.Domain;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Messaging;
using Api.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace Api.Features.Users;

/// <summary>
/// The profile of the calling user. Keycloak owns the account (sign-up, log-in, password), so UserService first hears of a user
/// when that user's token arrives: <see cref="GetAsync"/> / <see cref="UpdateAsync"/> create the profile then and publish
/// <see cref="UserCreated"/> (there is no sign-up call any more to publish it from).
/// </summary>
public class CurrentUserService(IDbContextOutbox<UserDbContext> outbox, IServiceScopeFactory scopes)
{
    // Each round of a race has one winner, so many users setting the same company at once need several rounds.
    private const int SaveAttempts = 10;

    /// <summary>The calling user (the token's `sub`); created from the token's claims the first time.</summary>
    public Task<User> GetAsync(ClaimsPrincipal principal, CancellationToken ct) => EnsureProfileAsync(outbox, principal, ct);

    /// <summary>
    /// Creates the profile of the token's user if there is none: id, email, and first and last name from the token (what the user
    /// typed when signing up in Keycloak). The row and <see cref="UserCreated"/> are saved in one transaction (Wolverine
    /// outbox), so the message goes out only if the insert commits. Also keeps the email in step with Keycloak.
    /// </summary>
    internal static async Task<User> EnsureProfileAsync(
        IDbContextOutbox<UserDbContext> outbox, ClaimsPrincipal principal, CancellationToken ct)
    {
        var db = outbox.DbContext;
        var id = principal.GetUserId();
        var email = (principal.GetEmail() ?? "").Trim().ToLowerInvariant();

        var user = await db.Users.FindAsync([id], ct);
        if (user is not null)
        {
            if (email != "" && user.Email != email)
            {
                user.Email = email;
                await db.SaveChangesAsync(ct);
            }
            return user;
        }

        user = new User
        {
            Id = id,
            Email = email,
            Name = Clamp(principal.GetGivenName()),
            Lastname = Clamp(principal.GetFamilyName())
        };
        db.Users.Add(user);
        await outbox.PublishAsync(new UserCreated(user.Id, user.Email));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
            return user;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request of the same user created it first (the frontend asks for the profile more than once).
            db.Entry(user).State = EntityState.Detached;
            return await db.Users.FindAsync([id], ct) ?? throw ex;
        }
    }

    private static string? Clamp(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 255)];

    /// <summary>
    /// Applies <paramref name="change"/> to the calling user and saves it. The first time name, last name and company are all set the user also
    /// gets a unique tenant slug, published as <see cref="TenantAssigned"/> in the same transaction (outbox). A concurrent
    /// user can take the same slug between the check and the commit; the unique index rejects that, and the update is
    /// repeated (after a short random pause, so the racers do not collide again) in a fresh DbContext, which picks the next
    /// free slug.
    /// </summary>
    public async Task<User> UpdateAsync(ClaimsPrincipal principal, Action<User> change, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var fresh = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<UserDbContext>>();
            var user = await EnsureProfileAsync(fresh, principal, ct);
            change(user);
            await EnsureNameFreeInCompanyAsync(fresh.DbContext, user, ct);
            try
            {
                await SaveAsync(fresh, user, ct);
                return user;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                               {
                                                   SqlState: PostgresErrorCodes.UniqueViolation,
                                                   ConstraintName: "IX_Users_Company_Name_Lastname"
                                               })
            {
                // A concurrent update took the same name in this company first.
                throw UserErrors.DuplicateNameInCompany();
            }
            catch (DbUpdateException ex) when (attempt < SaveAttempts
                                               && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await Task.Delay(Random.Shared.Next(10, 50) * attempt, ct);
            }
        }
    }

    /// <summary>At most one user per name and last name (ignoring case) in a company.</summary>
    private static async Task EnsureNameFreeInCompanyAsync(UserDbContext db, User user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.Company) || string.IsNullOrWhiteSpace(user.Name) || string.IsNullOrWhiteSpace(user.Lastname))
            return;

        var (company, name, lastname) = (user.Company.ToLower(), user.Name.ToLower(), user.Lastname.ToLower());
        var taken = await db.Users.AnyAsync(
            u => u.Id != user.Id && u.Company != null && u.Name != null && u.Lastname != null
                 && u.Company.ToLower() == company && u.Name.ToLower() == name && u.Lastname.ToLower() == lastname, ct);
        if (taken) throw UserErrors.DuplicateNameInCompany();
    }

    private static async Task SaveAsync(IDbContextOutbox<UserDbContext> outbox, User user, CancellationToken ct)
    {
        if (user.Tenant is null && !string.IsNullOrWhiteSpace(user.Name) && !string.IsNullOrWhiteSpace(user.Lastname)
            && !string.IsNullOrWhiteSpace(user.Company))
        {
            var slug = TenantSlug.From(user.Name, user.Lastname, user.Company);
            for (var attempt = 1; user.Tenant is null; attempt++)
            {
                var candidate = TenantSlug.Candidate(slug, attempt);
                if (!await outbox.DbContext.Users.AnyAsync(u => u.Tenant == candidate, ct)) user.Tenant = candidate;
            }
            await outbox.PublishAsync(new TenantAssigned(user.Id, user.Tenant));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(ct);
    }
}
