namespace Api.Domain;

public class User
{
    public Guid Id { get; set; }       // Entra ID object id (oid claim)
    public required string Email { get; set; }
    public string? Name { get; set; }
    public string? Lastname { get; set; }
    public string? Company { get; set; }

    /// <summary>
    /// Tenant slug, part of every site host name (site.tenant.domain). Assigned once from the company when it is first set,
    /// unique, and never changed afterwards so existing site addresses keep working.
    /// </summary>
    public string? Tenant { get; set; }
}
