using PixelEngine.Content;
using PixelEngine.Hosting;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Wand controller/projectile 通过公开脚本 API 写入真实 resident world 的集成测试。
/// </summary>
public sealed class WandRuntimeIntegrationTests
{
    private const int WorldWidth = 128;
    private const int WorldHeight = 96;

    /// <summary>
    /// 验证真实输入生成 hit -> material payload，并最终把 acid 写入权威 cell。
    /// </summary>
    [Fact]
    public void TriggerWandSpawnsNestedProjectilesAndPaintsMaterialIntoWorld()
    {
        using Engine engine = CreateEngine(
            out ScriptInputApi input,
            out ScriptCameraApi camera,
            out ScriptScene scene,
            out CellGrid grid,
            out MaterialTable materials);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        Assert.True(materials.TryGetId("acid", out ushort acid));
        FillRect(grid, stone, 70, 4, 76, 88);
        FillRect(grid, stone, 0, 76, WorldWidth, 82);

        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 20f;
        player.SpawnY = 42f;
        WandController wand = entity.AddComponent<WandController>();

        engine.RunHeadlessTicks(2, realDeltaSeconds: 1.0 / 60.0);
        Assert.False(wand.Faulted, wand.LastException?.ToString());
        Assert.True(wand.SelectWand(1));
        WandSpellCatalog catalog = Assert.IsType<WandSpellCatalog>(wand.Catalog);
        Assert.True(wand.TrySetSpellSlot(1, 1, catalog.FindSpellIndex("acid-orb")));
        Point2F target = camera.WorldToScreen(86f, player.CenterY - 2f);
        int entitiesBefore = scene.EntityCount;

        input.Update([], [MouseButton.Left], target.X, target.Y, wheelY: 0f);
        engine.RunHeadlessTicks(1, realDeltaSeconds: 1.0 / 60.0);
        input.Update([], [], target.X, target.Y, wheelY: 0f);

        Assert.Equal(1, wand.CastCount);
        Assert.Equal(2, wand.LastProjectileCount);
        Assert.Equal(2, wand.SpawnedProjectileCount);
        Assert.Equal(entitiesBefore, scene.EntityCount);
        Assert.Equal(254, wand.AvailableProjectileCount);
        Assert.True(scene.TryGetFirstComponent(out WandProjectile? root));
        Assert.False(root.IsPayload);
        Assert.Equal(1, root.AttachedPayloadCount);

        engine.RunHeadlessTicks(1, realDeltaSeconds: 1.0 / 60.0);
        Assert.True(root.IsActive);
        Assert.Equal(1, wand.ActiveProjectileCount);

        engine.RunHeadlessTicks(17, realDeltaSeconds: 1.0 / 60.0);

        Assert.False(wand.Faulted, wand.LastException?.ToString());
        Assert.False(root.Faulted, root.LastException?.ToString());
        Assert.True(
            wand.PayloadActivationCount >= 1,
            $"payload activations={wand.PayloadActivationCount}, plans={wand.LastPayloadPlanCount}, linked={wand.LinkedPayloadCount}, attached={root.AttachedPayloadCount}, active={wand.ActiveProjectileCount}, root=({root.X:F2},{root.Y:F2}), rootActive={root.IsActive}, entities={scene.EntityCount}");
        Assert.True(CountMaterial(grid, acid) > 0, "acid payload 应通过 Cells.Paint 写入权威世界。");
        Assert.Equal(entitiesBefore, scene.EntityCount);
    }

    /// <summary>
    /// 验证 projectile 离开 resident world 时立即归还固定池，不会停在最后一个可读坐标形成残影。
    /// </summary>
    [Fact]
    public void ProjectileLeavingResidentWorldReturnsToPoolWithoutCreatingEntities()
    {
        using Engine engine = CreateEngine(
            out ScriptInputApi input,
            out ScriptCameraApi camera,
            out ScriptScene scene,
            out _,
            out _);
        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 20f;
        player.SpawnY = 42f;
        WandController wand = entity.AddComponent<WandController>();

        engine.RunHeadlessTicks(2, realDeltaSeconds: 1.0 / 60.0);
        WandSpellCatalog catalog = Assert.IsType<WandSpellCatalog>(wand.Catalog);
        Assert.True(wand.TrySetSpellSlot(0, 0, catalog.FindSpellIndex("ember-bolt")));
        int entitiesBefore = scene.EntityCount;
        int availableBefore = wand.AvailableProjectileCount;
        Point2F target = camera.WorldToScreen(-200f, player.CenterY - 2f);

        input.Update([], [MouseButton.Left], target.X, target.Y, wheelY: 0f);
        engine.RunHeadlessTicks(1, realDeltaSeconds: 1.0 / 60.0);
        input.Update([], [], target.X, target.Y, wheelY: 0f);

        Assert.Equal(availableBefore - 1, wand.AvailableProjectileCount);
        engine.RunHeadlessTicks(40, realDeltaSeconds: 1.0 / 60.0);

        Assert.False(wand.Faulted, wand.LastException?.ToString());
        Assert.Equal(0, wand.ActiveProjectileCount);
        Assert.Equal(availableBefore, wand.AvailableProjectileCount);
        Assert.Equal(entitiesBefore, scene.EntityCount);
    }

