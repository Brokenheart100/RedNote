# 本次按优先级清理

1. 管理审核和用户限制请求使用现有 FluentValidation/Wolverine HTTP 字段校验，移除手写的原因、版本和期限校验。权限、动作白名单、资源状态、所有权和版本冲突继续由业务逻辑处理。用户限制期限转换为 UTC 后持久化。
2. 点赞、取消点赞、更新帖子、删除帖子和删除评论使用 Wolverine `[Transactional]` 和 `IMessageBus`，移除显式创建事务、IDbContextOutbox 及手动提交。Eager 事务保证数据库行锁生效；更新帖子保留事务内 SaveChanges 供响应查询读取最新标签，响应依赖失败时仍回滚。
3. 删除两个业务服务的旧内部审计查询 API。统一通过 AdminService 查询审计；原始表、审计事件、Outbox 和历史导入继续工作。
4. 用户和管理 BFF 共用 `RedNote.Bff.Shared` 的请求追踪和 CSRF 模块，使用标准 npm 本地依赖、TypeScript 编译和 peer dependencies。两个应用仍有独立的认证会话和权限。Docker 共用一个构建文件；详情见 [BFF 共享基础模块](bff-shared-infrastructure.md)。

AdminBusinessClient 继续保留：它处理管理应用的授权调用、内部 JSON 成功结果到对外 204 的转换，以及上游错误和幂等键。直接代理请求不能自动替代这些应用层职责。本次没有再引入一套代理或校验框架。

## 验证证据

- `artifacts/redundancy-backend-tests.log`：109/109，覆盖字段错误、空请求体、过长原因、非法动作、带时区期限、并发点赞/取消、删除权限、更新标签及保存后的回滚。
- `artifacts/redundancy-frontend-check.log`：lint、类型检查、24/24 单元测试；包含共享 CSRF Cookie 和并发追踪。`redundancy-frontend-lint.log` 检查新增 E2E 代码。
- `artifacts/redundancy-admin-check.log`：管理端类型检查。
- `artifacts/redundancy-shared-cold-install.log`：先构建共享包再安装消费者的冷安装通过。
- `artifacts/redundancy-docker-deploy.log`：实际部署镜像 `aspire-deploy-20261008060916`，六个数据库迁移任务退出 0；常驻应用容器运行、重启次数 0，健康检查返回 200。
- `artifacts/redundancy-admin-e2e.log`：MFA、权限隔离、CSRF、审核、搜索、限制、集中审计和退出的完整流程通过。
- `artifacts/redundancy-docker-e2e-initial.log`：既有用户端四项通过。新增断言误用不存在的 BFF 详情/编辑路径导致另一项失败，已改用现有网关详情接口；`redundancy-docker-e2e.log` 单项复验通过，包含真实 BFF 点赞、重复点赞和取消点赞。帖子更新由 Alba HTTP 集成测试验证，当前前端没有帖子详情/编辑 BFF API。
- 测试后审计记录 ID 完全一致：Content 原始/投影 52/52，User 18/18；本轮临时管理员数量 0，原有 90 篇带图测试帖子仍可搜索。

本次使用已有库简化职责重复的代码，没有更换 ORM、消息总线或认证框架。测试和部署结果对应本机 LocalDocker 环境。
