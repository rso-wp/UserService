using System.Security.Claims;
using Api.Domain;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Messaging;
using Api.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace Api.Features.Users;

public class CurrentUserService(IDbContextOutbox<UserDbContext> outbox)
{
    private UserDbContext Db => outbox.DbContext;

    /// <summary>
    /// Returns the calling user, creating the row on first sign-in. A new user is saved together with a
    /// <see cref="UserCreated"/> message (Wolverine outbox), so the message goes out only if the insert commits.
    /// </summary>
    public async Task<User> GetOrCreateAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var id = principal.GetUserId();
        var user = await Db.Users.FindAsync([id], ct);
        if (user is not null) return user;

        user = new User { Id = id, Email = principal.GetEmail() };
        Db.Users.Add(user);
        await outbox.PublishAsync(new UserCreated(user.Id, user.Email));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent first request created the user (and published UserCreated) already.
            Db.ChangeTracker.Clear();
            return await Db.Users.SingleAsync(u => u.Id == id, ct);
        }
        return user;
    }

    public Task SaveChangesAsync(CancellationToken ct) => Db.SaveChangesAsync(ct);
}
