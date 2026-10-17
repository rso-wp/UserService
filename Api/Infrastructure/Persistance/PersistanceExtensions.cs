using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure.Persistance;

public static class PersistanceExtensions
{
    public static IServiceCollection AddUserPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        // The dev database is shared with other services, so keep our migration history in our own schema.
        services.AddDbContext<UserDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", UserDbContext.Schema)));

        return services;
    }
}
