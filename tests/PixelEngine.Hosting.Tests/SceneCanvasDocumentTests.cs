using PixelEngine.UI;
using Xunit;
using ScriptUi = PixelEngine.Scripting;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// .scene v3 内建 WebCanvas/CanvasScaler 的迁移、确定性解析与 prefab 身份边界测试。
/// </summary>
public sealed class SceneCanvasDocumentTests
{
    /// <summary>v1/v2 旧场景不落盘 Canvas，但运行时获得唯一 implicit primary。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LegacySceneResolvesImplicitPrimaryCanvas(int formatVersion)
    {
        EngineSceneCanvasSet set = EngineSceneCanvasResolver.Resolve(new EngineSceneDocument
        {
            FormatVersion = formatVersion,
            Name = "legacy",
            Entities = [],
        });

        Assert.False(set.HasExplicitCanvases);
        Assert.Equal(1, set.Count);
        Assert.Equal(GameUiCanvasIdentity.LegacyImplicit, set.PrimaryId);
        EngineSceneCanvasDefinition canvas = Assert.Single(set.Canvases.ToArray());
        Assert.True(canvas.IsImplicit);
        Assert.True(canvas.IsPrimary);
        Assert.Equal(UiCanvasScalerSettings.Default, canvas.ScalerSettings);
        Assert.Contains(set.Diagnostics.ToArray(), item => item.Kind == EngineSceneCanvasDiagnosticKind.LegacyImplicit);
    }

    /// <summary>v3 Canvas 按 sorting order/StableId 稳定排序，并尊重唯一显式 primary 与完整 scaler。</summary>
    [Fact]
    public void VersionThreeResolvesDeterministicOrderPrimaryAndScaler()
    {
        EngineSceneDocument document = CreateDocument(
            Entity(20, sortingOrder: 5),
            Entity(30, sortingOrder: -2),
            Entity(10, sortingOrder: 5, primary: true, scaler: new EngineSceneCanvasScalerDocument
            {
                ScaleMode = UiScaleMode.ScaleWithScreenSize,
                ReferenceWidth = 1920,
                ReferenceHeight = 1080,
                ScreenMatchMode = UiScreenMatchMode.MatchWidthOrHeight,
                MatchWidthOrHeight = 0.25f,
                ReferencePixelsPerUnit = 125f,
                ScaleFactor = 1.5f,
                PhysicalUnit = UiPhysicalUnit.Centimeters,
                FallbackScreenDpi = 110f,
                DefaultSpriteDpi = 100f,
            }));

        EngineSceneCanvasSet set = EngineSceneCanvasResolver.Resolve(document);
        EngineSceneCanvasDefinition[] canvases = set.Canvases.ToArray();

        Assert.True(set.HasExplicitCanvases);
        Assert.Equal([30, 10, 20], [.. canvases.Select(static item => item.StableId)]);
        Assert.Equal(GameUiCanvasIdentity.FromStableId(10), set.PrimaryId);
        Assert.True(canvases[1].IsPrimary);
        Assert.Equal(UiScaleMode.ScaleWithScreenSize, canvases[1].ScalerSettings.ScaleMode);
        Assert.Equal(1920, canvases[1].ScalerSettings.ReferenceWidth);
        Assert.Equal(0.25f, canvases[1].ScalerSettings.MatchWidthOrHeight);
    }

    /// <summary>显式 Canvas 全部 disabled 时不会偷偷复活 legacy implicit Canvas。</summary>
    [Fact]
    public void AllExplicitCanvasesDisabledProducesNoPrimary()
    {
        EngineSceneDocument document = CreateDocument(
            new EngineSceneEntityDocument
            {
                StableId = 1,
                Name = "Root",
                Enabled = false,
            },
            new EngineSceneEntityDocument
            {
                StableId = 2,
                Name = "ChildCanvas",
                ParentId = 1,
                Enabled = true,
                WebCanvas = new EngineSceneWebCanvasDocument { Enabled = true, Primary = true },
            });

        EngineSceneCanvasSet set = EngineSceneCanvasResolver.Resolve(document);

        Assert.True(set.HasExplicitCanvases);
        Assert.Equal(0, set.Count);
        Assert.Equal(default, set.PrimaryId);
        Assert.Contains(set.Diagnostics.ToArray(), item =>
            item.Kind == EngineSceneCanvasDiagnosticKind.DisabledPrimary && item.StableId == 2);
        Assert.Contains(set.Diagnostics.ToArray(), item => item.Kind == EngineSceneCanvasDiagnosticKind.AllCanvasesDisabled);
    }

    /// <summary>缺失 Scaler 使用明确默认值，孤立 Scaler 只产生 inactive authoring 诊断。</summary>
    [Fact]
    public void MissingAndOrphanScalerHaveExplicitDiagnostics()
    {
        EngineSceneDocument document = CreateDocument(
            Entity(1, sortingOrder: 0),
            new EngineSceneEntityDocument
            {
                StableId = 2,
                Name = "OrphanScaler",
                Enabled = true,
                CanvasScaler = new EngineSceneCanvasScalerDocument(),
            });

        EngineSceneCanvasSet set = EngineSceneCanvasResolver.Resolve(document);

        Assert.Equal(UiCanvasScalerSettings.Default, Assert.Single(set.Canvases.ToArray()).ScalerSettings);
        Assert.Contains(set.Diagnostics.ToArray(), item =>
            item.Kind == EngineSceneCanvasDiagnosticKind.MissingScaler && item.StableId == 1);
        Assert.Contains(set.Diagnostics.ToArray(), item =>
            item.Kind == EngineSceneCanvasDiagnosticKind.OrphanScaler && item.StableId == 2);
    }

