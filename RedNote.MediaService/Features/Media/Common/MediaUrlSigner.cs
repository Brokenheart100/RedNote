using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace RedNote.MediaService.Features.Media.Common;

// A separate type keeps Wolverine's generated upload handlers on the internal S3 client.
public sealed class MediaUrlSigner : IDisposable
{
    private readonly AmazonS3Client client;
    private readonly Protocol protocol;

    public MediaUrlSigner(string accessKey, string secretKey, string publicUrl)
    {
        var endpoint = new Uri(publicUrl);
        protocol = endpoint.Scheme == "https" ? Protocol.HTTPS : Protocol.HTTP;
        client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), new AmazonS3Config
        {
            ServiceURL = endpoint.AbsoluteUri,
            AuthenticationRegion = "us-east-1",
            ForcePathStyle = true
        });
    }

    public Task<string> GetDownloadUrlAsync(string bucket, string key) =>
        client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = bucket, Key = key, Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(15), Protocol = protocol
        });

    public void Dispose() => client.Dispose();
}
