using RedNote.MediaService.Features.Media.UploadImage;
using SkiaSharp;
using Xunit;

namespace RedNote.Backend.Tests;

public sealed class ImageValidationTests
{
    [Theory]
    [InlineData("upload.jpg", "image/jpeg")]
    [InlineData("upload.png", "image/png")]
    public void ExistingIntegrationFixturesAreDecodable(string name, string contentType)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", name));
        Assert.Null(ImageUploadValidator.Validate(stream, contentType));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "image/png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp")]
    public void AcceptsDecodedImages(SKEncodedImageFormat format, string contentType)
    {
        using var stream = new MemoryStream(CreateImage(format));
        Assert.Null(ImageUploadValidator.Validate(stream, contentType));
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public void RejectsDisguisedText(string contentType)
    {
        using var stream = new MemoryStream("this is not an image"u8.ToArray());
        Assert.NotNull(ImageUploadValidator.Validate(stream, contentType));
    }

    [Fact]
    public void RejectsMismatchedType()
    {
        using var stream = new MemoryStream(CreateImage(SKEncodedImageFormat.Png));
        Assert.Contains("content type", ImageUploadValidator.Validate(stream, "image/jpeg"));
    }

    [Fact]
    public void RejectsTruncatedJpeg()
    {
        using var stream = new MemoryStream(new byte[] { 0xff, 0xd8, 0xff, 0xd9 });
        Assert.NotNull(ImageUploadValidator.Validate(stream, "image/jpeg"));
    }

    [Fact]
    public void RejectsExcessiveDimensionsBeforeDecoding()
    {
        using var stream = new MemoryStream(CreateImage(SKEncodedImageFormat.Png, 8193, 1));
        Assert.Contains("dimensions", ImageUploadValidator.Validate(stream, "image/png"));
    }

    internal static byte[] CreateImage(SKEncodedImageFormat format, int width = 2, int height = 2)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        return encoded.ToArray();
    }
}
