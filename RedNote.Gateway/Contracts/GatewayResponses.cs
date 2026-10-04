namespace RedNote.Gateway.Contracts;

/// <summary>
/// Gateway 基础服务信息。
/// </summary>
internal sealed record ServiceInformation(
    string Name,
    string Status,
    DateTimeOffset Timestamp);

/// <summary>
/// Gateway 运行状态。
/// </summary>
internal sealed record ServiceStatusResponse(
    string Service,
    string Status,
    string Environment,
    string MachineName,
    int ProcessId,
    DateTimeOffset Timestamp);