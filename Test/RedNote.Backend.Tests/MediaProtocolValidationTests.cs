using Alba;
using Amazon.S3;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Server;
using RedNote.Contracts.Media;
using RedNote.MediaService.Features.Media.Common;
using RedNote.MediaService.Features.Media.GetBatch;
using RedNote.MediaService.Infrastructure.Persistence;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.FluentValidation.Grpc;
using Wolverine.Grpc;
using Wolverine.Http;
using Wolverine.Http.ApiVersioning;
using Wolverine.Http.FluentValidation;
using Xunit;

namespace RedNote.Backend.Tests;

public sealed class MediaProtocolValidationTests : IAsyncLifetime
{
    private IAlbaHost _host = null!;
    private GrpcChannel _channel = null!;
    private IMediaGrpcService _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration["S3:BucketName"] = "validation-test";
        // Any accidental database access fails: invalid requests must stop before business queries.
        builder.Services.AddDbContext<MediaServiceDbContext>(options => options.UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1"));
        builder.Services.AddSingleton(new MediaUrlSigner("test", "test-secret", "http://localhost"));
        builder.Services.AddScoped<MediaQueryService>();
        builder.Services.AddSingleton<IAmazonS3>(_ => throw new InvalidOperationException("S3 must not be accessed by validation tests."));
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddWolverineHttp();
        builder.Services.AddCodeFirstGrpc();
        builder.Services.AddWolverineGrpc(options => options.IncludeCodeFirstContract<IMediaGrpcService>());
        builder.Host.UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(GetMediaBatchHandler).Assembly;
            options.UseRuntimeCompilation();
            options.UseFluentValidation();
            options.UseGrpcRichErrorDetails();
            options.UseFluentValidationGrpcErrorDetails();
            options.CodeGeneration.AlwaysUseServiceLocationFor<MediaServiceDbContext>();
            options.CodeGeneration.AlwaysUseServiceLocationFor<MediaQueryService>();
        });
        _host = await AlbaHost.For(builder, app =>
        {
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapWolverineGrpcServices();
            app.MapWolverineEndpoints(options =>
            {
                options.UseFluentValidationProblemDetailMiddleware();
                options.UseApiVersioning(v => { v.UrlSegmentPrefix = "api/v{version}"; v.UnversionedPolicy = UnversionedPolicy.PassThrough; });
            });
        });
        _channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = _host.Server.CreateHandler() });
        _client = _channel.CreateGrpcService<IMediaGrpcService>();
    }

    [Fact]
    public async Task HttpRejectsEmptyGuidWithFieldError()
    {
        var result = await _host.Scenario(x =>
        {
            x.Post.Json(new { mediaIds = new[] { Guid.Empty } }).ToUrl("/api/v1/media/batch");
            x.StatusCodeShouldBe(400);
        });
        var problem = result.ReadAsJson<System.Text.Json.JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("mediaIds", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GrpcRejectsInvalidIdsWithRichFieldErrors(bool tooMany)
    {
        var request = new GetMediaBatchRequest { MediaIds = tooMany ? Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList() : [Guid.Empty] };
        var exception = await Assert.ThrowsAsync<RpcException>(() => _client.GetBatchAsync(request));
        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        var trailer = exception.Trailers.GetValueBytes("grpc-status-details-bin");
        Assert.NotNull(trailer);
        var status = Google.Rpc.Status.Parser.ParseFrom(trailer);
        var details = Assert.Single(status.Details).Unpack<Google.Rpc.BadRequest>();
        Assert.Contains(details.FieldViolations, failure => failure.Field == "mediaIds");
    }

    [Fact]
    public async Task EmptyBatchWorksThroughBothProtocols()
    {
        Assert.Empty((await _client.GetBatchAsync(new())).Items);
        await _host.Scenario(x => { x.Post.Json(new { mediaIds = Array.Empty<Guid>() }).ToUrl("/api/v1/media/batch"); x.StatusCodeShouldBeOk(); });
    }

    public async Task DisposeAsync()
    {
        _channel?.Dispose();
        if (_host is not null) await _host.DisposeAsync();
    }
}
