using Quests.Api.Infrastructure.Data;
using Quests.Api.Infrastructure.Errors;
using Quests.Api.Infrastructure.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.AddQuestsLogging();
builder.Services.AddQuestsProblemDetails();
builder.Services.AddQuestsData(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

app.LogQuestsLoggingSettings();
await app.MigrateQuestsDatabaseAsync();

app.UseQuestsProblemDetails();

app.MapHealthChecks("/api/health");

await app.RunAsync();

public partial class Program;
