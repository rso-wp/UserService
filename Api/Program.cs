using Api.Features.Accounts;
using Api.Features.GraphQl;
using Api.Features.Users;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Messaging;
using Api.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddUserPersistence(builder.Configuration);
builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddNats(builder.Configuration);
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<AccountService>();
builder.Services
    .AddGraphQLServer()
    .AddAuthorization()
    .AddType<UserType>()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>();

builder.Host.UseWolverine(opts => opts.AddUserMessaging(builder.Configuration));

var app = builder.Build();

// Fail fast without a signing key (and create the development key file on the first run).
app.Services.GetRequiredService<SigningKey>();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
    await db.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGraphQL();
app.MapJwks();

await app.RunAsync();
