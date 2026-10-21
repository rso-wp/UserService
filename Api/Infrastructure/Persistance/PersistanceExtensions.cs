using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;

namespace Api.Infrastructure.Persistance;

public static class PersistanceExtensions
{
    public static string GetDefaultConnectionString(this IConfiguration configuration) =>
        configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

    public static IServiceCollection AddUserPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetDefaultConnectionString();

        // The dev database is shared with other services, so keep our migration history in our own schema.
        // Wolverine registers the DbContext itself so its transactional outbox can enlist in the same transaction.
        services.AddDbContextWithWolverineIntegration<UserDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", UserDbContext.Schema)));

        return services;
    }

    public static void AddUserMessaging(this WolverineOptions options, IConfiguration configuration)
    {
        options.PersistMessagesWithPostgresql(configuration.GetDefaultConnectionString(), UserDbContext.Schema);
        options.UseEntityFrameworkCoreTransactions();
        options.Policies.UseDurableLocalQueues();

        // Handlers forward to NATS; if it is down, retry for as long as it takes (every 15 minutes in the end) instead of
        // dead-lettering the message.
        options.OnException<Exception>()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30))
            .Then.ScheduleRetryIndefinitely(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));
    }
}
