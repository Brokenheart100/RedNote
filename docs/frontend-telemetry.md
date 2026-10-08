# Nuxt 服务端遥测

Nuxt 的 SSR、BFF、登录和网关请求日志通过 OpenTelemetry SDK 的 OTLP gRPC 导出器发送到 Aspire Dashboard。`AddViteApp` 已注入 OTLP 地址、服务名称、实例标识和认证头，不需要在 AppHost 重复指定 Dashboard 地址。

Nitro 将 `@opentelemetry/*` 作为外部 Node 运行时依赖，OpenTelemetry API 通过 `createRequire(...).resolve` 明确解析到 Node 入口，避免打包器选择 `module` 条件中的 ESM 辅助代码而产生顶层 `this` 改写警告。生产构建仍需携带 `.output/server/node_modules`，正常发布完整 `.output` 目录即可。

打开 Dashboard 的「结构化日志」，选择 `frontend`。日志保留 emoji 消息，字段包括请求方法、路径、状态码、耗时和 requestId。点击日志中的 Trace ID，在「追踪」中查看 Nuxt BFF、YARP 和后端服务的关联跨度。

开发环境的消息同时附带格式化 JSON 字段，因此 API RESPONSE 的响应 body、网关调用耗时等也可在消息中查看。日志详情的 Attributes 仍保留独立字段，`body` 为 JSON；HTTP 摘要日志本身不包含响应体，应查看同一 Trace ID 下的 API RESPONSE。Production 消息保留摘要。深度超过 6 层或数组超过 50 项的内容有截断限制。

`00.telemetry.ts` 创建服务端请求跨度，读取传入的 W3C traceparent。`createTracedFetch(event)` 为 BFF 上游请求创建客户端跨度并注入追踪上下文。登录、注册、退出、CSRF、令牌刷新及业务网关调用使用该请求入口。OIDC 库内部请求和浏览器授权跳转目前没有自动插桩，不保证整个登录过程是一条连续追踪。

新服务端日志使用 `eventLogger(event).info/warn/error(message, fields)`；无请求上下文的后台日志使用 `serverLogger`。不要记录原始请求头、令牌或完整异常请求配置。公共日志入口递归脱敏密码、Cookie、授权信息、令牌、OIDC code/state 等字段，并保留控制台输出。

生产环境也导出请求摘要。详细认证/API 调试日志继续限于开发环境。没有配置 OTLP 地址时，不初始化导出器，业务功能仍可运行。Nitro 关闭时刷新并关闭遥测 SDK。

## 本地浏览器日志

AppHost 接入 `Aspire.Hosting.Browsers`，在用户端和管理端的 `AddViteApp` 调用链上使用 `WithBrowserLogs(browser: "msedge", userDataMode: BrowserUserDataMode.Isolated)`，分别创建 `frontend-browser-logs` 和 `admin-browser-logs` 子资源。指定 Microsoft Edge，浏览器状态保存在 Aspire 管理的本项目目录中。跟踪浏览器默认打开对应 Nuxt 开发端点；完整登录流程仍使用配置的公开 HTTPS 网关入口，用户端为 `https://localhost:8443/`，管理端为 `https://localhost:8443/admin/`。

1. 运行 `scripts/start-dev.ps1`，打开 Aspire Dashboard。
2. 找到用户端下面的 `frontend-browser-logs` 或管理端下面的 `admin-browser-logs`，执行 **Open tracked browser**。
3. 在打开的 Edge 中操作页面；验证登录时，在该跟踪窗口内访问公开网关地址或 `https://localhost:8443/admin/login`。
4. 在该子资源的「控制台日志」查看 Vue 的 console 输出、JavaScript 异常和网络事件；可执行 **Capture screenshot** 保存截图。

此前网关跟踪配置的本机验证中，默认 Edge 曾报 `Browser debug pipe closed`，改用 Playwright Chromium 后成功采集网络事件；旧的 `Aspire:Hosting:BrowserLogs:gateway:Browser` 配置不适用于现在的两个子资源。当前源码明确指定 Edge，本次只验证 AppHost 编译，未重新验证 Edge 会话启动。先等待网关和前端启动完成再打开页面，避免首次导航时出现连接失败。

普通浏览器窗口不会自动被采集。浏览器日志与上面的 Nuxt 服务端 OTLP 结构化日志是两个入口，也不保证网络事件包含完整请求/响应 body。调试时仍应避免在页面 console 输出密码、Cookie、令牌或管理员 MFA 密钥。

该 API 仍为实验性，项目固定包版本；浏览器日志用于本地 Aspire 调试，框架将这些子资源排除在发布清单之外，不需要把浏览器安装进业务容器。[官方文档](https://aspire.dev/integrations/devtools/browser-logs/)。

当前导出协议为 OTLP gRPC，独立部署时应设置匹配的 `OTEL_EXPORTER_OTLP_ENDPOINT`、`OTEL_SERVICE_NAME` 和必要的 `OTEL_EXPORTER_OTLP_HEADERS`。Docker 需要重新构建前端镜像才包含此代码，本次未重建或重启现有 Docker 部署。
