using Api.Domain;

namespace Api.Features.GraphQl;

/// <summary>The GraphQL `User`: the profile columns.</summary>
public class UserType : ObjectType<User>
{
    protected override void Configure(IObjectTypeDescriptor<User> descriptor)
    {
    }
}
