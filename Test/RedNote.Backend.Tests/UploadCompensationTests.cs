using System.Reflection;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RedNote.MediaService.Domain.Media;
using RedNote.MediaService.Features.Media.Common;
using RedNote.MediaService.Features.Media.UploadImage;
using RedNote.MediaService.Infrastructure.Persistence;
using SkiaSharp;
using Xunit;

namespace RedNote.Backend.Tests;

[Collection("Backend")]
public sealed class UploadCompensationTests(BackendFixture fixture)
{
    [Fact]
    public async Task FailureAfterDatabaseCommitDoesNotDeleteTheRegisteredObject()
    {
        var s3 = DispatchProxy.Create<IAmazonS3, S3Stub>();
        var stub = (S3Stub)s3;
        await using var db = new MediaServiceDbContext(new DbContextOptionsBuilder<MediaServiceDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(new FailAfterCommit()).Options);
        using var request = CreateRequest("committed.png");
        await Assert.ThrowsAsync<IOException>(() => UploadImageEndpoint.Post(request.Context.Request,
            ContentConcurrencyTests.Principal(Guid.NewGuid()), s3, Configuration(), db,
            NullLogger<MediaServiceDbContext>.Instance, default));
        Assert.Single(stub.Uploaded);
        Assert.Empty(stub.Deleted);
        await using var verificationDb = fixture.CreateMediaDb();
        Assert.True(await verificationDb.MediaAssets.AnyAsync(asset => asset.ObjectKey == stub.Uploaded.Single()));
    }

    [Fact]
    public async Task DatabaseFailureDeletesUploadedObject()
    {
        var s3 = DispatchProxy.Create<IAmazonS3, S3Stub>();
        var stub = (S3Stub)s3;
        await using var db = fixture.CreateMediaDb();
        using var request = CreateRequest(new string('x', 300) + ".png");
        await Assert.ThrowsAsync<DbUpdateException>(() => UploadImageEndpoint.Post(request.Context.Request,
            ContentConcurrencyTests.Principal(Guid.NewGuid()), s3, Configuration(), db,
            NullLogger<MediaServiceDbContext>.Instance, default));
        Assert.Single(stub.Uploaded);
        Assert.Equal(stub.Uploaded, stub.Deleted);
    }

    [Fact]
    public async Task RequestCancellationDoesNotCancelCompensation()
    {
        using var cancellation = new CancellationTokenSource();
        var s3 = DispatchProxy.Create<IAmazonS3, S3Stub>();
        var stub = (S3Stub)s3;
        stub.CancelOnPut = cancellation;
        await using var db = fixture.CreateMediaDb();
        using var request = CreateRequest("cancelled.png");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UploadImageEndpoint.Post(
            request.Context.Request, ContentConcurrencyTests.Principal(Guid.NewGuid()), s3,
            Configuration(), db, NullLogger<MediaServiceDbContext>.Instance, cancellation.Token));
        Assert.Equal(stub.Uploaded, stub.Deleted);
        Assert.False(stub.DeleteTokenWasCancelled);
    }

    [Fact]
    public async Task SuccessfulUploadKeepsBothObjectAndDatabaseRecord()
    {
        var s3 = DispatchProxy.Create<IAmazonS3, S3Stub>();
        var stub = (S3Stub)s3;
        await using var db = fixture.CreateMediaDb();
        using var request = CreateRequest("valid.png");
        var result = await UploadImageEndpoint.Post(request.Context.Request,
            ContentConcurrencyTests.Principal(Guid.NewGuid()), s3, Configuration(), db,
            NullLogger<MediaServiceDbContext>.Instance, default);
        Assert.Equal(201, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Empty(stub.Deleted);
        Assert.True(await db.MediaAssets.AnyAsync(asset => asset.ObjectKey == stub.Uploaded.Single()));
    }

    [Fact]
    public async Task PeriodicCleanupOnlyDeletesOldUnregisteredObjects()
    {
        var s3 = DispatchProxy.Create<IAmazonS3, S3Stub>();
        var stub = (S3Stub)s3;
        var id = Guid.NewGuid();
        var registered = $"images/registered/{id}.png";
        stub.Objects.AddRange([
            new S3Object { Key = registered, LastModified = DateTime.UtcNow.AddDays(-2) },
            new S3Object { Key = "images/orphan.png", LastModified = DateTime.UtcNow.AddDays(-2) },
            new S3Object { Key = "images/recent.png", LastModified = DateTime.UtcNow }
        ]);
        await using (var db = fixture.CreateMediaDb())
        {
            db.MediaAssets.Add(new MediaAsset(id, Guid.NewGuid(), "registered.png", "image/png", 10, registered));
            await db.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        services.AddDbContext<MediaServiceDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        using var provider = services.BuildServiceProvider();
        using var cleanup = new OrphanedUploadCleanup(provider.GetRequiredService<IServiceScopeFactory>(),
            s3, Configuration(), NullLogger<OrphanedUploadCleanup>.Instance);
        await cleanup.CleanupAsync(default);
        Assert.Equal(["images/orphan.png"], stub.Deleted);
    }

    private IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:mediadb"] = fixture.ConnectionString, ["S3:BucketName"] = "test"
        }).Build();

    private static UploadRequest CreateRequest(string name)
    {
        var stream = new MemoryStream(ImageValidationTests.CreateImage(SKEncodedImageFormat.Png));
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=test";
        context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(),
            new FormFileCollection { new FormFile(stream, 0, stream.Length, "file", name)
                { Headers = new HeaderDictionary(), ContentType = "image/png" } });
        return new UploadRequest(context, stream);
    }

    private sealed record UploadRequest(DefaultHttpContext Context, MemoryStream Stream) : IDisposable
    {
        public void Dispose() => Stream.Dispose();
    }

    private sealed class FailAfterCommit : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default) =>
            throw new IOException("Simulated failure after the database committed.");
    }
}

public class S3Stub : DispatchProxy
{
    public List<string> Uploaded { get; } = [];
    public List<string> Deleted { get; } = [];
    public List<S3Object> Objects { get; } = [];
    public CancellationTokenSource? CancelOnPut { get; set; }
    public bool DeleteTokenWasCancelled { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod!.Name)
        {
            case "PutObjectAsync":
                Uploaded.Add(((PutObjectRequest)args![0]!).Key);
                CancelOnPut?.Cancel();
                return Task.FromResult(new PutObjectResponse());
            case "DeleteObjectAsync":
                Deleted.Add(((DeleteObjectRequest)args![0]!).Key);
                DeleteTokenWasCancelled = ((CancellationToken)args[1]!).IsCancellationRequested;
                return Task.FromResult(new DeleteObjectResponse());
            case "ListObjectsV2Async":
                return Task.FromResult(new ListObjectsV2Response { S3Objects = Objects, IsTruncated = false });
            case "Dispose": return null;
            default: throw new NotSupportedException(targetMethod.Name);
        }
    }
}
