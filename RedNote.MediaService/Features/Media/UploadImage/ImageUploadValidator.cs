using SkiaSharp;

namespace RedNote.MediaService.Features.Media.UploadImage;

internal static class ImageUploadValidator
{
    internal const long MaxPixels = 20_000_000;
    internal const int MaxDimension = 8192;

    internal static string? Validate(Stream stream, string contentType)
    {
        using var codec = SKCodec.Create(stream);
        if (codec is null)
            return "Image content is invalid or unsupported.";

        var expectedFormat = contentType switch
        {
            "image/jpeg" => SKEncodedImageFormat.Jpeg,
            "image/png" => SKEncodedImageFormat.Png,
            "image/webp" => SKEncodedImageFormat.Webp,
            _ => (SKEncodedImageFormat?)null
        };
        if (codec.EncodedFormat != expectedFormat)
            return "Image content does not match its content type.";

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > MaxDimension
            || info.Height > MaxDimension || (long)info.Width * info.Height > MaxPixels)
            return $"Image dimensions cannot exceed {MaxDimension} pixels or 20 megapixels.";

        if (codec.FrameCount > 1)
            return "Animated images are not supported.";

        // Decode after checking dimensions, so small compressed files cannot
        // cause an unbounded pixel allocation. Do not accept truncated images.
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height,
            SKColorType.Rgba8888, SKAlphaType.Premul));
        return codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SKCodecResult.Success
            ? null : "Image content is incomplete or corrupt.";
    }
}
