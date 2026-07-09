using PixelEngine.Core.Time;
using PixelEngine.Core;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using PixelEngine.World;
using System.Reflection;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Play/Edit/Step 执行模式测试。
/// 不变式：Play/Edit/Step 模式切换不泄漏输入、暂停时 tick 可冻结。
/// </summary>
public sealed class EngineExecutionModeTests
{
    /// <summary>
    /// 验证编辑模式普通 tick 只推进渲染帧，不推进 sim 相位。
    /// </summary>
    [Fact]
    public void EditModeTickKeepsRenderPhasesButPausesSim()
    {
        // Arrange：搭建测试场景与依赖
        List<EnginePhase> phases = [];
        EngineBuilder builder = new EngineBuilder()
            .WithWorkerCount(1);
        RegisterAllPhases(builder, phases);
        using Engine engine = builder.Build();

        engine.EnterEditMode();
        // Act：执行被测操作
        FrameTiming timing = engine.RunOneTick();

        // Assert：验证不变式与预期结果
        Assert.Equal(EngineExecutionMode.Edit, engine.Mode);
        Assert.False(timing.RunSim);
        Assert.False(timing.RunPhysics);
        Assert.Equal(1, engine.Context.Clock.FrameIndex);
        Assert.Equal(0, engine.Context.Clock.SimTickIndex);
        Assert.Equal(
            [
                EnginePhase.InputAndTime,
                EnginePhase.BuildRenderBuffer,
                EnginePhase.GpuUploadAndRender,
                EnginePhase.WorldStreaming,
            ],
            phases);
    }

    /// <summary>
    /// 验证 StepOnce 从编辑模式临时执行一个 sim tick，随后回到编辑模式。
    /// </summary>
    [Fact]
    public void StepOnceRunsOneSimTickThenReturnsToEditMode()
    {
        // Arrange：准备输入与初始状态
        List<EngineExecutionMode> observedModes = [];
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .OnPhase(EnginePhase.GameLogicAndScripts, context => observedModes.Add(context.Engine.Mode))
            .Build();

        engine.EnterEditMode();
        FrameTiming timing = engine.StepOnce();

        // Assert：验证预期结果
        Assert.True(timing.RunSim);
        Assert.True(timing.RunPhysics);
        Assert.Equal(1, engine.Context.Clock.FrameIndex);
        Assert.Equal(1, engine.Context.Clock.SimTickIndex);
        Assert.Equal(EngineExecutionMode.Edit, engine.Mode);
        Assert.Equal([EngineExecutionMode.Step], observedModes);
        engine.EnterPlayMode();
        _ = Assert.Throws<InvalidOperationException>(() => engine.StepOnce());
    }

    /// <summary>
    /// 验证 StepOnce 在 30Hz 降频时仍强制执行恰好一个 sim tick。
    /// </summary>
    [Fact]
    public void StepOnceForcesSimTickWhenClockIsDownscaled()
    {
        // Arrange：搭建测试场景与依赖
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithSimHz(EngineConstants.SimHzDownscaled)
            .Build();

        engine.EnterEditMode();
        // Act：执行被测操作
        _ = engine.RunOneTick();
        FrameTiming timing = engine.StepOnce();

        // Assert：验证不变式与预期结果
        Assert.True(timing.RunSim);
        Assert.True(timing.RunPhysics);
        Assert.Equal(2, engine.Context.Clock.FrameIndex);
        Assert.Equal(1, engine.Context.Clock.SimTickIndex);
        Assert.Equal(EngineExecutionMode.Edit, engine.Mode);
    }

    /// <summary>
    /// 验证 Editor sim 控制适配器只通过 Engine/FrameClock 控制暂停、单步与 60/30Hz。
    /// </summary>
    [Fact]
    public void EngineSimulationControlServiceDrivesEngineClockAndModes()
    {
        // Arrange：搭建测试场景与依赖
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        EngineSimulationControlService control = new(engine);

        control.EnterEditMode();
        control.SetSimHz(EngineConstants.SimHzDownscaled);
        control.StepOnce();
        SimulationControlSnapshot stepped = control.Capture();
        control.EnterPlayMode();
        // Act：执行被测操作
        _ = engine.RunOneTick();
        SimulationControlSnapshot playing = control.Capture();

        // Assert：验证不变式与预期结果
        Assert.False(stepped.IsPlaying);
        Assert.Equal(EngineConstants.SimHzDownscaled, stepped.SimHz);
        Assert.Equal(1, stepped.SimTickIndex);
        Assert.True(playing.IsPlaying);
        Assert.Equal(EngineConstants.SimHzDownscaled, engine.Context.Clock.SimHz);
        Assert.Equal(EngineConstants.SimHzDownscaled, engine.RequestedSimHz);
    }

