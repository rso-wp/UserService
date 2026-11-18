using System.Security.Claims;
using Api.Domain;
using Api.Features.Users;
using Api.Infrastructure.Messaging;
using Api.Infrastructure.Persistance;
using HotChocolate;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace Test;

/// <summary>
/// Keycloak owns sign-up and log-in, so UserService creates a user's profile (and publishes UserCreated) the first time that
/// user's token arrives. In-memory database: it does not enforce the unique indexes, so only the checks done in code are covered.
/// </summary>
public class CurrentUserServiceTests : IAsyncDisposable
{
    private readonly string dbName = Guid.NewGuid().ToString();
    private readonly List<object> published = [];
    private readonly ServiceProvider provider;
    private readonly AsyncServiceScope scope;
    private readonly CurrentUserService service;

    public CurrentUserServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<UserDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped(sp => CreateOutbox(sp.GetRequiredService<UserDbContext>()));
        provider = services.BuildServiceProvider();
        scope = provider.CreateAsyncScope();
        service = new CurrentUserService(
            scope.ServiceProvider.GetRequiredService<IDbContextOutbox<UserDbContext>>(),
            provider.GetRequiredService<IServiceScopeFactory>());
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
    }

    /// <summary>An outbox that saves through the DbContext and records what was published.</summary>
    private IDbContextOutbox<UserDbContext> CreateOutbox(UserDbContext db)
    {
        var outbox = Substitute.For<IDbContextOutbox<UserDbContext>>();
        outbox.DbContext.Returns(db);
        outbox.SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>()).Returns(_ => db.SaveChangesAsync());
        outbox.PublishAsync(Arg.Any<UserCreated>(), Arg.Any<DeliveryOptions?>()).Returns(call =>
        {
            published.Add(call.Arg<UserCreated>());
            return ValueTask.CompletedTask;
        });
        outbox.PublishAsync(Arg.Any<TenantAssigned>(), Arg.Any<DeliveryOptions?>()).Returns(call =>
        {
            published.Add(call.Arg<TenantAssigned>());
            return ValueTask.CompletedTask;
        });
        return outbox;
    }

    private static ClaimsPrincipal Token(Guid id, string? email = "jane@acme.test", string? given = null, string? family = null)
    {
        var claims = new List<Claim> { new("sub", id.ToString()) };
        if (email is not null) claims.Add(new Claim("email", email));
        if (given is not null) claims.Add(new Claim("given_name", given));
        if (family is not null) claims.Add(new Claim("family_name", family));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private async Task<User> Stored(Guid id)
    {
        await using var s = provider.CreateAsyncScope();
        return await s.ServiceProvider.GetRequiredService<UserDbContext>().Users.SingleAsync(u => u.Id == id);
    }

    private async Task<int> StoredCount()
    {
        await using var s = provider.CreateAsyncScope();
        return await s.ServiceProvider.GetRequiredService<UserDbContext>().Users.CountAsync();
    }

    // ---- first sight of a user (the "new user" moment) ----

    [Fact]
    public async Task FirstRequest_CreatesTheProfileFromTheToken_AndPublishesUserCreated()
    {
        var id = Guid.NewGuid();

        var user = await service.GetAsync(Token(id, "Jane@ACME.test", "Jane", "Doe"), CancellationToken.None);

        Assert.Equal(id, user.Id);
        Assert.Equal("jane@acme.test", user.Email);
        Assert.Equal("Jane", user.Name);
        Assert.Equal("Doe", user.Lastname);
        Assert.Null(user.Company);
        Assert.Null(user.Tenant);
        Assert.Equal(new UserCreated(id, "jane@acme.test"), Assert.Single(published));
        Assert.Equal("jane@acme.test", (await Stored(id)).Email);
    }

    [Fact]
    public async Task SecondRequest_ReturnsTheSameProfile_AndDoesNotPublishAgain()
    {
        var id = Guid.NewGuid();
        await service.GetAsync(Token(id), CancellationToken.None);

        var again = await service.GetAsync(Token(id), CancellationToken.None);

        Assert.Equal(id, again.Id);
        Assert.Single(published);
        Assert.Equal(1, await StoredCount());
    }

    [Fact]
    public async Task TokenWithoutNameClaims_LeavesNamesEmpty()
    {
        var user = await service.GetAsync(Token(Guid.NewGuid()), CancellationToken.None);

        Assert.Null(user.Name);
        Assert.Null(user.Lastname);
    }

    [Fact]
    public async Task EmailChangedInKeycloak_IsCopiedToTheProfile_WithoutAnotherUserCreated()
    {
        var id = Guid.NewGuid();
        await service.GetAsync(Token(id, "old@acme.test"), CancellationToken.None);

        await service.GetAsync(Token(id, "new@acme.test"), CancellationToken.None);

        Assert.Equal("new@acme.test", (await Stored(id)).Email);
        Assert.Single(published);
    }

    [Fact]
    public async Task TokenWithoutASubject_IsRejected()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("email", "jane@acme.test")], "Test"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync(principal, CancellationToken.None));

        Assert.Empty(published);
        Assert.Equal(0, await StoredCount());
    }

    [Fact]
    public async Task DifferentUsers_GetTheirOwnProfiles()
    {
        await service.GetAsync(Token(Guid.NewGuid(), "a@acme.test"), CancellationToken.None);
        await service.GetAsync(Token(Guid.NewGuid(), "b@acme.test"), CancellationToken.None);

        Assert.Equal(2, await StoredCount());
        Assert.Equal(2, published.OfType<UserCreated>().Count());
    }

    // ---- patchMe ----

    [Fact]
    public async Task Update_ForAUserWeHaveNotSeen_CreatesTheProfileFirst_ThenAppliesTheChange()
    {
        var id = Guid.NewGuid();

        var user = await service.UpdateAsync(Token(id), u => u.Company = "Acme", CancellationToken.None);

        Assert.Equal("Acme", user.Company);
        Assert.Equal("Acme", (await Stored(id)).Company);
        Assert.Single(published.OfType<UserCreated>());
    }

    [Fact]
    public async Task NameLastNameAndCompanyAllSet_AssignsTheTenant_AndPublishesTenantAssigned()
    {
        var id = Guid.NewGuid();

        var user = await service.UpdateAsync(Token(id), u =>
        {
            u.Name = "Jane";
            u.Lastname = "Doe";
            u.Company = "Acme Corp.";
        }, CancellationToken.None);

        Assert.Equal("jane-doe-acme-corp", user.Tenant);
        Assert.Contains(new TenantAssigned(id, "jane-doe-acme-corp"), published.OfType<TenantAssigned>());
    }

    [Fact]
    public async Task SignUpNamesFromKeycloak_OnlyNeedTheCompanyToGetATenant()
    {
        var id = Guid.NewGuid();
        await service.GetAsync(Token(id, given: "Jane", family: "Doe"), CancellationToken.None);

        var user = await service.UpdateAsync(Token(id), u => u.Company = "Acme", CancellationToken.None);

        Assert.Equal("jane-doe-acme", user.Tenant);
    }

    [Fact]
    public async Task NoTenantUntilAllThreeAreSet()
    {
        var user = await service.UpdateAsync(Token(Guid.NewGuid()), u => u.Company = "Acme", CancellationToken.None);

        Assert.Null(user.Tenant);
        Assert.Empty(published.OfType<TenantAssigned>());
    }

    [Fact]
    public async Task TheTenantNeverChanges_AfterItWasAssigned()
    {
        var id = Guid.NewGuid();
        await service.UpdateAsync(Token(id), u => { u.Name = "Jane"; u.Lastname = "Doe"; u.Company = "Acme"; }, CancellationToken.None);

        var user = await service.UpdateAsync(Token(id), u => u.Company = "Globex", CancellationToken.None);

        Assert.Equal("jane-doe-acme", user.Tenant);
        Assert.Single(published.OfType<TenantAssigned>());
    }

    [Fact]
    public async Task TwoUsersWithTheSameSlug_GetTheNextFreeOne()
    {
        await service.UpdateAsync(Token(Guid.NewGuid(), "a@acme.test"),
            u => { u.Name = "Jane"; u.Lastname = "Doe"; u.Company = "Acme Corp"; }, CancellationToken.None);

        // A different company name that gives the same slug.
        var second = await service.UpdateAsync(Token(Guid.NewGuid(), "b@acme.test"),
            u => { u.Name = "Jane"; u.Lastname = "Doe"; u.Company = "Acme-Corp"; }, CancellationToken.None);

        Assert.Equal("jane-doe-acme-corp-2", second.Tenant);
    }

    [Theory]
    [InlineData("Jane", "Doe", "Acme")]
    [InlineData("jane", "DOE", "acme")]
    public async Task SameNameInTheSameCompany_IsRejected_IgnoringCase(string name, string lastname, string company)
    {
        await service.UpdateAsync(Token(Guid.NewGuid(), "a@acme.test"),
            u => { u.Name = "Jane"; u.Lastname = "Doe"; u.Company = "Acme"; }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<GraphQLException>(() => service.UpdateAsync(Token(Guid.NewGuid(), "b@acme.test"),
            u => { u.Name = name; u.Lastname = lastname; u.Company = company; }, CancellationToken.None));

        Assert.Equal("DUPLICATE_NAME_IN_COMPANY", ex.Errors[0].Code);
    }

    [Fact]
    public async Task SameNameInAnotherCompany_IsAllowed()
    {
        await service.UpdateAsync(Token(Guid.NewGuid(), "a@acme.test"),
            u => { u.Name = "Jane"; u.Lastname = "Doe"; u.Company = "Acme"; }, CancellationToken.None);

        var other = await service.UpdateAsync(Token(Guid.NewGuid(), "b@globex.test"),
            u => { u.Name = "Jane"; u.Lastname = "Doe"; u.Company = "Globex"; }, CancellationToken.None);

        Assert.Equal("jane-doe-globex", other.Tenant);
    }

    [Fact]
    public async Task UpdatingYourOwnProfile_WithoutChangingTheName_IsAllowed()
    {
        var id = Guid.NewGuid();
        await service.UpdateAsync(Token(id), u => { u.Name = "Jane"; u.Lastname = "Doe"; u.Company = "Acme"; }, CancellationToken.None);

        var user = await service.UpdateAsync(Token(id), u => u.Company = "Acme", CancellationToken.None);

        Assert.Equal("jane-doe-acme", user.Tenant);
    }
}
