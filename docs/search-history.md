# 搜索历史

SearchService 的 PostgreSQL 数据库 `searchdb` 同时保存两类数据：

- EF Core 管理 `search.SearchHistory`，存储用户搜索历史。
- Wolverine 管理自身的消息持久化表，全文索引仍保存在 OpenSearch。

AppHost 使用 `search-migrations` 执行 EF 迁移，并让 SearchService 等待迁移成功；原来的 `search-database-init` 已移除。迁移可以在有创建数据库权限的账号下创建缺失的 `searchdb`。正式环境若限制服务账号权限，应先由运维创建数据库，再让迁移账号执行迁移。不要使用 `EnsureCreated` 代替迁移。

## 接口

以下接口均要求有效登录身份，从 JWT 的 `sub` 取得用户 ID，不接受调用方指定其他用户。

| 操作 | 网关接口 | Nuxt BFF 接口 |
| --- | --- | --- |
| 最近 20 条历史 | GET `/api/v1/search/history` | GET `/api/search/history` |
| 记录关键词 | POST `/api/v1/search/history` | POST `/api/search/history` |
| 删除一条 | DELETE `/api/v1/search/history/{id}` | DELETE `/api/search/history/{id}` |
| 清空个人历史 | DELETE `/api/v1/search/history` | DELETE `/api/search/history` |

POST JSON 为 `{ "keyword": "Nuxt" }`，关键词去除首尾空白后长度为 1 到 100。写入、删除和清空成功返回 204。GET 返回 `{ "items": [{ "id": "...", "keyword": "Nuxt", "lastSearchedAtUtc": "..." }] }`。

同一用户的关键词按 `ToUpperInvariant` 结果去重，重复搜索更新展示文字和时间，每人只保留最近 20 个不同关键词。数据库唯一索引与按用户加锁的事务保证并发写入时仍满足去重和数量限制。

当前只提供历史存储及 BFF 接口，尚未在搜索页面增加历史列表或自动记录。页面集成时应在用户确认搜索时调用 POST，不要把翻页、输入联想或公开 GET 请求自动记入个人历史。

更新部署使用 `scripts/start-docker.ps1`。已有环境可能留下已退出的旧 `search-database-init` 容器；它不会再成为新 SearchService 的启动依赖。
