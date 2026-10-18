using System.Security.Claims;
using Api.Domain;
using Api.Features.Accounts;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Messaging;
using Api.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Api.Features.Users;

public class CurrentUserService(IDbContextOutbox<UserDbContext> outbox)
{
    private UserDbContext Db => outbox.DbContext;

    /// <summary>The calling user (the token's `sub`). Users are created by sign-up.</summary>
    public async Task<User> GetAsync(ClaimsPrincipal principal, CancellationToken ct) =>
        await Db.Users.FindAsync([principal.GetUserId()], ct) ?? throw AuthErrors.UserNotFound();

    /// <summary>
    /// Saves profile changes. The first time a company is set the user also gets a unique tenant slug, published as
    /// <see cref="TenantAssigned"/> in the same transaction (outbox).
    /// </summary>
    public async Task SaveAsync(User user, CancellationToken ct)
    {
        if (user.Tenant is null && !string.IsNullOrWhiteSpace(user.Company))
        {
            var slug = TenantSlug.From(user.Company);
            for (var attempt = 1; user.Tenant is null; attempt++)
            {
                var candidate = TenantSlug.Candidate(slug, attempt);
                if (!await Db.Users.AnyAsync(u => u.Tenant == candidate, ct)) user.Tenant = candidate;
            }
            // A concurrent user taking the same slug fails on the unique index; the request can simply be retried.
            await outbox.PublishAsync(new TenantAssigned(user.Id, user.Tenant));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(ct);
    }
}
