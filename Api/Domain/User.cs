namespace Api.Domain;

public class User
{
    public Guid Id { get; set; }       // Entra ID object id (oid claim)
    public required string Email { get; set; }
    public string? Name { get; set; }
    public string? Lastname { get; set; }
    public string? Company { get; set; }
}
