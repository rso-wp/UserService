namespace Api.Domain;

public class User
{
    public Guid Id { get; set; }       // the token's `sub` (Keycloak's user id), the user id in every service
    public required string Email { get; set; }  // lowercase, copied from the token (Keycloak owns the account and its uniqueness)
    public string? Name { get; set; }
    public string? Lastname { get; set; }
    public string? Company { get; set; }

    /// <summary>
    /// Tenant slug, part of every site host name (site.tenant.domain). Assigned once, from name, last name and company, when all three are first set,
    /// unique, and never changed afterwards so existing site addresses keep working.
    /// </summary>
    public string? Tenant { get; set; }
}
