using System.Net;
using System.Text.Json;

namespace Quests.Tests.Infrastructure;

/// <summary><c>LOG_FILE</c> sends logs to a JSON-lines file.</summary>
public sealed class FileLoggingTests
{
    [Fact]
    public async Task LogFileSet_WritesJsonLines()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"quests-tests-logs-{Guid.NewGuid():N}");
        var logFile = Path.Combine(directory, "logs", "test.log");
        try
        {
            await using (var factory = ApiFactory.WithSettings(new Dictionary<string, string> { ["LOG_FILE"] = logFile }))
            {
                using var client = factory.CreateClient();
                var response = await client.GetAsync("/api/health");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            Assert.True(File.Exists(logFile), $"Expected log file at {logFile}");
            var lines = (await File.ReadAllLinesAsync(logFile)).Where(l => l.Length > 0).ToArray();
            Assert.NotEmpty(lines);
            foreach (var line in lines)
            {
                using var json = JsonDocument.Parse(line);
                Assert.True(json.RootElement.TryGetProperty("LogLevel", out _), $"No LogLevel in: {line}");
                Assert.True(json.RootElement.TryGetProperty("Message", out _), $"No Message in: {line}");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
