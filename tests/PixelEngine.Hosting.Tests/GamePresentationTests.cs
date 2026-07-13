using System.Numerics;
using PixelEngine.Editor.Shell;
using PixelEngine.Rendering;
using PixelEngine.UI;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>Game View 三层分辨率、原子 revision 与输入映射回归。</summary>
public sealed class GamePresentationTests
{
    /// <summary>验证 override 与显示度量在同一 presentation revision 原子提交。</summary>
    [Fact]
    public void CoordinatorCommitsOverrideAndDisplayMetricsAsOneRevision()
    {
        MutableDisplayMetricsSource display = new(new DisplayMetricsSnapshot(7, 1.5f, 1.5f, 144f, 1));
        MutablePresentationOverride presentationOverride = new();
        GamePresentationCoordinator coordinator = new(
            640,
            360,
            1280,
            720,
            4096,
            display,
            presentationOverride);

        GamePresentationDescriptor initial = coordinator.CommitFrameBoundary();
        GamePresentationDescriptor unchanged = coordinator.CommitFrameBoundary();
        presentationOverride.Request = new GamePresentationOverride(
            800,
            800,
            GamePresentationSource.EditorFixedResolution,
            4);
        GamePresentationDescriptor square = coordinator.CommitFrameBoundary();
        display.Value = display.Value with { Revision = 2, FramebufferScaleX = 2f, FramebufferScaleY = 2f };
        GamePresentationDescriptor dpiChanged = coordinator.CommitFrameBoundary();

        Assert.True(initial.IsValid);
        Assert.Equal(1, initial.PresentationRevision);
        Assert.Equal(initial.PresentationRevision, unchanged.PresentationRevision);
        Assert.Equal(2, square.PresentationRevision);
        Assert.Equal((800, 800), (square.PresentationWidth, square.PresentationHeight));
        Assert.Equal(new PresentationViewport(0, 175, 800, 450, 640, 360, 800, 800), square.WorldContentRect);
        Assert.Equal(4, square.RequestRevision);
        Assert.Equal(3, dpiChanged.PresentationRevision);
        Assert.Equal(2, dpiChanged.UiDisplayMetrics.MetricsRevision);
        Assert.Equal(2f, dpiChanged.UiDisplayMetrics.FramebufferScaleX);
    }

    /// <summary>验证超出 GPU 上限的请求保留上一份有效提交。</summary>
    [Fact]
    public void CoordinatorRejectsOversizedOverrideWithoutDestroyingCommittedFrame()
    {
        MutableDisplayMetricsSource display = new(new DisplayMetricsSnapshot(0, 1f, 1f, null, 1));
        MutablePresentationOverride presentationOverride = new();
        GamePresentationCoordinator coordinator = new(640, 360, 1280, 720, 2048, display, presentationOverride);
        GamePresentationDescriptor initial = coordinator.CommitFrameBoundary();
        presentationOverride.Request = new GamePresentationOverride(
            4096,
            2160,
            GamePresentationSource.EditorFixedResolution,
            1);

        GamePresentationDescriptor rejected = coordinator.CommitFrameBoundary();

        Assert.Equal(initial.PresentationRevision, rejected.PresentationRevision);
        Assert.Equal((1280, 720), (rejected.PresentationWidth, rejected.PresentationHeight));
        Assert.Contains("超过 renderer 上限", coordinator.LastDiagnostic, StringComparison.Ordinal);
    }

    /// <summary>验证 UI 命中完整画布、玩法只命中 world content。</summary>
    [Fact]
    public void PresentationInputMapsUiAcrossCanvasButGameplayOnlyInsideWorldContent()
    {
        GamePresentationDescriptor descriptor = CreateSquareDescriptor();

        GamePresentationInputMapping topBar = GamePresentationInputMapping.Resolve(
            in descriptor,
            new Vector2(400f, 50f));
        GamePresentationInputMapping center = GamePresentationInputMapping.Resolve(
            in descriptor,
            new Vector2(400f, 400f));

        Assert.True(topBar.IsInsidePresentation);
        Assert.False(topBar.IsInsideWorldContent);
        Assert.True(center.IsInsidePresentation);
        Assert.True(center.IsInsideWorldContent);
        Assert.Equal(320f, center.WorldPoint.X, 3);
        Assert.Equal(180f, center.WorldPoint.Y, 3);
        Assert.Equal(descriptor.PresentationRevision, center.PresentationRevision);
    }

