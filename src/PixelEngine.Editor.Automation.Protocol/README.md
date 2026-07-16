# PixelEngine Editor Automation Protocol

该包是 PixelEngine Editor 本地自动化 API 的低依赖公共协议层，包含：

- 有界 frame、request/response/event envelope 与协议版本协商 DTO；
- Named Pipe endpoint、实例发现、双向 HMAC 认证、scope、错误和 revision 合同；
- transaction、event resume、artifact、Editor 数据及 build/player 的 source-generated JSON DTO；
- `schema/editor-automation-protocol.v1.schema.json` 发布 Schema。

该包只依赖 .NET BCL，不引用 Editor Shell、ImGui、GL、Hosting、Server 或 Client。外部工具通常
直接引用 `PixelEngine.Editor.Automation.Client`，只有实现其他语言 transport、生成代码或检查
wire/schema compatibility 时才需要单独使用本包。

协议不把 credential secret、运行时类型名或大型二进制放入 JSON payload。截图、日志、profile
trace 与导出数据通过带 canonical path、长度和 SHA256 的 artifact reference 传递。

完整使用说明见 PixelEngine 仓库的 `docs/editor-automation-api.md`。
