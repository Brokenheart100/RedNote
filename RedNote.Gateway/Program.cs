using RedNote.Gateway.Extensions;
using RedNote.Gateway.Middleware;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddGatewayServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMiddleware<AuthProxyDebugMiddleware>();
}

app.UseExceptionHandler();

app.UseCors(GatewayServiceCollectionExtensions.FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy();
app.MapDefaultEndpoints();

await app.RunAsync();