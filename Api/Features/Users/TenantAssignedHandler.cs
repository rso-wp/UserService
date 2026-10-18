using Api.Infrastructure.Messaging;
using NATS.Client.JetStream;

namespace Api.Features.Users;

public static class TenantAssignedHandler
{
    /// <summary>Publishes the committed <see cref="TenantAssigned"/> to NATS (ProvisioningService needs it for host names).</summary>
    public static async Task Handle(TenantAssigned message, INatsJSContext js, CancellationToken ct)
    {
        await js.EnsureUsersStreamAsync(ct);
        await js.PublishJsonAsync(UserSubjects.TenantAssigned, message, ct);
    }
}
