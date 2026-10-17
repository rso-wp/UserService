using Api.Infrastructure.Messaging;
using NATS.Client.JetStream;

namespace Api.Features.Users;

public static class UserCreatedHandler
{
    /// <summary>
    /// Picked up by Wolverine from the durable local queue once the new user row is committed, and published to NATS
    /// (PaymentService creates the Stripe customer from it). Failures are retried by Wolverine.
    /// </summary>
    public static async Task Handle(UserCreated message, INatsJSContext js, CancellationToken ct)
    {
        await js.EnsureUsersStreamAsync(ct);
        await js.PublishJsonAsync(UserSubjects.Created, message, ct);
    }
}
