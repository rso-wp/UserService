using Api.Domain;

namespace Api.Features.GraphQl;

/// <summary>The GraphQL `User`: every column except the password hash.</summary>
public class UserType : ObjectType<User>
{
    protected override void Configure(IObjectTypeDescriptor<User> descriptor) =>
        descriptor.Ignore(u => u.PasswordHash);
}
