using System.Text.Json;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Api.Infrastructure.Messaging;

public static class NatsExtensions
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddNats(this IServiceCollection services, IConfiguration configuration)
    {
        var url = configuration["Nats:Url"]
            ?? throw new InvalidOperationException("'Nats:Url' is not configured.");

        services.AddSingleton(new NatsClient(url));
        services.AddSingleton(sp => sp.GetRequiredService<NatsClient>().CreateJetStreamContext());

        return services;
    }

    /// <summary>Creates the stream if missing. PaymentService declares the same stream.</summary>
    public static async Task EnsureUsersStreamAsync(this INatsJSContext js, CancellationToken ct)
    {
        try
        {
            await js.GetStreamAsync(UserSubjects.Stream, cancellationToken: ct);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            await js.CreateStreamAsync(
                new StreamConfig(UserSubjects.Stream, [UserSubjects.All])
                {
                    Storage = StreamConfigStorage.File,
                    MaxAge = TimeSpan.FromDays(7)
                },
                ct);
        }
    }

    public static async Task PublishJsonAsync<T>(this INatsJSContext js, string subject, T message, CancellationToken ct)
    {
        var ack = await js.PublishAsync(subject, JsonSerializer.SerializeToUtf8Bytes(message, Json), cancellationToken: ct);
        ack.EnsureSuccess();
    }
}
