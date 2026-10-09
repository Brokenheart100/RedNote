# MediaService 端点配置

MediaService 在 `Infrastructure/Hosting/MediaEndpointExtensions.cs` 中用 `ConfigureKestrel` 配置容器监听：

- 8080：HTTP/1，处理媒体 HTTP API。
- 8081：HTTP/2，处理容器网络内的明文 gRPC。

AppHost 在发布模式下设置 `Media__UseContainerEndpoints=true`，MediaService 据此启用这组监听。独立运行发布镜像时也需要提供此配置。开发模式继续使用 launchSettings/Aspire 的 HTTPS 端点，包括 Codespaces 和 Dev Container。

不要使用 `DOTNET_RUNNING_IN_CONTAINER` 来决定端点布局：开发容器也会设置它，但其中的 Aspire 仍按开发 HTTPS 端点编排服务。用该标记会导致服务实际监听端口与网关/gRPC 客户端地址不一致，上传请求返回 503。

AppHost 负责声明 `grpc` 端点的 targetPort 为 8081，并把解析后的地址传入 ContentService 的 `Grpc:MediaAddress`，不再通过四个 `Kestrel__Endpoints__...` 环境变量配置服务器协议。

监听端口与 AppHost 的 targetPort 必须同步。服务连接、凭据、公开地址和其他进程的配置仍通过 Aspire 环境变量或连接引用传递；AppHost 无法直接调用独立服务进程的配置方法。
