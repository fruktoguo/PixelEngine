# PixelEngine 反汇编与 BenchmarkDotNet 守门流程

本流程用于 plan/14 与 plan/16 的热路径 codegen 复核。性能结论必须来自 Release 构建、BenchmarkDotNet 报告或反汇编结果，不用 Debug 结果做判断。

## BenchmarkDotNet 守门

CI 的 `benchmark-guard` job 先运行 `tools/disassembly-guard.ps1`，再运行 `tools/benchmark-regression.ps1`。前者通过 BenchmarkDotNet `DisassemblyDiagnoser` 产出汇编并拒绝 `RNGCHKFAIL`，在支持 SIMD 的机器上还要求出现 ymm/zmm/gather 标记；后者按 `bench/PixelEngine.Benchmarks/baselines/ci-baseline.json` 跑回归基准。

本地复现：

```pwsh
dotnet build PixelEngine.sln -c Release
./tools/disassembly-guard.ps1
./tools/benchmark-regression.ps1 -BaselinePath bench/PixelEngine.Benchmarks/baselines/ci-baseline.json
```

针对单个 benchmark 查看 BDN 反汇编：

```pwsh
dotnet run --project bench/PixelEngine.Benchmarks -c Release --no-build -- --filter "*PaletteBgraConversionBenchmarks.ConvertAvx2Experimental*" --job Short --warmupCount 1 --iterationCount 1
```

## DOTNET_JitDisasm

用于精确查看某个 JIT 方法是否仍有 bounds-check 跳转或是否 light-up 到目标 SIMD 寄存器。先构建 Release，再设置方法过滤器运行对应基准或测试。

```pwsh
$env:DOTNET_JitDisasm="PixelEngine.Rendering.PaletteBgraConverter:*"
$env:DOTNET_JitDisasmSummary="1"
dotnet run --project bench/PixelEngine.Benchmarks -c Release --no-build -- --filter "*PaletteBgraConversionBenchmarks*"
Remove-Item Env:DOTNET_JitDisasm, Env:DOTNET_JitDisasmSummary -ErrorAction SilentlyContinue
```

判据：热方法中不应出现 `RNGCHKFAIL`；向量化路径在目标硬件启用时应出现 `ymm`/`zmm` 或对应 gather/vector 指令。若 SIMD 路径基准慢于 scalar，默认热路径必须保留 scalar 或 runtime gate。

## Disasmo / Rider

IDE 内复核时使用同一个 Release 目标与同一 benchmark filter。打开 Disasmo 或 Rider 反汇编视图后定位热方法，检查与 `DOTNET_JitDisasm` 相同的判据：无 `RNGCHKFAIL`，必要 SIMD 路径出现目标寄存器，且调用点没有落回 Debug 或未优化代码。