    /// <summary>
    /// 验证 inventory 编辑允许重复 spell，重置 slot uses，并立即改变下一次 evaluator 输出。
    /// </summary>
    [Fact]
    public void EditedWandSlotsImmediatelyDriveNextCast()
    {
        using Engine engine = CreateEngine(
            out ScriptInputApi input,
            out ScriptCameraApi camera,
            out ScriptScene scene,
            out _,
            out _);
        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 20f;
        player.SpawnY = 42f;
        WandController wand = entity.AddComponent<WandController>();

        engine.RunHeadlessTicks(2, realDeltaSeconds: 1.0 / 60.0);
        WandSpellCatalog catalog = Assert.IsType<WandSpellCatalog>(wand.Catalog);
        int ember = catalog.FindSpellIndex("ember-bolt");
        Assert.True(wand.TrySetSpellSlot(0, 0, ember));
        Assert.True(wand.TrySetSpellSlot(0, 1, ember));
        Assert.Equal("ember-bolt", wand.SpellSlotId(0, 0));
        Assert.Equal("ember-bolt", wand.SpellSlotId(0, 1));

        Point2F target = camera.WorldToScreen(86f, player.CenterY - 2f);
        input.Update([], [MouseButton.Left], target.X, target.Y, wheelY: 0f);
        engine.RunHeadlessTicks(1, realDeltaSeconds: 1.0 / 60.0);
        input.Update([], [], target.X, target.Y, wheelY: 0f);

        Assert.Equal(1, wand.CastCount);
        Assert.Equal(1, wand.LastProjectileCount);
        Assert.Equal("Ember Bolt", wand.LastSpellSummary);
        Assert.Equal("success", wand.LastCastStatusText);
    }

    private static Engine CreateEngine(
        out ScriptInputApi input,
        out ScriptCameraApi camera,
        out ScriptScene scene,
        out CellGrid grid,
        out MaterialTable materials)
    {
        string contentRoot = ContentRoot();
        materials = LoadMaterials(contentRoot);
        Engine engine = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .WithContentRoot(contentRoot)
            .Build();
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(WorldWidth, WorldHeight, particleCapacity: 512);
        _ = engine.AttachPhysics();
        input = new ScriptInputApi();
        camera = new ScriptCameraApi(
            viewportWidth: WorldWidth,
            viewportHeight: WorldHeight,
            centerX: WorldWidth / 2f,
            centerY: WorldHeight / 2f,
            zoom: 1f);
        engine.Context.RegisterService<IInputApi>(EngineServiceRole.Input, input);
        engine.Context.RegisterService(input);
        engine.Context.RegisterService<ICameraApi>(EngineServiceRole.Camera, camera);
        engine.Context.RegisterService(camera);
        engine.Context.RegisterService<IAudioApi>(EngineServiceRole.AudioService, NullAudioApi.Instance);
        scene = new ScriptScene();
        engine.Context.RegisterService(scene);
        _ = engine.AttachScriptingFromServices();
        grid = engine.Context.GetService<CellGrid>();
        return engine;
    }

    private static MaterialTable LoadMaterials(string contentRoot)
    {
        return MaterialContentLoader.Load(
            File.ReadAllText(Path.Combine(contentRoot, "materials.json")),
            File.ReadAllText(Path.Combine(contentRoot, "reactions.json"))).Materials;
    }

    private static void FillRect(
        CellGrid grid,
        ushort material,
        int minX,
        int minY,
        int maxXExclusive,
        int maxYExclusive)
    {
        for (int y = minY; y < maxYExclusive; y++)
        {
            for (int x = minX; x < maxXExclusive; x++)
            {
                grid.SetMaterial(x, y, material);
            }
        }
    }

    private static int CountMaterial(CellGrid grid, ushort material)
    {
        int count = 0;
        for (int y = 0; y < WorldHeight; y++)
        {
            for (int x = 0; x < WorldWidth; x++)
            {
                if (grid.GetMaterial(x, y) == material)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static string ContentRoot()
    {
        return Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }
}
