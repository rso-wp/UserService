using HotChocolate;

namespace Api.Features.Users;

/// <summary>GraphQL errors of the profile. `code` is for the client, `message` can be shown to the user.</summary>
public static class UserErrors
{
    public static GraphQLException DuplicateNameInCompany() => Error(
        "DUPLICATE_NAME_IN_COMPANY", "Someone with this name and last name is already registered in this company.");

    private static GraphQLException Error(string code, string message) =>
        new(ErrorBuilder.New().SetMessage(message).SetCode(code).Build());
}
