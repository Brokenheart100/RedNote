# GitHub Codespaces 开发环境

仓库使用 Aspire 官方开发容器 Feature、Docker-in-Docker、.NET 10、Node.js 24、PowerShell 和官方 SSH server Feature。SSH Feature 支持 `gh codespace ssh`、日志及文件传输，无需公开 SSH 端口。Actions 负责自动检查；Codespaces 使用 `.devcontainer/devcontainer.json` 初始化并运行开发环境，无需先等待 Actions 部署。

## 首次启动

1. 将本次修改提交并推送到 GitHub 的目标分支。
2. 仓库页面选择 **Code → Codespaces → 创建 Codespace**。配置请求 4 CPU、16 GB 内存、32 GB 存储，与本次账号可用的 `standardLinux32gb` 机型匹配，用于多个 .NET 服务及 PostgreSQL、OpenSearch、Redis、RabbitMQ、MinIO 和 Gorse；这不是性能容量保证，可根据实际负载调整。
3. 等待开发容器构建及初始化。`postCreateCommand` 恢复 .NET 依赖，生成开发密钥，并按共享 BFF、前台、管理端的顺序执行 `npm ci`。
4. `postStartCommand` 信任开发证书并执行原生 `aspire start`，后台启动 AppHost。数据库迁移由现有 Aspire 编排执行。
5. 在 **Ports** 打开 **8443** 的 HTTPS 转发地址。前台位于 `/`，管理端位于 `/admin/`。等待 Aspire Dashboard 中服务健康且两个 Nuxt 应用启动完成后使用。
6. **17286** 是 Aspire Dashboard，使用 CLI 输出的登录链接或其中的 token 登录。保留 Dashboard 身份验证。

已有 Codespace 在更新配置后需要执行 **Codespaces: Rebuild Container**。Codespace 停止再恢复时会重新启动 Aspire。若开发容器重建后原实例不再运行，可再次执行启动命令。

## 入口和登录

默认外部入口由 GitHub 提供的两个环境变量计算：

```text
https://${CODESPACE_NAME}-8443.${GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN}
```

AppHost 仅在 Codespaces 的运行模式下使用此地址作为 `gateway-public-url`、`frontend-public-url` 和 `media-public-url` 的默认值。它同时应用于 JWT Issuer、OIDC 登录/登出回调和图片签名 URL。服务间请求继续使用 Aspire 内部地址，不绕行 GitHub 的端口身份认证。网关的 AllowedHosts 保留内部主机并允许当前公开入口主机。

链路：

```text
浏览器 → Codespaces HTTPS 转发 → Gateway :8443
                                ├─ / → Nuxt 前台/BFF
                                ├─ /admin/ → Nuxt 管理端/BFF
                                ├─ OIDC / API → 各业务服务
                                └─ GET /rednote-media/images/** → MinIO
```

端口默认保持 Private，通过你的 GitHub 身份访问。无需为了日常开发将数据库、MinIO 管理接口或 Dashboard 改成 Public。Codespaces 不运行桌面 Edge 日志采集；Nuxt 服务端 OpenTelemetry 和业务服务日志仍进入 Dashboard。本地 Windows 的 Edge 日志配置继续生效。

新开发环境的数据库与本机独立，已有用户、管理员和帖子不会自动复制。普通用户可在前台注册；管理员沿用 `docs/administration.md` 的角色和 MFA 开通流程，不预置通用管理员密码。

UserService 的 EF 设计时工厂已改为读取 `ConnectionStrings__userdb`，避免迁移时错误使用旧的本机固定密码。离线生成迁移仍有不含密码的本地连接配置；执行数据库更新应由 Aspire 注入实际连接串。

MediaService 使用显式的 `Media:UseContainerEndpoints` 区分发布镜像的监听布局与开发 HTTPS 端点。开发容器中的 `DOTNET_RUNNING_IN_CONTAINER=true` 不代表正在运行发布镜像；误用该标记会使图片上传和 gRPC 连接到错误端口。详见 [媒体端点配置](media-endpoints.md)。

两个 Nuxt 应用的 `typescript.typeCheck` 使用 `build`，开发时由编辑器检查，构建时检查完整项目。GitHub Actions 仍分别执行 `npm run typecheck`，也可以在容器中手动执行，避免开发服务器额外启动两个持续占用内存的完整项目类型检查进程。

IdentityService 发布容器的 `HOME=/home/app` 与密钥卷一起在 Compose 服务回调中配置，避免把容器目录传给本机的 EF 迁移包生成进程。在 Linux 开发容器中，本机用户目录仍应为 `/home/vscode`。

## 常用命令

在仓库根目录：

```bash
bash scripts/start-codespaces.sh
aspire ps
aspire logs --help
aspire stop
```

若要关闭每次启动容器时的自动启动，可配置 Codespaces 环境变量 `REDNOTE_CODESPACES_AUTOSTART=false`，之后手动运行启动脚本。

