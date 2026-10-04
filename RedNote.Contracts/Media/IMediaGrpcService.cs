using System.ServiceModel;
using ProtoBuf;
using ProtoBuf.Grpc;
using Wolverine.Grpc;

namespace RedNote.Contracts.Media;

[ServiceContract]
[WolverineGrpcService]
public interface IMediaGrpcService
{
    Task<GetMediaBatchResponse> GetBatchAsync(
        GetMediaBatchRequest request,
        CallContext context = default);
}

[ProtoContract]
public sealed class GetMediaBatchRequest
{
    [ProtoMember(1)]
    public List<Guid> MediaIds { get; set; } = [];
}

[ProtoContract]
public sealed class GetMediaBatchResponse
{
    [ProtoMember(1)]
    public List<MediaBatchItem> Items { get; set; } = [];
}

[ProtoContract]
public sealed class MediaBatchItem
{
    [ProtoMember(1)]
    public Guid Id { get; set; }

    [ProtoMember(2)]
    public Guid OwnerUserId { get; set; }

    [ProtoMember(3)]
    public string FileName { get; set; } =
        string.Empty;

    [ProtoMember(4)]
    public string ContentType { get; set; } =
        string.Empty;

    [ProtoMember(5)]
    public long Size { get; set; }

    [ProtoMember(6, DataFormat = DataFormat.WellKnown)]
    public DateTime CreatedAtUtc { get; set; }

    [ProtoMember(7)]
    public string Url { get; set; } =
        string.Empty;
}