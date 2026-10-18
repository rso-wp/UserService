using System.Security.Claims;
using Api.Domain;
using Api.Features.Users;
using HotChocolate.Authorization;

namespace Api.Features.GraphQl;

public class Query
{
    [Authorize]
    public Task<User> GetMe(ClaimsPrincipal principal, CurrentUserService users, CancellationToken ct) =>
        users.GetAsync(principal, ct);
}
