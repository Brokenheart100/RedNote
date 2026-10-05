# RedNote 前端

运行环境：Node.js 24。该项目使用 Nuxt 页面与 Nitro BFF，业务 API 由 Gateway 提供。

## 职责边界

- `app/pages`：路由、查询与页面编排。
- `app/components`：展示与交互组件。
- `app/composables`：认证、用户资料、发布和评论等业务逻辑。
- `app/stores`：跨页面共享的笔记实体与互动状态。
- `server/api`：HTTP 请求校验和业务入口。
- `server/utils/gateway-fetch.ts`：认证、上游请求、超时和错误转换。
- `shared/schemas`：输入数据的运行时校验。
- `shared/types`：共享 DTO。

SSR 内部查询使用 `useFetch` 或 `useRequestFetch` 转发当前请求的 Cookie。
异步请求在页面切换、退出和状态重置时取消，并禁止旧结果回写。

## 开发

```powershell
npm ci
npm run dev
```

完整系统建议从根目录的 Aspire AppHost 启动。单独运行前端需配置：

| 环境变量 | 用途 |
| --- | --- |
| NUXT_GATEWAY_BASE_URL | Nuxt 服务端可访问的 Gateway 地址 |
| NUXT_SESSION_PASSWORD | 至少 32 字符的会话加密密码 |
| REDIS_URI | Redis 连接 URI；也支持 Aspire 的 ConnectionStrings__redis |
| NUXT_OAUTH_OIDC_CLIENT_ID | OIDC 客户端，通常为 rednote-web |
| NUXT_OAUTH_OIDC_OPENID_CONFIG | OIDC discovery 地址 |
| NUXT_OAUTH_OIDC_REDIRECT_URL | 浏览器可访问的 /auth/rednote 回调地址 |

生产环境通过 HTTPS 访问；Nuxt Session Cookie 在生产构建中启用 Secure。
Identity Cookie 名称为 RedNote.Identity，开发 HTTP 与生产 HTTPS 的策略分别配置。
前端与身份授权入口优先使用同一个公网域名。

## Token 存储与刷新

浏览器会话保存最小登录信息；OAuth Token 仅存储在 Redis。
所有前端实例必须连接同一个认证 Redis。

刷新使用 Redis 锁，并在锁内重新读取 Token。写回要求锁仍属于当前请求且原 Token 未变化。
退出删除 Token 后，仍在执行的刷新请求不能重新创建它。
上游刷新失败为 400/401 时清理原 Token；临时网络故障保留凭据。
Token 存储键沿用 rednote:auth-tokens:session:<session-id>。

## 自动检查

```powershell
npm run lint
npm run typecheck
npm test
npm run build
```

单元测试使用 Node 原生测试运行器，覆盖刷新并发、退出竞态、锁过期、校验和错误边界。

默认浏览器测试自动启动模拟 Gateway 和 Nuxt，验证 SSR 搜索、评论弹窗、输入校验及未登录访问：

```powershell
npx playwright install chromium
npm run test:e2e
```

验证生产构建：

```powershell
npm run build
$env:REDNOTE_E2E_PREVIEW = '1'
npm run test:e2e
```

真实登录与退出测试需要先启动完整系统，并配置可用的 OIDC 和 Redis：

```powershell
$env:REDNOTE_LIVE_E2E = '1'
npm run test:e2e
```

真实认证测试仍要求本地前端入口 http://localhost:3000。
刷新竞态通过单元测试主动制造，不依赖手工修改生产代码的有效期。

CI 位于仓库根目录的 `.github/workflows/frontend.yml`，依次执行 lint、类型检查、单元测试、构建和生产模式浏览器测试。
模拟 Gateway 测试不能替代完整系统的 OIDC、Redis 和数据库集成测试。