开发密钥使用系统随机数生成，保存在容器当前用户的 .NET User Secrets 中，不写进仓库。也可以通过 Codespaces Secrets 提供以下名称，初始化脚本会将其更新到对应的 AppHost 参数（GitHub Secret 名称不能使用连字符）：

```text
REDNOTE_ADMIN_OIDC_SECRET
REDNOTE_ADMIN_SESSION_PASSWORD
REDNOTE_GORSE_API_KEY
REDNOTE_GORSE_DASHBOARD_PASSWORD
```

开发容器的 Docker 数据卷用于持久化该环境的数据。删除 Codespace 前按需导出数据；代码推送不会备份数据库、图片或密钥。

## MinIO 镜像

全新环境不能依赖本机缓存的 `minio/minio:latest`。该 Docker Hub 镜像在本次验证中返回 pull access denied；MinIO 社区版官方已经改为源码发行。因此 AppHost 通过 `infrastructure/minio/Dockerfile` 从官方 `RELEASE.2025-10-15T17-29-55Z` 源码构建，保留原来的 S3 接口、根账户参数和数据卷。首次启动需要下载 Go 依赖并编译，时间会比直接拉取旧镜像长。

这个改动解决镜像可获取性，并不表示停止维护的社区版已适合长期生产使用。正式部署前需要另行确定受维护的对象存储方案。[MinIO 官方发行说明](https://github.com/minio/minio#source-only-distribution)

## 边界与验证

Codespaces 是开发环境，停止或闲置超时后页面不再运行；这套配置不提供生产部署、高可用或数据备份。

本次验证（2026-10-09）：

- AppHost Release 编译通过，0 警告、0 错误；Shell 语法和 GitHub Actions 工作流检查通过。
- 使用官方 Dev Containers CLI 构建 Linux 开发容器，依赖初始化、证书信任和开发密钥初始化通过；重复初始化保留原有密钥。
- MinIO 源码镜像构建通过；Aspire 实际启动后，业务服务及基础设施达到健康状态，数据库迁移成功。
- 模拟 Codespaces 域名时，公开地址参数与 OIDC discovery 中的地址一致，前台 session 接口返回 200。
- 用户端 5 项 Playwright 回归已分阶段通过：真实 OIDC 登录、Redis 会话、图片上传和头像、gRPC 带图发帖、搜索与导航取消、评论/帖子删除、退出登录、幂等交互、输入校验，以及搜索历史的 25 次并发写入、20 条上限、持久化和双用户隔离。
- 管理端 1 项安全冒烟回归通过：错误 TOTP 拒绝、密码 + MFA 登录、OIDC/PKCE、三种角色权限、服务端 Redis 令牌、撤权后旧令牌拒绝、CSRF 和退出登录。此次未运行完整的审核、封禁和审计业务回归；测试管理员及其 MFA 文件在结束后清理。
- 用户端 ESLint、两个 Nuxt 应用的类型检查通过；24 项 Node 单元测试通过。
- Linux 中的 `aspire publish` 完整执行通过，Compose 文件及 7 个 EF 迁移包生成成功；确认媒体服务显式启用发布监听、ContentService 连接 `http://media-service:8081`、IdentityService 保留密钥卷。此次没有构建并启动整套发布镜像，不能把发布产物生成等同于 Docker 部署验证。
- 首次测试遇到 504 和内存不足；本地 Docker 虚拟机约 8 GB，低于配置请求的 16 GB。复测关闭未使用的资源，测试进程使用工作站 GC 和 Node 堆上限；这些限制仅用于本地验证，没有更改生产运行配置。搜索历史接口测试改用 Playwright 原生 HTTP 请求上下文，保留原有并发和隔离断言。
- 本地测试用假的 Codespaces 主机名和 localhost 开发证书，因此仅测试工具忽略该主机名的证书匹配；应用的 TLS 校验保持开启。该测试不能验证 GitHub 提供的真实证书和端口认证。

仓库配置以本地 Linux Dev Container 验证为基础；模拟 `CODESPACES` 环境变量能够检查内部编排和生成的公开 URL，但不能替代真实 GitHub 转发域名、GitHub 端口认证和浏览器回调的验证。真实 Codespace 创建后应检查首页、注册登录、管理端登录、带图发帖及图片访问。

官方依据：[Aspire Codespaces](https://aspire.dev/get-started/github-codespaces/)、[开发容器模板](https://github.com/microsoft/aspire-devcontainer/blob/main/.devcontainer/devcontainer.json)、[GitHub 环境变量](https://docs.github.com/en/codespaces/developing-in-a-codespace/default-environment-variables-for-your-codespace)、[端口转发](https://docs.github.com/en/codespaces/developing-in-a-codespace/forwarding-ports-in-your-codespace)。
