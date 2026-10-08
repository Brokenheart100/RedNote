# 请求校验

Identity、Content、User、Search、Media 使用 Wolverine 6.46.0 集成：

- `WolverineFx.Http.FluentValidation`：HTTP 自动校验，失败返回 400 ProblemDetails 与 `errors`。
- 该包包含 `WolverineFx.FluentValidation`；`UseFluentValidation()` 自动发现并注册当前服务的公开验证器。
- `MapWolverineEndpoints` 中的 `UseFluentValidationProblemDetailMiddleware()` 将验证器接入 HTTP 管线。

验证器负责必填、长度、GUID、标签数量及分页边界。字段错误继续使用 `email`、`title`、`tags`、`pageSize` 等原来的名称。一次请求可以返回多个字段错误。

JSON 请求使用 `ServiceDefaults.Validation.RequestValidator<T>`。它通过 FluentValidation 的 `PreValidate` 钩子处理整个 JSON 请求体为 `null` 的情况；业务字段规则仍使用 FluentValidation。

分页与搜索通过 `[AsParameters]` 绑定查询对象，由同一 HTTP 校验管线检查。现有 URL 和参数名保持一致；缺失分页参数仍按 0 校验并返回 400。

数据库存在性、媒体归属、权限、身份验证、并发锁和 Identity 密码策略仍由业务代码与已有框架处理。图像解码校验继续使用 SkiaSharp。普通 ASP.NET Minimal API 管理接口不在 Wolverine HTTP 管线中，不能仅靠开启该集成就获得自动校验。

Media 的 HTTP/gRPC 批量请求共用媒体 ID 规则，按去重后的数量限制 100 个，不接受空 GUID。HTTP 返回 400 字段错误；`WolverineFx.FluentValidation.Grpc` 将 gRPC 校验错误映射为 `InvalidArgument`，通过 `grpc-status-details-bin` 携带 `google.rpc.BadRequest.FieldViolations`。原有 `MediaQueryValidationException` 和 HTTP 捕获转换代码已删除。

`InputValidationHttpTests` 使用 Alba/TestServer 执行生成的 Wolverine HTTP 管线，覆盖非法字段、空根对象、分页/路由绑定、标签去重、可选字段和相对头像 URL。`MediaProtocolValidationTests` 还通过实际 HTTP/gRPC 协议检查媒体批量校验。直接调用静态端点方法会绕过 HTTP 自动校验，测试无效输入时应使用 HTTP 管线。
