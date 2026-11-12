namespace Api.Domain;

public class User
{
    public Guid Id { get; set; }       // the token's `sub`, the user id in every service
    public required string Email { get; set; }  // lowercase, used to log in (unique among accounts with a password)

    /// <summary>ASP.NET Core Identity password hash (PBKDF2). Never exposed over GraphQL.</summary>
    public string? PasswordHash { get; set; }
    public string? Name { get; set; }
    public string? Lastname { get; set; }
    public string? Company { get; set; }

    /// <summary>
    /// Tenant slug, part of every site host name (site.tenant.domain). Assigned once, from name, last name and company, when all three are first set,
    /// unique, and never changed afterwards so existing site addresses keep working.
    /// </summary>
    public string? Tenant { get; set; }
}
