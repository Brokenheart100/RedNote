# 管理后台第一版

## 运行入口

开发环境：`https://localhost:8443/admin/login`。

本地 Docker：`http://localhost:8080/admin/login`。这是本机 HTTP 测试配置；公网部署使用 HTTPS，并保留安全 Cookie。

后台是独立 Nuxt 4 应用 `RedNote.Admin`，由 Gateway 的 `/admin/` 路由转发，Docker 中不公开后台容器端口。因此后台不要求新增域名。用户前端和后台各自有 BFF，浏览器不保存访问令牌。

链路：浏览器 → YARP `/admin/` → Admin Nuxt BFF → YARP `/api/v1/admin/*` → AdminService → ContentService / UserService。IdentityService 负责管理员密码、TOTP、多因素认证与 OIDC 令牌。AdminService 通过 Aspire 服务发现访问业务服务，不连接它们的数据库。

`RedNote.Admin` 是界面和浏览器 BFF；`RedNote.AdminService` 是 .NET 管理业务入口。后者独立部署，拥有 `admindb`，第一版负责管理接口协调与集中审计投影。帖子和评论的状态、用户限制、版本检查和原始审计仍属于业务服务。内部接口使用 `/internal/admin/*`，继续检查管理员权限；Gateway 拒绝公开访问 `/internal/*`。

常规数据库查询和实体写入使用 EF Core。幂等审计插入及行锁的参数化 SQL 封装在各服务 DbContext 的 `InsertAuditIfAbsentAsync`、`LockPostForWriteAsync`、`LockProfileForWriteAsync` 中；业务处理代码只调用这些方法。行锁方法要求已经开启事务，锁一直保留到事务结束，提交继续由原有事务或 Wolverine 管理。

## 创建第一个管理员

1. 运行 `scripts/start-dev.ps1`（或 `scripts/start-docker.ps1`）。启动脚本会初始化两个随机后台密钥，存入 AppHost 的本机 User Secrets；不会覆盖现有值。
2. 在项目根目录运行：

```powershell
./scripts/provision-admin.ps1 -Email '你的管理邮箱' -Roles ContentModerator,UserAdministrator,AuditReader
```

脚本交互式读取密码；密码不会放进命令行参数或 Git。它通过当前 Aspire 的数据库配置创建新账号，不会覆盖已有用户，也没有默认管理员密码。

Docker 环境先找 Identity 容器名，再运行：

```powershell
docker ps --format '{{.Names}}'
./scripts/provision-admin.ps1 -Email '你的管理邮箱' -Roles ContentModerator,UserAdministrator,AuditReader -DockerContainer 'Identity容器名'
```

3. 脚本返回受保护的 `artifacts/admin-enrollment-*.json` 文件路径。将其中的 `AuthenticatorUri` 导入支持 TOTP 的身份验证器。此文件含 MFA 密钥，导入后删除，不要提交或共享。
4. 通过后台登录页输入邮箱、密码及六位验证码。

可用角色：

| 角色 | 授予的权限 |
| --- | --- |
| ContentModerator | 查看帖子/评论、下架/恢复 |
| UserAdministrator | 查询用户、限制/恢复发布和评论资格 |
| AuditReader | 查询内容和用户操作审计 |

角色可以组合。第一版没有动态角色编辑器；不要把所有员工都授予全部角色。

## 会话与权限

- 独立 confidential OIDC 客户端 `rednote-admin`，PKCE 授权码流程；普通用户客户端不能申请后台 scope。
- `rednote-admin-session` Cookie 使用 `/admin/` 路径、HttpOnly、SameSite=Lax；开发 HTTPS 使用 Secure，LocalDocker HTTP 才关闭 Secure。
- OIDC 的临时状态 Cookie 也使用后台专属名称，避免用户和后台登录流程互相覆盖。
- Redis `rednote:admin:session:*` 保存令牌；后台和普通前端使用不同会话密钥、名称与数据命名空间。
- 访问令牌 5 分钟；后台会话 30 分钟无操作失效、最多 8 小时。MFA 超过 8 小时必须重新登录。
- BFF 每次请求重新确认权限；业务服务也向 Identity 确认数据库当前角色、锁定状态、MFA 和安全版本。撤销角色后旧令牌不能继续执行对应管理操作。身份服务不可达时拒绝操作。
- 写操作必须匹配 Origin 和后台会话的 CSRF 值；后台登录受网关限流、密码和验证码失败受 Identity 锁定策略约束。
- 退出删除 Redis/BFF 会话并退出 Identity 的 SSO Cookie。用户和后台共享 Identity SSO，退出后台也会清除浏览器的 Identity SSO，但不会删除用户前端已经建立的独立 BFF 会话。
- 密钥轮换会使已有后台 Cookie 失效；OIDC 客户端密钥应与 Identity/BFF 同时更新。生产部署应将密钥交给部署平台密钥存储，而非写入源码。

## 管理操作

帖子和评论的 `IsHidden` 与作者删除状态分离。管理员可以恢复审核下架的内容，不能恢复作者已删除的内容。公共列表、详情、互动和评论查询排除隐藏内容。

每次处理提交处理原因和当前 `Revision`。业务服务在事务中锁定关联帖子/用户，过期版本返回 409；页面提示刷新，避免同时操作覆盖。隐藏顶层评论会隐藏其回复；恢复顶层评论不会恢复被单独下架的回复。

