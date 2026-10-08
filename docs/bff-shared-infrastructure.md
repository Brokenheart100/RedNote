# BFF 共享基础模块

用户端和管理端通过本地 npm 包 `@rednote/bff-infrastructure` 共用请求追踪和 Identity CSRF Cookie 处理。源码在 `RedNote.Bff.Shared/src`；两个应用的 `server/utils` 文件仅重新导出，现有调用路径保持兼容。

包使用 TypeScript 编译为 JavaScript 和类型声明；不直接从 `node_modules` 执行 TypeScript。OpenTelemetry、h3 和 ofetch 是 peer dependencies，运行时使用各应用自己的实例，避免破坏追踪上下文。两个应用仍有独立会话、Cookie、权限、Redis 命名空间及遥测服务名称。

## 开发

`aspire run` 会先运行 `bff-shared-build`（`npm ci`，包含 prepare 编译），两个前端的 npm 安装资源等待它成功完成，然后启动应用。此构建资源只用于开发，不发布为服务。

独立启动 Nuxt 时，先在共享包运行 `npm ci`，再在目标应用运行 `npm ci` 和 `npm run dev`。修改共享源码后，运行共享包的 `npm run build`，再在两个应用运行 `npm ci`，更新本地依赖副本。`.npmrc` 的 `install-links=true` 使 file 依赖按普通包安装，依赖解析使用应用的 node_modules。

## Docker

`Dockerfile.nuxt` 供两个应用共用，通过 `APP` 选择应用目录。先编译共享包，再安装并构建目标 Nuxt 应用；运行镜像仅包含该应用的 `.output`，以 node 用户运行。

构建上下文为仓库根目录，`Dockerfile.nuxt.dockerignore` 使用允许列表限制为两个前端及共享包，同时排除依赖、构建输出、环境文件和测试产物。后端源码、artifacts 和部署密钥不进入上下文。

AppHost 使用 Aspire 的公开 DockerfileBuildAnnotation 替换自动生成的 Dockerfile 配置，并保留已有镜像构建流水线。Aspire 13.5.4 再次调用 WithDockerfile 会触发重复流水线注解错误，因此只替换构建配置；部署和镜像发布仍交给 Aspire。

回归入口：用户端 `npm run check` 包含共享 CSRF 多 Cookie、缺失 token/Cookie、已有 Cookie 回退及并发请求追踪测试；管理端运行 `npm run typecheck`。Docker 验证使用原有 `scripts/start-docker.ps1` 和用户/管理端 E2E。
