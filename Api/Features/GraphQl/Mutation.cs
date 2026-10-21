using System.Security.Claims;
using Api.Domain;
using Api.Features.Accounts;
using Api.Features.Users;
using HotChocolate.Authorization;

namespace Api.Features.GraphQl;

public record PatchUserInput(
    Optional<string?> Name,
    Optional<string?> Lastname,
    Optional<string?> Company);

public record CredentialsInput(string Email, string Password);

public class Mutation
{
    /// <summary>Creates an account and signs it in.</summary>
    public Task<AuthPayload> SignUp(CredentialsInput input, AccountService accounts, CancellationToken ct) =>
        accounts.SignUpAsync(input.Email, input.Password, ct);

    public Task<AuthPayload> LogIn(CredentialsInput input, AccountService accounts, CancellationToken ct) =>
        accounts.LogInAsync(input.Email, input.Password, ct);

    [Authorize]
    public Task<User> PatchMe(
        PatchUserInput input,
        ClaimsPrincipal principal,
        CurrentUserService users,
        CancellationToken ct) =>
        users.UpdateAsync(principal, user =>
        {
            if (input.Name.HasValue) user.Name = input.Name.Value;
            if (input.Lastname.HasValue) user.Lastname = input.Lastname.Value;
            if (input.Company.HasValue) user.Company = input.Company.Value;
        }, ct);
}
