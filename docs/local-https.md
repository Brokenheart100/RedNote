# 本地 HTTPS 开发

GitHub Codespaces 使用 [Codespaces 配置](codespaces.md)，公开入口由 AppHost 自动适配转发地址。

首次配置开发证书：

```powershell
dotnet dev-certs https --trust
```

启动：

```powershell
./scripts/start-dev.ps1
```

也可以直接执行 `aspire run --apphost RedNote.AppHost/RedNote.AppHost.csproj`。

浏览器统一访问 `https://localhost:8443`。YARP 转发页面和 BFF 请求到内部 HTTP Nuxt 服务，转发后端 API 和 OIDC 请求到业务服务。JWT Issuer、认证入口、登录页面和回调默认使用该 HTTPS origin。Nuxt Session Cookie 标记为 Secure。

Nuxt 使用 `NODE_EXTRA_CA_CERTS` 信任 .NET 开发证书，开发插件通过 Node.js 24 的 `setDefaultCACertificates` 将信任应用到 Nitro 工作线程。AppHost 从当前用户证书存储导出公钥证书到 `artifacts/certificates/localhost.pem`；不导出私钥，不关闭 TLS 校验。证书更新后重启 Aspire。

开发时图片签名 URL 也使用该 HTTPS origin。YARP 只转发 GET `/rednote-media/images/**` 到 MinIO，并保留原始 Host，使 MinIO 可以验证签名。MinIO 写入和管理接口不通过此路由公开。

本地开发使用 8443 和动态内部端口，可以与使用 8080/3000/9000 的 Docker 部署并行。容器发布默认配置仍为原来的本地 HTTP 配置；本次没有为 Docker 配置 TLS，也没有导出容器用的私钥。

## HTTPS 回归

等待 Aspire 所有业务服务为 Healthy、Nuxt 启动完成后，在 `Red-Book` 目录执行：

```powershell
$env:REDNOTE_FRONTEND_URL = 'https://localhost:8443'
$env:REDNOTE_GATEWAY_URL = 'https://localhost:8443'
$env:REDNOTE_MEDIA_URL = 'https://localhost:8443'
$env:NODE_EXTRA_CA_CERTS = (Resolve-Path ../artifacts/certificates/localhost.pem).Path
npm run test:docker
```

同一套浏览器业务测试可以验证 Docker HTTP 或 Aspire HTTPS；HTTPS 运行时还验证身份 Cookie 的 Secure 属性和发现文档的 HTTPS Issuer。
