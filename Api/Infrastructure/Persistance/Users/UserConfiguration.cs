using Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistance.Users;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Email).IsRequired().HasMaxLength(320);
        // No unique index on the email: Keycloak owns the account and keeps emails unique.
        builder.Property(u => u.Name).HasMaxLength(255);
        builder.Property(u => u.Lastname).HasMaxLength(255);
        builder.Property(u => u.Company).HasMaxLength(255);
        builder.Property(u => u.Tenant).HasMaxLength(30);
        builder.HasIndex(u => u.Tenant).IsUnique();
        // At most one user with the same name and last name in a company (only once all three are set).
        builder.HasIndex(u => new { u.Company, u.Name, u.Lastname }).IsUnique()
            .HasFilter("\"Company\" IS NOT NULL AND \"Name\" IS NOT NULL AND \"Lastname\" IS NOT NULL");
    }
}