    /// <summary>验证独立 Player 依次处理 OS 与 world 两层 letterbox。</summary>
    [Fact]
    public void StandaloneInputUsesNestedOsPresentationAndWorldLetterboxes()
    {
        GamePresentationDescriptor descriptor = CreateSquareDescriptor();

        Assert.False(GamePresentationInputTransform.TryMapFramebufferToPresentation(
            in descriptor,
            1600,
            900,
            100f,
            450f,
            out _));
        Assert.True(GamePresentationInputTransform.TryMapFramebufferToPresentation(
            in descriptor,
            1600,
            900,
            800f,
            450f,
            out Vector2 presentationPoint));
        GamePresentationInputMapping mapping = GamePresentationInputMapping.Resolve(in descriptor, presentationPoint);

        Assert.Equal(new Vector2(400f, 400f), presentationPoint);
        Assert.True(mapping.IsInsideWorldContent);
        Assert.Equal(320f, mapping.WorldPoint.X, 3);
        Assert.Equal(180f, mapping.WorldPoint.Y, 3);
    }

    /// <summary>验证 presentation IME 几何正确映回 OS framebuffer。</summary>
    [Fact]
    public void StandaloneImeGeometryMapsFromPresentationBackToOsFramebuffer()
    {
        GamePresentationDescriptor descriptor = CreateSquareDescriptor();
        UiImeGeometry geometry = UiImeGeometry.FromCaretRect(400f, 400f, 2f, 20f);

        Assert.True(GamePresentationInputTransform.TryMapPresentationGeometryToFramebuffer(
            in descriptor,
            1600,
            900,
            in geometry,
            out UiImeGeometry mapped));

        Assert.Equal(800f, mapped.CaretX, 3);
        Assert.Equal(450f, mapped.CaretY, 3);
        Assert.Equal(2.25f, mapped.CaretWidth, 3);
        Assert.Equal(22.5f, mapped.CaretHeight, 3);
    }

    /// <summary>验证 Fit 居中、物理像素 scale 与 pan clamp。</summary>
    [Fact]
    public void GameViewSnapshotCentersFitAndClampsPhysicalScalePan()
    {
        PresentationViewport world = PresentationViewport.Fit(640, 360, 320, 180);
        GameViewViewportSnapshot fit = GameViewViewportSnapshot.Create(
            320,
            180,
            8,
            in world,
            new Vector2(10f, 20f),
            new Vector2(200f, 200f),
            Vector2.One,
            0f,
            Vector2.Zero);
        PresentationViewport largeWorld = PresentationViewport.Fit(640, 360, 640, 360);
        GameViewViewportSnapshot cropped = GameViewViewportSnapshot.Create(
            640,
            360,
            9,
            in largeWorld,
            Vector2.Zero,
            new Vector2(160f, 90f),
            Vector2.One,
            100f,
            new Vector2(float.MaxValue, float.MaxValue));

        Assert.Equal(new GameViewRect(10f, 63.75f, 200f, 112.5f), fit.ImageRect);
        Assert.Equal(8, fit.PresentationRevision);
        Assert.Equal(new Vector2(240f, 135f), cropped.Pan);
        Assert.True(cropped.TryMapPanelToViewport(new Vector2(80f, 45f), out Vector2 point));
        Assert.Equal(560f, point.X, 3);
        Assert.Equal(315f, point.Y, 3);
    }

