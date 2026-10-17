using Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure.Persistance;

public class UserDbContext(DbContextOptions<UserDbContext> options) : DbContext(options)
{
    public const string Schema = "user_service";

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserDbContext).Assembly);
    }
}