    /// <summary>
    /// 验证 Editor Play session 服务经 Engine 切换当前态 Play/Edit。
    /// </summary>
    [Fact]
    public void EngineEditorPlaySessionServiceDrivesCurrentStatePlay()
    {
        // Arrange：准备输入与初始状态
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        EngineEditorPlaySessionService service = new(engine);

        EditorPlaySessionResult play = service.EnterPlayCurrent();
        EditorPlaySessionResult edit = service.ExitPlay();

        // Assert：验证预期结果
        Assert.True(play.Succeeded);
        Assert.True(edit.Succeeded);
        Assert.Equal(EngineExecutionMode.Edit, engine.Mode);
        Assert.Equal(EditorMode.Edit, edit.Snapshot.Mode);
    }

    /// <summary>
    /// 验证临时 Play 缺少快照后端时明确失败，不进入 Play。
    /// </summary>
    [Fact]
    public void EngineEditorPlaySessionServiceReportsMissingTemporarySnapshotStore()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        EngineEditorPlaySessionService service = new(engine);
        engine.EnterEditMode();

        EditorPlaySessionResult result = service.EnterPlayTemporary();

        Assert.False(result.Succeeded);
        Assert.Equal(EngineExecutionMode.Edit, engine.Mode);
        Assert.Contains("缺少快照后端", result.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证临时 Play 经快照后端保存并在退出时恢复。
    /// </summary>
    [Fact]
    public void EngineEditorPlaySessionServiceSavesAndRestoresTemporarySnapshot()
    {
        // Arrange：准备输入与初始状态
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        RecordingPlaySnapshotStore store = new();
        EngineEditorPlaySessionService service = new(engine, store);

        EditorPlaySessionResult play = service.EnterPlayTemporary();
        EditorPlaySessionResult edit = service.ExitPlay();

        // Assert：验证预期结果
        Assert.True(play.Succeeded);
        Assert.True(edit.Succeeded);
        Assert.Equal(["save", "restore"], store.Calls);
        Assert.False(edit.Snapshot.TemporarySnapshotActive);
        Assert.Equal(EngineExecutionMode.Edit, engine.Mode);
    }

    /// <summary>
    /// 验证 Engine 暴露脚本程序集注册入口，且重复注册不会产生重复项。
    /// </summary>
    [Fact]
    public void EngineRegistersScriptAssembliesForScriptingHost()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        Assembly assembly = typeof(EngineExecutionModeTests).Assembly;

        engine.RegisterScriptAssembly(assembly);
        engine.RegisterScriptAssembly(assembly);

        ScriptAssemblyRegistry registry = engine.Context.GetService<ScriptAssemblyRegistry>();
        Assert.Equal([assembly], registry.Assemblies);
    }

    /// <summary>
    /// 验证 Engine 可从 ContentRoot 加载材质内容包并注册材质/反应服务。
    /// </summary>
    [Fact]
    public void EngineLoadsContentPackageAndRegistersMaterialServices()
    {
        // Arrange：准备输入与初始状态
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-content-{Guid.NewGuid():N}");
        try
        {
            _ = Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, EngineContentLoader.MaterialsFileName), MaterialsJson);
            File.WriteAllText(Path.Combine(contentRoot, EngineContentLoader.ReactionsFileName), ReactionsJson);
            using Engine engine = new EngineBuilder()
                .WithWorkerCount(1)
                .WithContentRoot(contentRoot)
                .Build();

            EngineContentPackage package = engine.LoadContentPackage();

            // Assert：验证预期结果
            Assert.True(package.TryResolveMaterial("empty", out MaterialId emptyId));
            Assert.Equal(0, emptyId.Value);
            Assert.Equal(2, package.MaterialCount);
            Assert.Equal(0, package.ReactionCount);
            Assert.Same(package.Materials, engine.Context.GetService<IMaterialQuery>());
            Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.MaterialRegistry));
            Assert.Same(package.Materials, engine.Context.GetService<EngineMaterialRegistry>());
            Assert.NotNull(engine.Context.GetService<MaterialTable>());
            Assert.NotNull(engine.Context.GetService<ReactionTable>());
            Assert.Same(package, engine.Context.GetService<EngineContentPackage>());
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    private static void RegisterAllPhases(EngineBuilder builder, List<EnginePhase> phases)
    {
        for (int i = 0; i < 12; i++)
        {
            EnginePhase phase = (EnginePhase)i;
            _ = builder.OnPhase(phase, context => phases.Add(context.Phase));
        }
    }

    private sealed class RecordingPlaySnapshotStore : IEditorPlaySnapshotStore
    {
        public List<string> Calls { get; } = [];

        public SaveLoadOperationResult SaveTemporarySnapshot()
        {
            Calls.Add("save");
            return new SaveLoadOperationResult(true, "saved", null, null);
        }

        public SaveLoadOperationResult RestoreTemporarySnapshot()
        {
            Calls.Add("restore");
            return new SaveLoadOperationResult(true, "restored", null, null);
        }
    }

    private const string MaterialsJson = /*lang=json,strict*/ """
    {
      "materials": [
        { "name": "empty", "type": "Empty", "heatCapacity": 1 },
        { "name": "stone", "type": "Solid", "density": 200, "heatCapacity": 1 }
      ]
    }
    """;

    private const string ReactionsJson = /*lang=json,strict*/ """{ "reactions": [] }""";
}
