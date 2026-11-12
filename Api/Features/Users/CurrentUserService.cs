using System.Security.Claims;
using Api.Domain;
using Api.Features.Accounts;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Messaging;
using Api.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace Api.Features.Users;

public class CurrentUserService(IDbContextOutbox<UserDbContext> outbox, IServiceScopeFactory scopes)
{
    // Each round of a race has one winner, so many users setting the same company at once need several rounds.
    private const int SaveAttempts = 10;

    private UserDbContext Db => outbox.DbContext;

    /// <summary>The calling user (the token's `sub`). Users are created by sign-up.</summary>
    public async Task<User> GetAsync(ClaimsPrincipal principal, CancellationToken ct) =>
        await Db.Users.FindAsync([principal.GetUserId()], ct) ?? throw AuthErrors.UserNotFound();

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
            var user = await fresh.DbContext.Users.FindAsync([principal.GetUserId()], ct) ?? throw AuthErrors.UserNotFound();
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
                throw AuthErrors.DuplicateNameInCompany();
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
        if (taken) throw AuthErrors.DuplicateNameInCompany();
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
