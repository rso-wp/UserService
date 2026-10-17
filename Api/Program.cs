using Api.Features.GraphQl;
using Api.Features.Users;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Messaging;
using Api.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddUserPersistence(builder.Configuration);
builder.Services.AddEntraAuth(builder.Configuration, builder.Environment);
builder.Services.AddNats(builder.Configuration);
builder.Services.AddScoped<CurrentUserService>();
builder.Services
    .AddGraphQLServer()
    .AddAuthorization()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>();

builder.Host.UseWolverine(opts => opts.AddUserMessaging(builder.Configuration));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
    await db.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGraphQL();

await app.RunAsync();