内容变更、审计和 Outbox 在同一数据库事务提交。搜索通过独立版本的 `PostVisibilityChanged` 处理下架和恢复；消息乱序不会覆盖新状态，永久删除墓碑不会因恢复事件重新出现。搜索同步是最终一致性，可能短暂滞后。

管理写请求从 Nuxt BFF 经 AdminService 向业务服务传递 `Idempotency-Key`。实际业务端点使用 Wolverine `[Transactional]` 与 `[DeduplicatedWithResponse]`；同一管理员、同一端点、同一请求的重试返回原结果，不重复修改或审计，修改请求后复用键返回 422。内部接口使用可重放的 JSON 结果，AdminService 对外保持原来的 204 成功响应。AdminService 取消继承的自动 HTTP 重试，超时不会盲目重发写操作。

用户限制支持发布、评论、到期时间及解除。ContentService 在执行新增帖子/评论前确认 UserService 的限制，无法确认时返回 503。到期后限制自动失效。此功能不封禁登录，也不阻止用户删除自己的内容。

审核和限制请求的字段校验统一使用现有 FluentValidation/Wolverine HTTP 中间件，返回含字段 errors 的 400 ProblemDetails。权限、操作动作、资源状态和版本冲突仍由业务端点检查。用户限制到期时间在持久化前转换为 UTC。

内容审核和用户限制都在业务服务本地事务中同时保存状态、原始审计和 `AdminAuditRecorded` Outbox 消息。消息经过 `admin-audit-events` RabbitMQ 交换机，由 AdminService 的 Durable Inbox 消费。`admindb` 按 `(Source, Id)` 唯一键插入审计投影，重复投递和历史导入不会覆盖原记录。集中审计最终一致，可能短暂延迟；消息未处理时由 Wolverine 重试。

审计页默认查看全部来源，也可按来源、操作者、目标、动作和时间查询；记录处理原因、变更结果、时间及 TraceId。`/api/v1/admin/audit` 查询全部来源，旧 `content-audit`、`user-audit` 路径保留兼容并改为查询 `admindb`。数据库索引支持时间/操作者/目标查询；对外没有审计修改/删除接口。第一版不包含审计外部不可变归档或失败登录审计报表。

已有 Docker 数据可运行 `scripts/backfill-admin-audit.ps1` 回填。该受控维护脚本只读导出业务库原始记录，再调用 AdminService 的显式导入命令。导入事务失败整体回滚；重复运行安全。原始表和原始记录继续保留，运行服务不会获得跨库连接。导出文件位于 Git 忽略的 `artifacts`，按本地敏感数据管理。

ContentService 的 `/internal/admin/content-audit` 和 UserService 的 `/internal/admin/user-audit` 查询接口已移除，统一查询 AdminService 的审计投影；原始审计表、消息发布和历史回填继续保留。

## 验证

后端回归：`scripts/test-backend.ps1`，使用独立 PostgreSQL/OpenSearch 容器。

后台端到端测试：运行 `scripts/test-admin.ps1`；Docker 使用 `scripts/test-admin.ps1 -Docker`。脚本自动创建三个测试专用 MFA 管理员，执行 `Red-Book/playwright.admin.config.ts`，结束后删除这次创建的管理员及本地凭据文件。测试覆盖 MFA、角色隔离、CSRF、旧令牌角色撤销、8 个并发请求刷新会话、审核并发、幂等重放、搜索同步、用户限制（包括路径大小写及尾斜杠）、集中审计、带时区的日期筛选、审计页面筛选及退出。不要用员工的实际账号运行测试。

2026-10-08 验证结果：SQL 封装重构后，后端回归 96/96 通过；后台类型检查和用户前端 lint 通过；重新构建并实际启动 Docker（镜像标签 `aspire-deploy-20261008052451`），六个迁移任务全部退出 0；Gateway 和六个业务服务的健康端点均返回 200；后台完整端到端测试通过，用户端 5/5 回归通过。历史审计首次回填 40 条成功，重构后重复回填 54 条没有重复入库。测试后的原始审计与集中投影按记录 ID 比对一致（内容 46 条、用户 16 条），审计队列无待处理消息。测试管理员及本地测试凭据已清理，原有 90 篇带图测试帖子仍可搜索到。测试结果对应本机 LocalDocker 环境，不代表生产环境已部署或完成负载测试。

2026-10-08 后续去冗余验证：管理字段校验及五个帖子事务端点重构后，后端 109/109 通过；用户端 lint、类型检查及 24/24 单元测试通过，管理端类型检查通过。Docker 实际更新到 `aspire-deploy-20261008060916`，六个迁移任务退出 0、Gateway 和六个业务服务健康检查返回 200。管理端综合 E2E 通过；用户端既有四项通过，扩展的幂等/点赞测试修正不存在的 BFF 详情路径后单项复验通过。原始审计与集中投影 ID 一致（内容 52 条、用户 18 条）；本轮三个临时管理员和凭据文件已清理，90 篇带图测试帖子保留。构建途中 Docker Engine 曾无响应，恢复后依次预构建两个前端并完成部署。

## 第一版边界

没有永久删除、批量操作、导出、动态权限配置、登录封禁、MFA 自助重置或恢复码管理。MFA 初次导入由受控初始化完成，遗失身份验证器需由系统维护者按身份核验流程处理。

已签发的媒体链接不会因帖子下架立即失效；如果要求违规媒体立即不可访问，需要额外的媒体撤销和缓存清理机制。第一版不能据此宣称满足特定合规认证。
