using System.Security.Claims;
using Api.Domain;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Features.Users;

public class CurrentUserService(UserDbContext db)
{
    public async Task<User> GetOrCreateAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var id = principal.GetUserId();
        var user = await db.Users.FindAsync([id], ct);
        if (user is not null) return user;

        user = new User { Id = id, Email = principal.GetEmail() };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(user).State = EntityState.Detached;
            return await db.Users.SingleAsync(u => u.Id == id, ct);
        }
        return user;
    }
}
