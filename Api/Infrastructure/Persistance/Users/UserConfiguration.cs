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
        // Only accounts with a password: rows created before sign-up existed (DevAuth / Entra) can share an email and
        // can never log in.
        builder.HasIndex(u => u.Email).IsUnique().HasFilter("\"PasswordHash\" IS NOT NULL");
        builder.Property(u => u.Name).HasMaxLength(255);
        builder.Property(u => u.Lastname).HasMaxLength(255);
        builder.Property(u => u.Company).HasMaxLength(255);
        builder.Property(u => u.Tenant).HasMaxLength(30);
        builder.HasIndex(u => u.Tenant).IsUnique();
    }
}