    /// <summary>验证 toolbar workspace 持久化及自动最大化生命周期。</summary>
    [Fact]
    public void GameViewPanelPersistsToolbarStateAndRestoresOnlyAutomaticMaximize()
    {
        string projectPath = Path.Combine(Path.GetTempPath(), "PixelEngine", "GameViewPanelState");
        EditorWorkspaceStore workspace = EditorWorkspaceStore.CreateInMemory();
        GameViewPanel panel = CreatePanel(workspace, projectPath);
        panel.SelectPreset("resolution-1280-720");
        panel.SetScalePercent(100f);
        panel.SetMaximizeOnPlay(true);

        panel.PrepareFrame(PixelEngine.Editor.EditorMode.Play);
        Assert.True(panel.IsMaximized);
        panel.PrepareFrame(PixelEngine.Editor.EditorMode.Paused);
        Assert.True(panel.IsMaximized);
        panel.PrepareFrame(PixelEngine.Editor.EditorMode.Edit);
        Assert.False(panel.IsMaximized);

        panel.ToggleMaximized();
        panel.PrepareFrame(PixelEngine.Editor.EditorMode.Play);
        panel.PrepareFrame(PixelEngine.Editor.EditorMode.Edit);
        Assert.True(panel.IsMaximized);

        GameViewPanel restored = CreatePanel(workspace, projectPath);
        Assert.Equal("resolution-1280-720", restored.SelectedPresetId);
        Assert.Equal(100f, restored.ScalePercent);
        Assert.True(restored.MaximizeOnPlay);
    }

    /// <summary>
    /// 验证 Game View 工具栏按真实控件预算逐级转入 overflow，且任何合法宽度都不会把最后的菜单入口裁出面板。
    /// </summary>
    [Fact]
    public void GameViewToolbarMovesLowFrequencyControlsIntoOverflowBeforeClipping()
    {
        GameViewToolbarMetrics metrics = new(
            ItemSpacing: 8f,
            OverflowWidth: 30f,
            CompactScaleWidth: 72f,
            FullScaleWidth: 88f,
            CompactMaximizeWidth: 40f,
            FullMaximizeWidth: 72f,
            MaximizeOnPlayWidth: 136f,
            MinimumPresetWidth: 96f,
            FullPresetMinimumWidth: 140f,
            MaximumPresetWidth: 260f);

        GameViewToolbarLayout full = GameViewPanel.ResolveToolbarLayout(640f, in metrics);
        GameViewToolbarLayout compact = GameViewPanel.ResolveToolbarLayout(360f, in metrics);
        GameViewToolbarLayout narrow = GameViewPanel.ResolveToolbarLayout(200f, in metrics);
        GameViewToolbarLayout overflowOnly = GameViewPanel.ResolveToolbarLayout(120f, in metrics);
        GameViewToolbarLayout invalidWidth = GameViewPanel.ResolveToolbarLayout(float.NaN, in metrics);

        Assert.Equal(GameViewToolbarDensity.Full, full.Density);
        Assert.True(full.ShowPreset && full.ShowScale && full.ShowMaximize && full.ShowMaximizeOnPlay);
        Assert.InRange(full.OccupiedWidth, 1f, 640f);

        Assert.Equal(GameViewToolbarDensity.Compact, compact.Density);
        Assert.True(compact.ShowPreset && compact.ShowScale && compact.ShowMaximize);
        Assert.False(compact.ShowMaximizeOnPlay);
        Assert.InRange(compact.OccupiedWidth, 1f, 360f);

        Assert.Equal(GameViewToolbarDensity.Narrow, narrow.Density);
        Assert.True(narrow.ShowPreset);
        Assert.False(narrow.ShowScale || narrow.ShowMaximize || narrow.ShowMaximizeOnPlay);
        Assert.Equal(200f, narrow.OccupiedWidth, precision: 3);

        Assert.Equal(GameViewToolbarDensity.OverflowOnly, overflowOnly.Density);
        Assert.False(overflowOnly.ShowPreset || overflowOnly.ShowScale || overflowOnly.ShowMaximize || overflowOnly.ShowMaximizeOnPlay);
        Assert.Equal(30f, overflowOnly.OccupiedWidth, precision: 3);
        Assert.Equal(1f, invalidWidth.OccupiedWidth, precision: 3);
    }

