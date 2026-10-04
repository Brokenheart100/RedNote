namespace RedNote.Gateway.Configuration;

/// <summary>
/// Gateway 跨域访问配置。
/// </summary>
internal sealed class GatewayCorsOptions
{
  public const string SectionName = "Cors";

  /// <summary>
  /// 是否允许任意来源访问。
  ///
  /// 仅建议在本地开发环境启用。
  /// 启用后仍允许携带认证凭据，因此不能使用 AllowAnyOrigin()。
  /// </summary>
  public bool AllowAnyOrigin { get; init; }

  /// <summary>
  /// 允许携带认证凭据访问 Gateway 的前端来源。
  /// 来源必须包含协议和端口，且不能包含路径。
  ///
  /// 当 AllowAnyOrigin 为 true 时忽略此配置。
  /// </summary>
  public string[] AllowedOrigins { get; init; } = [];
}