    /// <summary>保存/读取 v3 会保持 WebCanvas 与三种 scaler 模式所需的全部入盘字段。</summary>
    [Fact]
    public void VersionThreeRoundTripsBuiltInCanvasComponents()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-scene-canvas-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(root);
        string path = Path.Combine(root, "canvas.scene");
        try
        {
            EngineSceneDocument source = CreateDocument(Entity(
                7,
                sortingOrder: 42,
                primary: true,
                scaler: new EngineSceneCanvasScalerDocument
                {
                    ScaleMode = UiScaleMode.ConstantPhysicalSize,
                    PhysicalUnit = UiPhysicalUnit.Millimeters,
                    FallbackScreenDpi = 144f,
                    DefaultSpriteDpi = 96f,
                    ScaleFactor = 1.25f,
                    ReferencePixelsPerUnit = 123f,
                },
                manifestPath: "ui/hud-manifest.json",
                initialScreenId: "hud"));

            EngineSceneDocumentLoader.SaveDocument(source, path);
            EngineSceneDocument loaded = EngineSceneDocumentLoader.LoadDocument(path);
            EngineSceneEntityDocument entity = Assert.Single(loaded.Entities!);

            Assert.Equal(EngineSceneDocumentLoader.CurrentFormatVersion, loaded.FormatVersion);
            Assert.Equal("ui/hud-manifest.json", entity.WebCanvas!.ManifestPath);
            Assert.Equal("hud", entity.WebCanvas.InitialScreenId);
            Assert.Equal(42, entity.WebCanvas.SortingOrder);
            Assert.True(entity.WebCanvas.Primary);
            Assert.Equal(UiScaleMode.ConstantPhysicalSize, entity.CanvasScaler!.ScaleMode);
            Assert.Equal(UiPhysicalUnit.Millimeters, entity.CanvasScaler.PhysicalUnit);
            Assert.Equal(144f, entity.CanvasScaler.FallbackScreenDpi);
            Assert.Equal(123f, entity.CanvasScaler.ReferencePixelsPerUnit);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>多个有效 primary、逃逸 manifest 与 prefab 持久 primary 都在落盘/运行前硬失败。</summary>
    [Fact]
    public void InvalidPrimaryManifestAndPrefabIdentityAreRejected()
    {
        EngineSceneDocument multiplePrimary = CreateDocument(
            Entity(1, sortingOrder: 0, primary: true),
            Entity(2, sortingOrder: 1, primary: true));
        _ = Assert.Throws<InvalidOperationException>(() => EngineSceneCanvasResolver.Resolve(multiplePrimary));

        EngineSceneDocument escapingManifest = CreateDocument(Entity(
            1,
            sortingOrder: 0,
            manifestPath: "../outside.json"));
        _ = Assert.Throws<InvalidDataException>(() => EngineSceneCanvasResolver.Resolve(escapingManifest));

        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-prefab-canvas-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(root);
        try
        {
            string prefabPath = Path.Combine(root, "hud.prefab");
            _ = Assert.Throws<InvalidOperationException>(() =>
                EngineSceneDocumentLoader.SaveDocument(
                    CreateDocument(Entity(1, sortingOrder: 0, primary: true)),
                    prefabPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>StableId 到 opaque Canvas id 的映射在正 Int32 域内保持非零且无碰撞。</summary>
    [Fact]
    public void CanvasIdentityIsStableInjectiveAndSeparateFromLegacyId()
    {
        ScriptUi.UiCanvasId first = GameUiCanvasIdentity.FromStableId(1);
        ScriptUi.UiCanvasId last = GameUiCanvasIdentity.FromStableId(int.MaxValue);

        Assert.NotEqual(default, first);
        Assert.NotEqual(first, last);
        Assert.NotEqual(first, GameUiCanvasIdentity.LegacyImplicit);
        Assert.Equal(first, GameUiCanvasIdentity.FromStableId(1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => GameUiCanvasIdentity.FromStableId(0));
    }

    private static EngineSceneDocument CreateDocument(params EngineSceneEntityDocument[] entities)
    {
        return new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "canvas-test",
            Entities = entities,
        };
    }

    private static EngineSceneEntityDocument Entity(
        int stableId,
        int sortingOrder,
        bool primary = false,
        EngineSceneCanvasScalerDocument? scaler = null,
        string? manifestPath = null,
        string? initialScreenId = null)
    {
        return new EngineSceneEntityDocument
        {
            StableId = stableId,
            Name = $"Canvas {stableId}",
            Enabled = true,
            WebCanvas = new EngineSceneWebCanvasDocument
            {
                Enabled = true,
                SortingOrder = sortingOrder,
                Primary = primary,
                ManifestPath = manifestPath,
                InitialScreenId = initialScreenId,
            },
            CanvasScaler = scaler,
        };
    }
}
