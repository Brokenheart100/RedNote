# GitHub Actions 持续集成

项目使用 `.github/workflows/backend.yml` 与 `.github/workflows/frontend.yml`。相关文件 push、创建或更新 PR 时触发；合并配置到默认分支后，也可在 GitHub 的 Actions 页面通过 Run workflow 手动执行。

## 后端

Ubuntu 24.04 Runner 使用 `global.json` 安装 .NET SDK，恢复并以 Release 编译整个解决方案，包括 AppHost、管理服务和推荐服务。随后复用已有的 `scripts/test-backend.ps1`，以 `-NoBuild -Configuration Release` 测试同一份构建结果。

测试脚本创建独立 PostgreSQL、OpenSearch、Redis 和 Gorse 容器，生成临时测试密码，并在 finally 清理本次资源。它不会使用本地 Aspire、用户数据或部署密钥。脚本支持 `-ResultsDirectory`，CI 将 TRX 报告上传为 `backend-test-results`，保留七天。

触发路径包含业务服务、ServiceDefaults、测试、Gorse 镜像配置、全局 SDK、NuGet/构建配置和测试脚本。

本地对应命令：

```powershell
dotnet restore RedNote.slnx
dotnet build RedNote.slnx --configuration Release --no-restore
./scripts/test-backend.ps1 -NoBuild -Configuration Release -ResultsDirectory artifacts/backend-test-results
```

## 用户端与管理端

两项独立矩阵任务分别验证 `Red-Book` 与 `RedNote.Admin`。每项任务先在 `RedNote.Bff.Shared` 执行 `npm ci`，通过包的 prepare 生命周期编译共享 BFF，然后安装应用依赖。

- 用户端：Lint、类型检查、单元测试、生产构建、Chromium 冒烟测试。
- 管理端：类型检查、生产构建。

用户端浏览器测试使用 `tests/e2e/mock-gateway.mjs`，验证构建后的 Nuxt/BFF 行为。它不启动完整业务系统，不等同于真实 OIDC、管理端 MFA 或 Docker 部署端到端测试。失败时上传 Playwright 报告、截图及 trace，保留七天。

共享 BFF、用户端或管理端变化均触发两项任务。Node 使用 24，缓存键包含共享包及相应应用的 package-lock.json；依赖安装使用 npm ci。

## 权限与运行边界

工作流仅授予 contents: read，不部署、不发布镜像。GitHub 官方 Actions 使用已核对的发行版提交 SHA 固定。相同分支的新提交取消旧检查，前端矩阵某一项失败不会取消另一项。

目前不需要配置生产 Secrets。这里的会话密码和 Gorse API key 仅用于隔离测试。自动镜像发布、部署、真实后端浏览器测试与分支保护规则需另行配置。

GitHub 只会检查已推送的代码。新增项目、共享包、锁文件、测试 fixture 和 Gorse 配置必须一起提交；本地未提交文件不会出现在 Runner 上。工作流的实际托管运行结果应以仓库 Actions 页面为准。

## 本次验证

2026-10-09：两份工作流通过 actionlint 1.7.12；本地 Windows 完整解决方案 Release 编译零警告、零错误，使用上述测试参数完成 123/123 后端回归并生成 TRX，临时测试容器已清理。

前端使用独立 Linux Node 24 容器，复制源文件并排除现有 node_modules、dist、.nuxt 和 .output，按工作流顺序重新 npm ci。共享 BFF 编译、两套前端类型检查和生产构建通过；用户端 Lint、24/24 单元测试、5/5 Chromium 冒烟测试通过。未在 GitHub 托管 Runner 上触发实际运行，未执行生产部署。
