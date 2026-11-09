using Microsoft.Extensions.DependencyInjection.Extensions;
using Quests.Api.Features.Identity;
using Quests.Api.Features.Sync;
using Quests.Api.Infrastructure.Auth;
using Quests.Api.Infrastructure.Data;
using Quests.Api.Infrastructure.Errors;
using Quests.Api.Infrastructure.Logging;
using Quests.Api.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddQuestsLogging();
builder.Services.AddQuestsProblemDetails();
builder.Services.AddQuestsData(builder.Configuration);
builder.Services.AddQuestsAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddQuestsAuthorization(builder.Configuration);
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<SyncService>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks();

var app = builder.Build();

app.LogQuestsLoggingSettings();
app.LogQuestsAuthenticationSettings();
app.LogQuestsAuthorizationSettings();
await app.MigrateQuestsDatabaseAsync();

app.UseQuestsProblemDetails();
app.UseQuestsStaticWeb();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<UserProvisioningMiddleware>();

app.MapHealthChecks("/api/health").AllowAnonymous();
app.MapMeEndpoints();
app.MapSyncEndpoints();
app.MapQuestsStaticWeb();

await app.RunAsync();

public partial class Program;
