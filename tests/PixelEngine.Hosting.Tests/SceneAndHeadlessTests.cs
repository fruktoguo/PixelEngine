using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 场景、项目模型与 headless 驱动测试。
/// </summary>
public sealed class SceneAndHeadlessTests
{
    /// <summary>
    /// 验证 EngineBuilder 能从项目模型注册场景并切到起始场景。
    /// </summary>
    [Fact]
    public void BuildLoadsProjectScenesAndStartScene()
    {
        EngineProject project = new(
            "game-content",
            "start",
            [
                new SceneDescriptor("start", SceneSourceKind.Procedural, "terrain-a"),
                new SceneDescriptor("menu"),
            ]);

        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithProject(project)
            .Build();

        ISceneService scenes = engine.Context.GetService<ISceneService>();

        Assert.Equal("game-content", engine.Context.Options.ContentRoot);
        Assert.Equal("start", engine.Context.Options.StartScene);
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.SceneService));
        Assert.NotNull(scenes.Current);
        Assert.Equal("start", scenes.Current.Name);
        Assert.Equal("terrain-a", scenes.Current.ResolvedSource);
        Assert.True(scenes.Current.WorldConstructionPending);
        Assert.True(scenes.TryGet("menu", out SceneDescriptor menu));
        Assert.Equal(SceneSourceKind.Empty, menu.SourceKind);
    }

    /// <summary>
    /// 验证手动场景切换和卸载。
    /// </summary>
    [Fact]
    public void SceneServiceSwitchesAndUnloadsCurrentScene()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("a"))
            .AddScene(new SceneDescriptor("b", SceneSourceKind.SaveDirectory, "saves/b"))
            .AddScene(new SceneDescriptor("c", SceneSourceKind.SceneFile, "scenes/c.scene"))
            .Build();
        ISceneService scenes = engine.Context.GetService<ISceneService>();

        Scene loaded = engine.LoadScene("b");

        Assert.Same(loaded, scenes.Current);
        Assert.Equal(SceneSourceKind.SaveDirectory, loaded.Descriptor.SourceKind);
        Assert.Equal("saves/b", loaded.Descriptor.Source);
        Assert.Equal(Path.GetFullPath(Path.Combine(engine.Context.Options.ContentRoot, "saves/b")), loaded.ResolvedSource);
        Assert.True(loaded.WorldConstructionPending);

        Scene sceneFile = scenes.SwitchTo("c");
        Assert.Equal(SceneSourceKind.SceneFile, sceneFile.Descriptor.SourceKind);
        Assert.Equal(Path.GetFullPath(Path.Combine(engine.Context.Options.ContentRoot, "scenes/c.scene")), sceneFile.ResolvedSource);
        Assert.True(sceneFile.WorldConstructionPending);

        scenes.UnloadCurrent();
        Assert.Null(scenes.Current);
        _ = Assert.Throws<InvalidOperationException>(() => scenes.SwitchTo("missing"));
    }

    /// <summary>
    /// 验证 Engine.LoadScene 会从 .scene 文件物化脚本实体与 Behaviour 参数。
    /// </summary>
    [Fact]
    public void LoadSceneFileInstantiatesScriptBehaviours()
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-scene-file-{Guid.NewGuid():N}");
        try
        {
            string sceneDirectory = Path.Combine(contentRoot, "scenes");
            _ = Directory.CreateDirectory(sceneDirectory);
            string scenePath = Path.Combine(sceneDirectory, "c.scene");
            string behaviourType = typeof(SceneFileTestBehaviour).FullName!;
            File.WriteAllText(
                scenePath,
                $$"""
                {
                  "formatVersion": 1,
                  "name": "c",
                  "entities": [
                    {
                      "stableId": 7,
                      "name": "player",
                      "behaviours": [
                        {
                          "typeName": "{{behaviourType}}",
                          "serializedFields": {
                            "Label": "hero",
                            "Health": "42",
                            "Enabled": "false"
                          }
                        }
                      ]
                    }
                  ]
                }
                """);
            using Engine engine = new EngineBuilder()
                .WithWorkerCount(1)
                .WithContentRoot(contentRoot)
                .AddScene(new SceneDescriptor("c", SceneSourceKind.SceneFile, "scenes/c.scene"))
                .Build();
            engine.RegisterScriptAssembly(typeof(SceneFileTestBehaviour).Assembly);

            Scene loaded = engine.LoadScene("c");

            Assert.NotNull(loaded.ScriptScene);
            Assert.Equal(1, loaded.ScriptScene.EntityCount);
            ScriptEntityInspection[] snapshot = loaded.ScriptScene.CaptureInspectionSnapshot();
            SceneFileTestBehaviour behaviour = Assert.IsType<SceneFileTestBehaviour>(snapshot[0].Components[0].Behaviour);
            Assert.Equal("hero", behaviour.Label);
            Assert.Equal(42, behaviour.Health);
            Assert.False(behaviour.Enabled);
            Assert.Same(loaded.ScriptScene, engine.Context.GetService<PixelEngine.Scripting.Scene>());
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 procedural scene source 会在脚本程序集注册后物化入口 Behaviour。
    /// </summary>
    [Fact]
    public void RegisterScriptAssemblyMaterializesCurrentProceduralScene()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("proc", SceneSourceKind.Procedural, nameof(ProceduralEntryBehaviour)))
            .WithStartScene("proc")
            .Build();

        engine.RegisterScriptAssembly(typeof(ProceduralEntryBehaviour).Assembly);

        Scene? current = engine.Context.GetService<ISceneService>().Current;
        Assert.NotNull(current);
        Assert.NotNull(current.ScriptScene);
        Assert.Equal(1, current.ScriptScene.EntityCount);
        ScriptEntityInspection[] snapshot = current.ScriptScene.CaptureInspectionSnapshot();
        _ = Assert.IsType<ProceduralEntryBehaviour>(snapshot[0].Components[0].Behaviour);
        Assert.Same(current.ScriptScene, engine.Context.GetService<PixelEngine.Scripting.Scene>());
    }

    /// <summary>
    /// 验证手动 LoadScene 也会物化 procedural Behaviour。
    /// </summary>
    [Fact]
    public void LoadSceneMaterializesProceduralBehaviour()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("proc", SceneSourceKind.Procedural, typeof(ProceduralEntryBehaviour).FullName!))
            .Build();
        engine.RegisterScriptAssembly(typeof(ProceduralEntryBehaviour).Assembly);

        Scene loaded = engine.LoadScene("proc");

        Assert.NotNull(loaded.ScriptScene);
        ScriptEntityInspection[] snapshot = loaded.ScriptScene.CaptureInspectionSnapshot();
        _ = Assert.IsType<ProceduralEntryBehaviour>(snapshot[0].Components[0].Behaviour);
    }

    /// <summary>
    /// 验证场景来源配置会快速拒绝无效组合。
    /// </summary>
    [Fact]
    public void SceneDescriptorRejectsInvalidSourceConfiguration()
    {
        _ = Assert.Throws<ArgumentException>(() => new SceneDescriptor("empty", SceneSourceKind.Empty, "unexpected"));
        _ = Assert.Throws<ArgumentException>(() => new SceneDescriptor("save", SceneSourceKind.SaveDirectory));
        _ = Assert.Throws<ArgumentException>(() => new SceneDescriptor("scene", SceneSourceKind.SceneFile));
        _ = Assert.Throws<ArgumentException>(() => new SceneDescriptor("proc", SceneSourceKind.Procedural));
    }

    /// <summary>
    /// 验证 headless 模式可以按固定步数驱动，且非 headless 禁止使用该入口。
    /// </summary>
    [Fact]
    public void RunHeadlessTicksAdvancesFixedNumberOfFrames()
    {
        using Engine headless = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .Build();

        headless.RunHeadlessTicks(5);

        Assert.Equal(5, headless.Context.Clock.FrameIndex);
        Assert.Equal(5, headless.Context.Clock.SimTickIndex);
        Assert.False(headless.Context.Options.EnableGpu);

        using Engine windowed = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        _ = Assert.Throws<InvalidOperationException>(() => windowed.RunHeadlessTicks(1));
    }

    /// <summary>
    /// .scene 加载测试用 Behaviour。
    /// </summary>
    public sealed class SceneFileTestBehaviour : Behaviour
    {
        /// <summary>
        /// 测试字符串字段。
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// 测试数值字段。
        /// </summary>
        public int Health { get; set; }
    }

    /// <summary>
    /// procedural scene 测试入口 Behaviour。
    /// </summary>
    public sealed class ProceduralEntryBehaviour : Behaviour
    {
    }
}
