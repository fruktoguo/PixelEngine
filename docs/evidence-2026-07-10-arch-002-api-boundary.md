# 2026-07-10 ARCH-002 Chunk/CellGrid API 边界验收

taskIds: `ARCH-002`  
commit: `5ff732dd`  
runSessionId: `local-20260710-arch002-api-boundary`  
runIdentityStatus: `captured`  
hardware: `Microsoft Windows 11 专业版 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 目标

收紧 `Chunk`/`CellGrid` 的公开可写数组和 `ref` 返回，确保 Demo、Hosting 及跨程序集生产调用不能绕过 dirty、parity、KeepAlive 与 rigid-damage 语义，同时保留 Simulation 热路径的零分配 SoA/ref 漫游。

## 实现结果

- `Chunk.Material/Flags/Lifetime/Damage` 对外改为 `ReadOnlySpan<T>`；POH 数组、`Get*Base()` 以及 `MaterialBuffer/FlagsBuffer/LifetimeBuffer/DamageBuffer` 仅保留为 internal implementation seam。
- `CellGrid` 新增 `GetFlags/GetLifetime/GetDamage` 只读 getter；原四个 `ref` accessor 降为 internal；新增 `TryClearCell`，普通 cell 清除会标记 working dirty，刚体 cell 会通知 sink、拒绝裸清除。
- Physics 的 region erase 迁移到 `GetFlags/TryClearCell`；Hosting/Scripting/Rendering/World 与 Demo 生产路径不再使用公开数组或 CellGrid 裸 ref。World 的序列化仍通过受信任 internal buffer 生成可写 snapshot。
- `NeighborWindow` 的四个裸 `ref` accessor 也降为 internal；公开高层 seam 继续保留给 CA/材质扩展协议。
- API discipline 反射测试锁定公开类型为只读 span、公开方法无 ref 返回、热路径基址不公开；行为回归覆盖 normal dirty、rigid damage 通知、刚体状态保留和受控覆盖。
- `plan/03-simulation-kernel.md §3.2–§3.4` 已同步公开边界与 internal hot-path 约束。

## 验证命令与结果

```powershell
dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1
dotnet test PixelEngine.sln -c Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"
dotnet test tests/PixelEngine.Simulation.Tests/PixelEngine.Simulation.Tests.csproj -c Release --no-restore -m:1
dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~PerformanceHardeningToolingDisciplineTests.SimulationHotNeighborAccessUsesUnsafeBaseRefs" -m:1
git diff --check
```

结果：

```text
Build: 32 projects, 0 warnings, 0 errors
Full tests: 1497 passed, 0 failed, 34 skipped
Simulation tests: 191 passed, 0 failed
Tooling discipline regression: 1 passed, 0 failed
git diff --check: clean
```

公开边界静态检查中，`Chunk`/`CellGrid`/`NeighborWindow` 不再出现 public array 或 public cell `ref` accessor；`src/PixelEngine.Hosting`、`src/PixelEngine.Scripting`、`src/PixelEngine.Physics` 与 `demo/PixelEngine.Demo` 不再直接写 `Chunk` 数组或调用 CellGrid 裸 ref。34 个 skipped 均为未启用的 native/条件 smoke，不是本次 API 迁移失败。

## 边界

本报告只证明本地公开 API 边界、内部热路径 seam 和行为回归；未执行远端 push、CI runner 或 native 外部硬件矩阵验证。