    /// <summary>验证真实窗口探针只在 Hosting commit 与面板 texture/revision/world rect 完全一致时通过。</summary>
    [Fact]
    public void ScriptedPresentationSnapshotRejectsMixedRevisionOrGeometry()
    {
        GamePresentationDescriptor descriptor = CreateSquareDescriptor();
        PresentationViewport worldContent = descriptor.WorldContentRect;
        GameViewViewportSnapshot matching = GameViewViewportSnapshot.Create(
            descriptor.PresentationWidth,
            descriptor.PresentationHeight,
            descriptor.PresentationRevision,
            in worldContent,
            Vector2.Zero,
            new Vector2(800f, 600f),
            Vector2.One,
            0f,
            Vector2.Zero);
        ScriptedGameViewPresentationSnapshot valid = ScriptedGameViewPresentationSnapshot.Create(
            "resolution-800-800",
            0f,
            maximizeOnPlay: true,
            isMaximized: true,
            in descriptor,
            in matching,
            new Vector2(1.5f));
        GameViewViewportSnapshot stale = matching with { PresentationRevision = descriptor.PresentationRevision + 1 };
        ScriptedGameViewPresentationSnapshot mixed = ScriptedGameViewPresentationSnapshot.Create(
            "resolution-800-800",
            0f,
            maximizeOnPlay: true,
            isMaximized: true,
            in descriptor,
            in stale,
            Vector2.One);

        Assert.True(valid.IsSynchronized);
        Assert.Equal(GamePresentationSource.EditorFixedResolution, valid.Source);
        Assert.Equal((800, 800), (valid.PresentationWidth, valid.PresentationHeight));
        Assert.True(valid.MaximizeOnPlay);
        Assert.True(valid.IsMaximized);
        Assert.False(mixed.IsSynchronized);
    }

    /// <summary>验证 ratio preset 受可用区域约束并拒绝 GPU 纹理溢出。</summary>
    [Fact]
    public void PresetResolverKeepsAspectInsideAvailableFramebufferAndRejectsGpuOverflow()
    {
        GameViewPresentationPreset ratio = GameViewPresentationPreset.BuiltIns.Single(
            static preset => preset.Id == "aspect-16-9");
        GameViewPresentationPreset oversized = new(
            "custom-too-large",
            "Too Large",
            GameViewPresentationPresetKind.FixedResolution,
            8192,
            4320);

        Assert.True(GameViewPresentationResolver.TryResolve(
            in ratio,
            1280,
            720,
            1000,
            800,
            4096,
            3,
            out GamePresentationOverride request,
            out string diagnostic), diagnostic);
        Assert.Equal((1000, 562), (request.Width, request.Height));
        Assert.Equal(GamePresentationSource.EditorAspectRatio, request.Source);
        Assert.False(GameViewPresentationResolver.TryResolve(
            in oversized,
            1280,
            720,
            1000,
            800,
            4096,
            4,
            out _,
            out diagnostic));
        Assert.Contains("renderer 上限", diagnostic, StringComparison.Ordinal);
    }

    private static GameViewPanel CreatePanel(EditorWorkspaceStore workspace, string projectPath)
    {
        return new GameViewPanel(
            static () => new RenderViewportTexture(1, 640, 360),
            static () => default,
            static () => (1280, 720),
            4096,
            workspace,
            projectPath);
    }

    private static GamePresentationDescriptor CreateSquareDescriptor()
    {
        DisplayMetricsSnapshot display = new(0, 1f, 1f, null, 1);
        return new GamePresentationDescriptor(
            640,
            360,
            800,
            800,
            PresentationViewport.Fit(640, 360, 800, 800),
            UiDisplayMetrics.FromRendering(800, 800, in display),
            GamePresentationSource.EditorFixedResolution,
            3,
            7);
    }

    private sealed class MutableDisplayMetricsSource(DisplayMetricsSnapshot value) : IDisplayMetricsSource
    {
        public DisplayMetricsSnapshot Value { get; set; } = value;

        public DisplayMetricsSnapshot Current { get; private set; }

        public DisplayMetricsSnapshot CommitFrameBoundary()
        {
            Current = Value;
            return Current;
        }
    }

    private sealed class MutablePresentationOverride : IGamePresentationOverride
    {
        public GamePresentationOverride? Request { get; set; }

        public bool TryGetPendingPresentation(out GamePresentationOverride request)
        {
            request = Request.GetValueOrDefault();
            return Request.HasValue;
        }
    }
}
