using PixelEngine.Simulation;
using PixelEngine.Scripting;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Demo 运行态脚本后端装配测试。
/// </summary>
public sealed class DemoRuntimeScriptingTests
{
    /// <summary>
    /// 验证 Engine 能基于已注册脚本程序集物化当前 scene 的 Behaviour，并经 Hosting 相位驱动生命周期。
    /// </summary>
    [Fact]
    public void EngineMaterializesAndDrivesCurrentSceneBehaviour()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty));
        using Engine engine = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .AddScene(new SceneDescriptor("demo", SceneSourceKind.Procedural, typeof(DemoRuntimeBehaviour).FullName!))
            .WithStartScene("demo")
            .Build();
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 64, worldHeightCells: 64, particleCapacity: 16);
        ScriptInputApi input = new();
        engine.Context.RegisterService<IInputApi>(EngineServiceRole.Input, input);
        engine.Context.RegisterService(input);

        engine.RegisterScriptAssembly(typeof(DemoRuntimeBehaviour).Assembly);
        ScriptScene scriptScene = engine.Context.GetService<ScriptScene>();
        DemoRuntimeBehaviour behaviour = GetSingleBehaviour<DemoRuntimeBehaviour>(scriptScene);
        List<string> events = [];
        behaviour.Events = events;

        ScriptSimulationContext scriptContext = engine.AttachScriptingFromServices();
        engine.RunHeadlessTicks(1);

        Scene? current = engine.Context.GetService<ISceneService>().Current;
        Assert.NotNull(current);
        Assert.Same(scriptScene, current.ScriptScene);
        Assert.Same(scriptContext, engine.Context.GetService<IScriptContext>());
        Assert.Same(input, scriptContext.Input);
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.Scripting));
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.WorldAccess));
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.ParticleService));
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.MaterialRegistry));
        Assert.Equal(["start", "update"], events);
    }

    private static MaterialTable Materials(params (string Name, CellType Type)[] definitions)
    {
        MaterialDef[] materials = new MaterialDef[definitions.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = new MaterialDef
            {
                Id = (ushort)i,
                Name = definitions[i].Name,
                Type = definitions[i].Type,
                Density = i == 0 ? (byte)0 : (byte)100,
                HeatCapacity = 1,
                HeatConduct = 255,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            };
        }

        return new MaterialTable(materials);
    }

    private static TBehaviour GetSingleBehaviour<TBehaviour>(ScriptScene scene)
        where TBehaviour : Behaviour
    {
        ScriptEntityInspection[] snapshot = scene.CaptureInspectionSnapshot();
        _ = Assert.Single(snapshot);
        _ = Assert.Single(snapshot[0].Components);
        return Assert.IsType<TBehaviour>(snapshot[0].Components[0].Behaviour);
    }

    private sealed class DemoRuntimeBehaviour : Behaviour
    {
        public List<string> Events { get; set; } = [];

        protected override void OnStart()
        {
            Events.Add("start");
        }

        protected override void OnUpdate(float dt)
        {
            Events.Add("update");
        }
    }
}
