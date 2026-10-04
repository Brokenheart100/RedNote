using System.ServiceModel;
using ProtoBuf;
using ProtoBuf.Grpc;
using Wolverine.Grpc;

namespace RedNote.Contracts.Users;

[ServiceContract]
[WolverineGrpcService]
public interface IUserGrpcService
{
    Task<GetFollowingUserIdsResponse> GetFollowingUserIdsAsync(
        GetFollowingUserIdsRequest request,
        CallContext context = default);

    Task<GetUsersBatchResponse> GetUsersBatchAsync(
        GetUsersBatchRequest request,
        CallContext context = default);
}

[ProtoContract]
public sealed class GetFollowingUserIdsRequest
{
    [ProtoMember(1)]
    public Guid UserId { get; set; }
}

[ProtoContract]
public sealed class GetFollowingUserIdsResponse
{
    [ProtoMember(1)]
    public List<Guid> UserIds { get; set; } = [];
}

[ProtoContract]
public sealed class GetUsersBatchRequest
{
    [ProtoMember(1)]
    public List<Guid> UserIds { get; set; } = [];
}

[ProtoContract]
public sealed class GetUsersBatchResponse
{
    [ProtoMember(1)]
    public List<UserSummary> Users { get; set; } = [];
}

[ProtoContract]
public sealed class UserSummary
{
    [ProtoMember(1)]
    public Guid UserId { get; set; }

    [ProtoMember(2)]
    public string? Nickname { get; set; }

    [ProtoMember(3)]
    public string? AvatarUrl { get; set; }
}