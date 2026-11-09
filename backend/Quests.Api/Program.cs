using Microsoft.Extensions.DependencyInjection.Extensions;
using Quests.Api.Features.Identity;
using Quests.Api.Infrastructure.Auth;
using Quests.Api.Infrastructure.Data;
using Quests.Api.Infrastructure.Errors;
using Quests.Api.Infrastructure.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.AddQuestsLogging();
builder.Services.AddQuestsProblemDetails();
builder.Services.AddQuestsData(builder.Configuration);
builder.Services.AddQuestsAuthentication(builder.Configuration);
builder.Services.AddQuestsAuthorization(builder.Configuration);
builder.Services.AddScoped<CurrentUser>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks();

var app = builder.Build();

app.LogQuestsLoggingSettings();
app.LogQuestsAuthenticationSettings();
app.LogQuestsAuthorizationSettings();
await app.MigrateQuestsDatabaseAsync();

app.UseQuestsProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<UserProvisioningMiddleware>();

app.MapHealthChecks("/api/health").AllowAnonymous();
app.MapMeEndpoints();

await app.RunAsync();

public partial class Program;
