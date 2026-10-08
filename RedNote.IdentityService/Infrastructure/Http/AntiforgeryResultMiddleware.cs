using Microsoft.AspNetCore.Antiforgery;
using Wolverine.Http;

namespace RedNote.IdentityService.Infrastructure.Http;

// ASP.NET validates tokens in UseAntiforgery, but leaves rejection to the endpoint.
// JSON endpoints consume that verdict here, before their business logic runs.
public static class AntiforgeryResultMiddleware
{
    public static IResult Before(HttpContext context)
    {
        var required = context.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>()?.RequiresValidation == true;
        var safe = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method) || HttpMethods.IsTrace(context.Request.Method);
        return required && !safe && context.Features.Get<IAntiforgeryValidationFeature>()?.IsValid != true
            ? Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid antiforgery token.")
            : WolverineContinue.Result();
    }
}
