using Api.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Users;

public static class InternalUserEndpoints
{
    /// <summary>
    /// GET /internal/users/{id}/tenant: <c>{ "tenant": "acme-corp" }</c>, or 404 while the user has none. For ProvisioningService
    /// when its copy of <see cref="Infrastructure.Messaging.TenantAssigned"/> is missing. Cluster-internal: the gateway only
    /// routes /graphql to this service.
    /// </summary>
    public static IEndpointRouteBuilder MapInternalUsers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/users/{id:guid}/tenant", async (Guid id, UserDbContext db, CancellationToken ct) =>
            await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Tenant).SingleOrDefaultAsync(ct) is { } tenant
                ? Results.Ok(new { tenant })
                : Results.NotFound());

        return app;
    }
}
