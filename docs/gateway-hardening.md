# 网关访问与部署配置

网关使用 ASP.NET Core 与 YARP 自带的认证、限流、健康探测及转发头处理，不引入新的网关框架。

## 访问策略

`Authenticated` 要求有效的 JWT。个人资料、搜索历史、点赞收藏列表和业务写操作使用该策略。公开帖子列表、详情、评论列表、用户资料、搜索、媒体读取及批量读取通过单独的匿名路由开放。后端继续验证权限及资源归属。

OIDC discovery、connect 和 Identity 登录接口匿名转发，由 Identity 校验协议、客户端和 Cookie。Nuxt 页面及 `/api/*` 使用 BFF Cookie 会话，不能在这些路由强制要求浏览器携带 JWT。未来新增公开后端接口需要显式配置匿名路由；其余后端路由默认要求认证。

## 限流

全局限流按可信代理中间件处理后的 RemoteIpAddress 和业务类别分区，不直接读取客户端发送的 X-Forwarded-For。覆盖浏览器 BFF 和直接访问的后端路径。被拒绝时返回 429 Problem Details 和 Retry-After，不排队。

生产默认每分钟：登录/注册/token 30，搜索及历史 120，图片上传 30，其他 API 600。通过 `RateLimiting:{Auth|Search|Upload|Api}PermitsPerMinute` 配置。开发环境提高额度，便于调试和批量测试。健康检查、静态资源不消耗 API 额度。

这是单实例的内存限流。网关扩容时额度会随实例数增加；需要全局额度时在统一入口使用共享限流。Nuxt 内部请求会共享 BFF 的源 IP 配额，因此生产应根据实例负载调整，不能把该额度当作用户级配额。防止绕过 BFF 限流还需要部署网络限制，不要向公网单独暴露 Nuxt 或后端服务端口。

## 可信代理与域名

生产禁止 `Security:TrustAnyForwardedHeaders=true`。Gateway 和 Identity 通过共享代码读取：

- `Security:KnownProxies`：代理 IP 数组，例如 Docker 网络内网关的固定 IP。
- `Security:KnownNetworks`：可信代理网络 CIDR 数组，仅限部署网络，不能配置整个公网或所有私有网段。
- 未配置时保留框架的 loopback 信任范围；每一跳只消费最靠近本服务的一层转发头。

外部 HTTPS 终止于入口代理时，Gateway 信任该入口代理，Identity 信任 Gateway 所在的专用网络。Docker Compose 可通过服务环境变量 `Security__KnownNetworks__0` 注入实际网络 CIDR。HTTP 本地 Docker 测试不需要恢复外部 HTTPS；正式 HTTPS 部署必须配置该信任范围，不能用开发环境的 trust-all 替代。

Gateway `AllowedHosts` 默认仅允许 localhost、127.0.0.1、gateway。正式域名通过 `AllowedHosts` 配置，以分号分隔，保留内部 gateway 域名；生产拒绝 `*`。CORS 的 `Cors:AllowedOrigins` 必须是实际前端 origin，生产禁止任意来源搭配凭据。

## 健康检查、超时与日志

网关通过共享的 `app.MapDefaultEndpoints()` 提供匿名 `/health` 和 `/alive`，不再依赖前端兜底转发。网关配置 `HealthChecks:Enabled=true`，使生产环境也保留这两个端点。AppHost 为后端开启 `HealthChecks:Enabled`，供 YARP 每 15 秒主动探测 `/health`，探测超时 5 秒。`AvailableDestinationsPolicy=HealthyAndUnknown` 排除不健康的上游，全部不健康时返回 503，成功探测后恢复转发；避免默认 HealthyOrPanic 在全体不健康时继续发送请求。健康检查只反映已注册的检查，不代表所有业务路径可用。生产入口应限制外部访问健康检查路径，后端仅放在内部网络。

YARP activity timeout：一般后端 30 秒、媒体 120 秒、Nuxt 60 秒。这是流读写空闲超时，不是整个请求的总时限；这里没有为业务写操作添加自动重试。

生产 YARP 日志默认 Warning，减少包含预签名图片完整 URL 的常规代理日志。健康探测分类 `Yarp.ReverseProxy.Health.ActiveHealthCheckMonitor` 单独设为 Warning，开发环境也不输出每 15 秒的成功探测日志，告警与错误仍保留。开发保留其他代理 Information 及 emoji 调试中间件，调试字段只记录 Cookie 名称及不含 query 的 URL。调试 TraceId 使用实际分布式 Activity TraceId。

验证命令：`dotnet test Test/RedNote.Backend.Tests/RedNote.Backend.Tests.csproj -c Release --filter FullyQualifiedName~GatewayTests`。测试启动真实 Kestrel/YARP 和隔离的测试上游，不依赖数据库，覆盖公开/私有路由、BFF/后端限流、伪造 IP、生产配置限制。

参考：[YARP 认证](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/authn-authz?view=aspnetcore-10.0)、[限流](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/rate-limiting?view=aspnetcore-10.0)、[健康探测](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/dests-health-checks?view=aspnetcore-10.0)。
