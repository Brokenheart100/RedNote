using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using ProtoBuf.Grpc.Server;
using RedNote.Authentication;
using RedNote.Contracts.Media;
using RedNote.MediaService.Infrastructure.Persistence;
using ServiceDefaults;
using Wolverine;
using Wolverine.Grpc;
using Wolverine.Http;
using Wolverine.Http.ApiVersioning;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("mediadb")
    ?? throw new InvalidOperationException("Connection string 'mediadb' was not found.");

builder.Services.AddDbContext<MediaServiceDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddRedNoteJwtAuthentication(builder.Configuration);

builder.Host.UseWolverine(options =>
{
    options.UseRuntimeCompilation();
    options.CodeGeneration.AlwaysUseServiceLocationFor<MediaServiceDbContext>();
});

builder.Services.AddWolverineHttp();

builder.Services.AddCodeFirstGrpc();

builder.Services.AddWolverineGrpc(options =>
{
    options.IncludeCodeFirstContract<IMediaGrpcService>();
});

var s3Endpoint = builder.Configuration["S3:ServiceUrl"]
    ?? throw new InvalidOperationException("S3 service URL was not found.");

var s3AccessKey = builder.Configuration["S3:AccessKey"]
    ?? throw new InvalidOperationException("S3 access key was not found.");

var s3SecretKey = builder.Configuration["S3:SecretKey"]
    ?? throw new InvalidOperationException("S3 secret key was not found.");

builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var credentials = new BasicAWSCredentials(s3AccessKey, s3SecretKey);

    var configuration = new AmazonS3Config
    {
        ServiceURL = s3Endpoint,
        ForcePathStyle = true,
        UseHttp = true
    };

    return new AmazonS3Client(credentials, configuration);
});

var app = builder.Build();

await EnsureBucketExistsAsync(app.Services, app.Lifetime.ApplicationStopping);

app.UseAuthentication();
app.UseAuthorization();

app.MapWolverineGrpcServices();

app.MapWolverineEndpoints(options =>
{
    options.UseApiVersioning(versioning =>
    {
        versioning.UrlSegmentPrefix = "api/v{version}";
        versioning.UnversionedPolicy = UnversionedPolicy.PassThrough;
    });
});

app.MapDefaultEndpoints();

await app.RunAsync();

static async Task EnsureBucketExistsAsync(
    IServiceProvider services,
    CancellationToken cancellationToken)
{
    using var scope = services.CreateScope();

    var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();

    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    var bucketName = configuration["S3:BucketName"]
        ?? throw new InvalidOperationException("S3 bucket name was not found.");

    var response = await s3.ListBucketsAsync(cancellationToken);

    var exists = response.Buckets?.Any(bucket =>
        string.Equals(
            bucket.BucketName,
            bucketName,
            StringComparison.Ordinal)) == true;

    if (exists)
    {
        return;
    }

    await s3.PutBucketAsync(
        new PutBucketRequest
        {
            BucketName = bucketName
        },
        cancellationToken);
}