using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceDefaults;

public static class HttpErrorExtensions
{
    public static IServiceCollection AddDefaultProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString()
                ?? context.HttpContext.TraceIdentifier);

    public static WebApplication UseDefaultExceptionHandler(this WebApplication app,
        Func<Exception, int>? statusCodeSelector = null)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = statusCodeSelector ?? (_ => StatusCodes.Status500InternalServerError),
            // Retain server-side logs and traces; clients receive a generic problem body.
            SuppressDiagnosticsCallback = _ => false
        });
        return app;
    }
}
