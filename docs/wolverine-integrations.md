# Wolverine 集成

当前 Wolverine 相关包统一为 **6.46.0**。迁移请求去重时，6.38.0 的 HTTP 管线测试发现：校验返回 400 后仍占用去重键，修正输入重试返回 409。因此升级框架，并以失败后重试和并发数据库写入测试验证行为。

## 媒体校验

HTTP 和 gRPC 批量请求使用同一份媒体 ID 规则。HTTP 集成返回 400 字段错误，gRPC 桥接返回 `InvalidArgument` 和字段详情。见 [请求校验](input-validation.md)。

## CSRF

普通用户注册、Cookie 登录、退出和管理员 MFA 登录统一使用 Wolverine `[ValidateAntiforgery]` 元数据，由 ASP.NET Core `UseAntiforgery()` 校验 Cookie 与请求头令牌。框架只记录验证结果，不会直接中断 JSON 端点，所以通过一处 `AntiforgeryResultMiddleware` 检查 `IAntiforgeryValidationFeature`，无效或缺失令牌返回 400。未自行实现令牌生成或加密。OpenIddict 协议端点仍由其自身处理。

## EF Core 事务与 Outbox

Content、User 使用 `AddDbContextWithWolverineIntegration` 合并 DbContext 注册和 Wolverine EF 集成。发帖、创建评论、用户资料初始化/更新及 Content 用户资料投影改用 `[Transactional]`，删除这些路径的手动提交代码。创建帖子/评论通过返回值发送级联事件，用户资料通过事务内的 `IMessageBus` 发布。

评论写入仍保留 `SELECT ... FOR UPDATE`，事务覆盖检查、评论插入、统计版本更新和事件持久化。点赞、删除、管理等其余路径仍使用已有显式事务与 Outbox；没有全局开启 `AutoApplyTransactions`，避免嵌套事务。媒体文件存储不参与数据库事务，补偿删除机制保留。

## 发帖与评论幂等响应

- 两个创建端点使用 `[DeduplicatedWithResponse(DeduplicationScope.User | DeduplicationScope.Endpoint, Required = false)]`，启用 `EnableDeduplicatedResponses`，删除手写键哈希中间件。旧客户端不传键时仍可提交，但没有幂等保护。
- 前端同一页面内、内容未变化的失败重试继续使用原 `Idempotency-Key`；编辑内容或确认成功后使用新键。BFF 转发该键，并保留 1–128 字符输入限制。
- JWT 的 `NameClaimType` 使用稳定的 `sub`，避免 Wolverine 默认优先使用 `Identity.Name` 时将同名用户放入同一隔离范围；显示昵称仍读取 `name`。
- 首次成功返回 201；同键、相同请求重放原来的状态码、JSON 正文和 Location。前端现有的顶层 `id` 等字段保持不变。
- 同键改动请求字节返回 422；首次请求仍在处理时返回 409。前端遇到 409 保留输入和原键，供稍后重试。
- 字段校验失败、媒体不存在、父评论无效、提交异常等失败释放键；并发同键最多创建一条业务记录。
- PostgreSQL 使用框架 `wolverine_deduplicated_responses` 表，默认保留 24 小时。响应记录在业务提交后单独写入；进程在两次写入之间终止时，重试可能一直返回 409，直至保留窗口结束并被清理，不会在窗口内再次执行。
- 重试须发送相同字节，重新排序 JSON 属性或修改空白也会改变指纹。此功能不用于 multipart 上传。

## 异常响应

ServiceDefaults 注册原生 `AddProblemDetails`，五个业务服务及 Gateway 使用 ASP.NET Core 异常处理中间件。未预期异常返回通用 500 ProblemDetails 与 `traceId`；Content/User 的 EF 乐观并发异常返回 409，User 中缺失或非法 subject 返回 401。客户端不接收异常堆栈和内部数据库细节，服务端日志与追踪保留。

该中间件位于 Wolverine 事务外层，事务先回滚或完成清理，再转换异常。现有字段校验和业务错误契约保留，gRPC 使用已有错误详情桥接。
## Alba

点赞、取消点赞、更新帖子、删除帖子和删除评论统一使用 `[Transactional]` 的 Eager 事务及 `IMessageBus`，删除手动 BeginTransaction/Outbox 提交代码。数据库行锁仍在事务内；状态写入和消息由 Wolverine 一起提交。更新帖子保留一次事务内 SaveChanges，让响应查询读取最新标签；后续依赖调用失败仍回滚。并发与删除测试已改为通过 Alba HTTP 调用，以执行真实的框架事务中间件。

测试项目引入 Alba 8.5.3。输入校验、CSRF、事务与投影、异常响应、Media HTTP/gRPC 和 Content 幂等响应均通过内存 HTTP 主机运行。Content 去重测试使用真实端点与独立 PostgreSQL 数据库，检查重复、并发、失败重试、用户/接口隔离和旧客户端兼容。

运行完整后端测试：`pwsh -File scripts/test-backend.ps1`。前端检查：在 `Red-Book` 运行 `npm run check`。真实 BFF 请求去重另有 Playwright 端到端测试。

## Aspire 诊断

AppHost 使用 JasperFx.Aspire 2.69.3，在本地运行模式给五个业务服务添加 `Check environment`、`Describe`、`Preview generated code` 命令。

服务通过 `RunJasperFxCommands(args)` 运行，正常启动仍运行服务。诊断命令跳过 OpenIddict 客户端播种、MinIO 桶创建和 OpenSearch 索引初始化，避免执行这些启动写操作。

诊断按钮使用目标服务的 Aspire 环境，输出进入该服务日志。没有启用修改资源或重建投影的命令。此集成用于本地 Dashboard，Docker Compose 不提供这些按钮。

## 本次验证

- 完整后端 87 项测试通过，包含 CSRF 正反向验证、幂等响应正文与 Location 重放、用户/路径隔离、请求指纹、并发计数、提交失败后的键释放、用户资料/事件回滚及投影版本保护。
- 前端 lint、类型检查和 22 项单元测试通过。
- 通过 `scripts/start-docker.ps1` 实际重建并部署 Docker，镜像标签 `aspire-deploy-20261007152535`；五个数据库迁移任务均以 0 退出，常驻容器运行且重启次数为 0。
- Docker 用户端 5 项 Playwright 测试通过，包含 BFF 原响应重放与 422 指纹检查、搜索历史、输入校验、登录/上传/gRPC/用户资料/搜索/评论/删除/退出及退出期间的状态刷新。
- `scripts/test-admin.ps1 -Docker` 的 1 项管理端综合测试通过，覆盖 MFA、权限隔离、CSRF、审核、搜索、用户限制、审计及退出。脚本清理自身创建的管理测试账号和登记文件。
- 入口：前端 `http://localhost:3000`，管理端 `http://localhost:8080/admin`。本轮运行使用本地 Docker HTTP 配置；生产 TLS 未在本轮验证。
- 日志：`artifacts/wolverine-simplify-backend-tests.log`、`artifacts/wolverine-simplify-frontend-check.log`、`artifacts/wolverine-simplify-docker-deploy.log`、`artifacts/wolverine-simplify-docker-e2e.log`、`artifacts/wolverine-simplify-admin-e2e.log`。
