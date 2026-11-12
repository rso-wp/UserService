namespace Api.Features.Accounts;

public record CredentialsRequest(string? Email, string? Password);

public static class AccountEndpoints
{
    /// <summary>
    /// POST /auth/signup and POST /auth/login, both <c>{ "email", "password" }</c> and open to everyone. Success:
    /// <see cref="AuthPayload"/> (access token, expiry, user id and email). Failure: a problem response whose status
    /// and <c>code</c> are those of the <see cref="AccountException"/> (400 INVALID_EMAIL / INVALID_PASSWORD, 409 EMAIL_TAKEN,
    /// 401 INVALID_CREDENTIALS) and whose <c>detail</c> can be shown to the user.
    /// </summary>
    public static IEndpointRouteBuilder MapAccounts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").AllowAnonymous();

        group.MapPost("/signup", (CredentialsRequest request, AccountService accounts, CancellationToken ct) =>
            Run(() => accounts.SignUpAsync(request.Email ?? "", request.Password ?? "", ct), StatusCodes.Status201Created));

        group.MapPost("/login", (CredentialsRequest request, AccountService accounts, CancellationToken ct) =>
            Run(() => accounts.LogInAsync(request.Email ?? "", request.Password ?? "", ct), StatusCodes.Status200OK));

        return app;
    }

    private static async Task<IResult> Run(Func<Task<AuthPayload>> action, int successStatus)
    {
        try
        {
            return Results.Json(await action(), statusCode: successStatus);
        }
        catch (AccountException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: ex.StatusCode,
                extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
        }
    }
}
