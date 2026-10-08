# 多用户带图发帖测试

先启动本地 Docker 部署（默认前端 `http://localhost:3000`，网关 `http://localhost:8080`）。

```powershell
cd Red-Book
npm run test:bulk-posts
# 自定义数量或地址
npm run test:bulk-posts -- --users 3 --posts-per-user 30 --frontend http://localhost:3000 --gateway http://localhost:8080
# 再次验证已完成批次，不创建新用户或帖子
npm run test:bulk-posts -- --verify-report ../artifacts/bulk-posts/<批次>/report.json
```

这是主动创建并保留数据的独立命令，不加入日常回归测试。每次运行创建新的普通账号，默认 3 人各 30 篇帖子，每篇单独上传一张 900×1200 PNG 测试封面。通过真实 OIDC/BFF 登录，每个用户使用独立浏览器会话。测试验证图片归属、下载字节、作者帖子数量、跨用户删除拒绝、搜索索引和浏览器封面显示。

上传按共享 IP 的每分钟 30 次限流控制节奏，遇到 429 有限等待重试，默认约需几分钟。不会关闭限流，不会自动重试结果不明确的上传操作，也不会删除原有数据。

结果保存在根目录 `artifacts/bulk-posts/<批次>/`（已被 Git 忽略）：

- `report.json`：运行结果、账号邮箱和用户 ID、已创建帖子/图片 ID、搜索页面地址。每篇帖子成功后更新；失败时保留部分数据。
- `accounts.local.json`：账号邮箱和密码，Windows 下限制为当前用户访问。不要提交或分享该文件。
- `search-preview.png`：搜索页面截图。

打开报告中的 `searchUrl` 可集中查看该批次，也可以在首页浏览最新帖子。封面是合成的测试图，不是真实摄影图片。重复运行会产生新一批数据。
