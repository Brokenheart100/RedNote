namespace RedNote.MediaService.Domain.Media;

public sealed class MediaAsset
{
    private MediaAsset()
    {
    }

    public MediaAsset(
        Guid id,
        Guid ownerUserId,
        string fileName,
        string contentType,
        long size,
        string objectKey)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Media id cannot be empty.",
                nameof(id));
        }

        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Owner user id cannot be empty.",
                nameof(ownerUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            fileName);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            contentType);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            objectKey);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        Id = id;
        OwnerUserId = ownerUserId;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        ObjectKey = objectKey;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long Size { get; private set; }

    public string ObjectKey { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }
}