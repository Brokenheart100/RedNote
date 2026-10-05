# 后端一致性与上传可靠性

## 帖子写入与消息

点赞、取消点赞、评论、删除评论、编辑、删除先开启 PostgreSQL 事务，锁定 Posts 行，再读取业务状态与统计。业务记录、递增的 Revision 和 Wolverine Outbox 在同一事务提交；同一帖子的写入串行，不同帖子仍可并行。

HTTP 重复点赞和取消点赞不重复写入或发事件。统计读取仍以互动表为准；行锁保证 COUNT 加减期间其他写入不会改变同一帖子的统计。

帖子及评论由各自作者删除，服务端检查身份与所有权。删除顶层评论同时软删除其回复；删除单条回复保留父评论和其他回复。级联状态变更、实际移除数量及事件版本在同一事务提交，重复删除评论不重复减少数量。

前端详情提供删除确认，顶层评论的确认框说明会删除回复。帖子删除成功后从首页、搜索、喜欢列表隐藏，客户端 tombstone 阻止迟到的查询结果重新展示该帖子；退出登录清理这些状态。评论删除后重新加载第一页，避免偏移分页跳过评论。失败时保留帖子与确认框，允许重试。

所有新增的帖子写入路径必须遵守相同锁定顺序：先 Posts，再互动记录。不要绕过锁直接修改互动表或版本。

## 搜索投影

事件附带 Revision。OpenSearch 使用原子脚本分别比较 metadataRevision 与 metricsRevision，旧版本或重复消息不会覆盖新数据，正文更新也不会把较新的统计覆盖掉。统计先到时可建立部分文档，正文事件补齐信息。

删除保留最小 tombstone，清除正文，并从搜索结果排除。ContentService 当前不支持恢复删除帖子，所以任何后到的发布、编辑、统计消息都不能恢复该文档。不要直接清理 tombstone；清理后旧消息可能重建帖子。

更新冲突由 OpenSearch 重试；最终失败抛出异常，由持久化 Inbox 的失败处理机制接管。

## 图片上传

使用 SkiaSharp 3.119.4 解码验证 JPEG、PNG、WebP，并附带 Linux native assets。限制 10 MB、单边 8192 像素、总计 2000 万像素；拒绝格式不匹配、损坏及解码器识别出的动画图片。原始文件保持不变。

S3 上传成功但数据库保存失败时，使用独立 DbContext 和独立的 10 秒超时核查提交结果。确认没有 MediaAssets 记录才删除对象；核查失败保留对象并记录日志，避免未知提交结果导致误删。

后台每小时核查 images/ 下超过 24 小时的对象，仅删除没有 MediaAssets 记录的对象。多实例重复删除是幂等的。已登记但未用于帖子或头像的图片不在此清理范围内。

## 验证

安装 .NET 10、Docker，执行：

```powershell
./scripts/test-backend.ps1
```

脚本启动独立 PostgreSQL 18.3、OpenSearch 3.8.0 容器，使用随机端口与凭据，运行 xUnit 测试，最后删除测试容器。不连接现有业务数据库。

只验证图片，无需容器：

```powershell
dotnet test Test/RedNote.Backend.Tests --filter FullyQualifiedName~ImageValidationTests
```

新增后端 CI 会构建整个解决方案并运行隔离回归测试。旧 Python 用例使用 Test/fixtures 中的真实图片。

## 部署边界

必须先应用 ContentService 的 PostEventRevision 迁移，再启动新的 ContentService；同时升级 SearchService 和 MediaService 镜像。新事件字段保留缺省值 0，便于反序列化历史消息，但历史事件本身没有可靠的顺序信息。升级前应暂停旧写入并等待旧事件处理完成；不要长期混跑新旧 ContentService 实例。

本改动不自动重建历史搜索索引，也不修改生产证书、限流或健康检查配置。这些仍需要后续完善。
