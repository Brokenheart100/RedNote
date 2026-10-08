# MediaService 端点配置

MediaService 在 `Infrastructure/Hosting/MediaEndpointExtensions.cs` 中用 `ConfigureKestrel` 配置容器监听：

- 8080：HTTP/1，处理媒体 HTTP API。
- 8081：HTTP/2，处理容器网络内的明文 gRPC。

.NET 容器基础镜像自带 `DOTNET_RUNNING_IN_CONTAINER=true`。代码使用该标记启用容器监听；不以 Production 环境名判断是否运行在容器中。本地运行时继续使用 launchSettings/Aspire 的 HTTPS 端点。

AppHost 负责声明 `grpc` 端点的 targetPort 为 8081，并把解析后的地址传入 ContentService 的 `Grpc:MediaAddress`，不再通过四个 `Kestrel__Endpoints__...` 环境变量配置服务器协议。

监听端口与 AppHost 的 targetPort 必须同步。服务连接、凭据、公开地址和其他进程的配置仍通过 Aspire 环境变量或连接引用传递；AppHost 无法直接调用独立服务进程的配置方法。
