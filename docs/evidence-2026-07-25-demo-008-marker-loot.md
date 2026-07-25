# DEMO-008 loot 与商店交互运行时证据

## 已实现

- 玩家实体挂载 `PlayerInventory`，保存本轮金币、累计金币和最多 16 个未装配法术目录索引。
- `spawn_hp`、宝箱/奖励、免费法术和 shop item marker 生成 Demo 侧纯 C# `NoitaMarkerLoot`，不依赖 Lua 或本机 Noita 运行时。
- `E` 键进入公开脚本输入与 Silk 键盘映射；玩家进入 18 cell 范围后显示中文交互提示，提示窗口不捕获鼠标或键盘。
- 宝箱按 world seed 与坐标确定性产出 20..50 金币；商店按 cheap/normal/special 语义消费 20/50/100 金币；法术索引约束到当前实际目录。
- 成功拾取后 marker 写入当前 run 的 4096 槽 resolved 集，离开视口再返回不会复活；marker key 纳入函数名，避免同坐标不同交互被合并。
- 未支持的通用 Loot marker fail-closed，不再把 workshop、perk reroll 等对象误生成成法术。

## 验证

```pwsh
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --filter "FullyQualifiedName~NoitaMarkerLootTests|FullyQualifiedName~NoitaMarkerEnemyTests"
```

结果：`15 passed / 0 failed`。覆盖金币收支、库存容量、宝箱一次性状态、商店余额门槛、法术目录约束、四类 profile 白名单及未实现 Loot 抑制。

```pwsh
dotnet test tests/PixelEngine.Scripting.Tests/PixelEngine.Scripting.Tests.csproj -c Release --filter "FullyQualifiedName~ScriptInputApiTests"
```

结果：`4 passed / 0 failed`。覆盖脚本输入按键生命周期；本节点新增 `E` 保持同一枚举/快照路径。

```pwsh
dotnet build demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release
```

结果：`0 error`；首次编译暴露的新 XML/style warning 已在提交前清零，并由后续定向测试编译确认。

## 未完成

- 当前未装配法术已进入库存，但尚未接入 Wand 编辑器的拖放、交换、丢弃与世界 item 实体。
- marker 仍使用几何 overlay 表达，真实 item sprite、动画和掉落物物理尚未闭合。
- 传送、机关、动态物理陷阱、perk 与 Holy Mountain 专属交互仍需逐类实现。
- 本证据是代码与定向测试证据，不替代后续真实 Player 输入和截图矩阵。
