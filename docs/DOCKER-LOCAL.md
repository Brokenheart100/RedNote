# 本地 Docker 运行

在仓库根目录执行，需要 Docker Desktop（Linux 容器）、.NET 10 SDK 和 Aspire CLI。
前端镜像在 Docker 内使用 Node 24 构建；运行浏览器测试还需要本机 Node 24、npm 依赖和 Playwright Chromium。

```powershell
./scripts/start-docker.ps1
```

脚本通过当前 AppHost 生成 Compose、构建及标记镜像、执行迁移并启动容器，显式覆盖旧部署缓存中的公网地址。
生成的 Compose 和环境文件位于 `RedNote.AppHost/aspire-output`，不要手改或提交其中的密钥。
Aspire 的部署环境名称为 Production；`LocalDocker=true` 是本项目显式的本地 HTTP 配置开关。
业务容器仍使用 Production 环境运行。

| 用途 | 地址 |
| --- | --- |
| 前端 | http://localhost:3000 |
| 网关与公开 OIDC issuer | http://localhost:8080 |
| MinIO 图片下载 | http://localhost:9000 |
| MinIO 管理界面 | http://localhost:9001 |

容器间使用服务名称通信，Content → Media 的 gRPC 使用内部 HTTP/2 端口 8081。
OIDC 浏览器跳转使用公开地址，BFF 的 token 请求和后端的 JWKS 获取使用内部网关。
公网媒体地址在签名前配置；不会替换已经签名的 URL。
PostgreSQL 和 OpenSearch 健康检查通过后再启动依赖任务；四个 EF 迁移和搜索数据库初始化任务退出码为 0 是正常状态。

```powershell
./scripts/docker-local.ps1 ps
./scripts/docker-local.ps1 logs gateway
./scripts/docker-local.ps1 stop
./scripts/docker-local.ps1 start
```

这些脚本识别 Aspire 实际使用的 Compose 项目名称，停止和再次启动会保留数据卷。
PostgreSQL、Redis、RabbitMQ、MinIO、OpenSearch 和 Identity 的证书、Data Protection 密钥均持久化。
重建镜像不会删除数据；勿用 `docker compose down -v` 清空已有数据。

业务验证：

```powershell
cd Red-Book
npm run test:docker
```

测试会创建测试账号和带图片的帖子，验证真实 OIDC 登录、Redis 会话、上传、gRPC 组装帖子、签名图片下载、OpenSearch 索引和退出。
不使用 mock，也不包含浏览器 token。

本地 HTTP 配置允许非 Secure Cookie，并使用现有开发证书和本地 MinIO 默认凭据。
公网生产部署应使用 HTTPS、Secure Cookie、独立秘密配置、正式签名证书和明确的受信任代理。
部署目录和 Aspire 保存的参数需保留，避免已有 PostgreSQL 卷与新生成的密码不匹配。
