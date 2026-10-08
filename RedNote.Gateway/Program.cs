using RedNote.Gateway.Extensions;
using RedNote.Gateway.Middleware;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTrustedForwardedHeaders();
builder.Services.AddGatewayServices(builder.Configuration, builder.Environment);

var app = builder.Build();

if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Diagnostics:GatewayDebug:Enabled", true))
{
    app.UseMiddleware<AuthProxyDebugMiddleware>();
}

app.UseDefaultExceptionHandler();
app.UseForwardedHeaders();
app.UseRouting();

app.UseCors(GatewayServiceCollectionExtensions.FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapDefaultEndpoints();
app.MapMethods("/internal/{**path}", ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"],
    (string path) => Results.NotFound()).AllowAnonymous();

app.MapReverseProxy();

await app.RunAsync();
