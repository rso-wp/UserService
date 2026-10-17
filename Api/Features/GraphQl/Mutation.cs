using System.Security.Claims;
using Api.Domain;
using Api.Features.Users;
using Api.Infrastructure.Persistance;
using HotChocolate.Authorization;

namespace Api.Features.GraphQl;

public record PatchUserInput(
    Optional<string?> Name,
    Optional<string?> Lastname,
    Optional<string?> Company);

public class Mutation
{
    [Authorize]
    public async Task<User> PatchMe(
        PatchUserInput input,
        ClaimsPrincipal principal,
        CurrentUserService users,
        UserDbContext db,
        CancellationToken ct)
    {
        var user = await users.GetOrCreateAsync(principal, ct);

        if (input.Name.HasValue) user.Name = input.Name.Value;
        if (input.Lastname.HasValue) user.Lastname = input.Lastname.Value;
        if (input.Company.HasValue) user.Company = input.Company.Value;

        await db.SaveChangesAsync(ct);
        return user;
    }
}
