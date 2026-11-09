using System.Diagnostics;

namespace Quests.Api.Infrastructure.Errors;

/// <summary>
/// RFC 7807 ProblemDetails for all error responses. Unhandled exceptions are logged at Error
/// by the built-in exception handler middleware (with request path and trace id).
/// </summary>
public static class ProblemDetailsSetup
{
    public static IServiceCollection AddQuestsProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions["traceId"] = traceId;
            };
        });
        return services;
    }

    public static WebApplication UseQuestsProblemDetails(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}
