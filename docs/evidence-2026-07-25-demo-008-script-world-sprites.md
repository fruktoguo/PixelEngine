# DEMO-008 脚本世界精灵公开 API 证据

## 缺口

Demo 的动态敌人、掉落物、机关和 Verlet/物理植被此前只能使用屏幕空间矩形与线条 overlay。Hosting 已有内容纹理缓存、world cell 到 viewport 的相机映射和 Background/Decoration 合成层，但脚本公开 API 无法提交带 `ScriptAssetReference` 的瞬时世界精灵，导致真实实体贴图无法随运行时位置移动。

## 实现

- `IScriptContext.WorldSprites` 提供通用 `IWorldSpriteApi`，参数只包含稳定 Texture 资产、world cell 矩形、组合层和 tint，不含任何 Noita 专属类型。
- `ScriptOverlayApi` 同时承载固定 256 条世界精灵命令；达到容量时明确失败，不扩容，不在稳态帧产生世界精灵集合分配。
- `ScriptRuntime.BeginFrame` 通过既有瞬时请求清理链同时清空 overlay 与世界精灵，实体必须在每帧 Update 明确提交当前状态。
- `RenderPhaseDriver` 在相位 10 复用 `IRenderFrameSink.TryResolveWorldVisualSprite`、纹理缓存和运行时相机，将命令转换为已有 `OverlayCommand.SpriteRectangle`。
- 命令仅参与渲染，不写 cell、碰撞、攻击或存档；权威玩法仍由脚本实体和 CPU 世界负责。

## 验证

- Scripting 快速测试验证上一帧命令清空、本帧重新提交：1 passed / 0 failed。
- Hosting 快速测试验证脚本 Texture 资产经相机映射进入 `WorldDecoration`，保留尺寸与 tint：1 passed / 0 failed。

该 API 是后续替换通用方块敌人、物品和动态植被视觉的公共能力节点，本身不宣称相关 Noita 实体已完成视觉 parity。
