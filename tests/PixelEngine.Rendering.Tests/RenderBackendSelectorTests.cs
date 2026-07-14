using Silk.NET.Windowing;
using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// 渲染后端选择器测试：偏好顺序与自动回退。
/// </summary>
public sealed class RenderBackendSelectorTests
{
    /// <summary>
    /// 验证Auto Preference Tries Desktop Then Gles。
    /// </summary>
    [Fact]
    public void AutoPreferenceTriesDesktopThenGles()
    {
        ReadOnlySpan<RenderBackend> order = RenderBackendSelector.GetAttemptOrder(RenderBackendPreference.Auto);

        Assert.Equal(2, order.Length);
        Assert.Equal(RenderBackend.DesktopGl33, order[0]);
        Assert.Equal(RenderBackend.GlEs30Angle, order[1]);
    }

    /// <summary>
    /// 验证系统捕获兼容偏好优先 desktop GL + DXGI interop，并保留普通桌面 GL 回退。
    /// </summary>
    [Fact]
    public void CaptureCompatiblePreferenceTriesDxgiInteropThenDesktop()
    {
        ReadOnlySpan<RenderBackend> order = RenderBackendSelector.GetAttemptOrder(RenderBackendPreference.CaptureCompatible);

        Assert.Equal(2, order.Length);
        Assert.Equal(RenderBackend.DesktopGl33DxgiInterop, order[0]);
        Assert.Equal(RenderBackend.DesktopGl33, order[1]);
    }

    /// <summary>
    /// 验证Desktop Options Request Open Gl33Core。
    /// </summary>
    [Fact]
    public void DesktopOptionsRequestOpenGl33Core()
    {
        RenderWindowOptions options = new()
        {
            Width = 320,
            Height = 180,
            Title = "render-test",
            EnableDebugContext = true,
        };

        WindowOptions windowOptions = RenderBackendSelector.CreateWindowOptions(options, RenderBackend.DesktopGl33);

        Assert.Equal(ContextAPI.OpenGL, windowOptions.API.API);
        Assert.Equal(ContextProfile.Core, windowOptions.API.Profile);
        Assert.Equal(ContextFlags.Debug, windowOptions.API.Flags);
        Assert.Equal(new APIVersion(3, 3), windowOptions.API.Version);
        Assert.Equal("render-test", windowOptions.Title);
        Assert.Equal(320, windowOptions.Size.X);
        Assert.Equal(180, windowOptions.Size.Y);
        Assert.True(windowOptions.VSync);
        Assert.Equal(0, windowOptions.FramesPerSecond);
        Assert.Equal(0, windowOptions.UpdatesPerSecond);
        Assert.False(windowOptions.ShouldSwapAutomatically);
    }

    /// <summary>
    /// 验证 DXGI interop 后端仍创建 desktop OpenGL 3.3 core context，避免为捕获兼容性改变权威渲染 API。
    /// </summary>
    [Fact]
    public void DxgiInteropOptionsRequestDesktopOpenGl33Core()
    {
        RenderWindowOptions options = new()
        {
            EnableDebugContext = true,
        };

        WindowOptions windowOptions = RenderBackendSelector.CreateWindowOptions(
            options,
            RenderBackend.DesktopGl33DxgiInterop);

        Assert.Equal(ContextAPI.OpenGL, windowOptions.API.API);
        Assert.Equal(ContextProfile.Core, windowOptions.API.Profile);
        Assert.Equal(ContextFlags.Debug, windowOptions.API.Flags);
        Assert.Equal(new APIVersion(3, 3), windowOptions.API.Version);
        Assert.False(windowOptions.ShouldSwapAutomatically);
    }

    /// <summary>
    /// 验证Gles Options Request Open Gles30。
    /// </summary>
    [Fact]
    public void GlesOptionsRequestOpenGles30()
    {
        RenderWindowOptions options = new()
        {
            EnableDebugContext = false,
        };

        WindowOptions windowOptions = RenderBackendSelector.CreateWindowOptions(options, RenderBackend.GlEs30Angle);

        Assert.Equal(ContextAPI.OpenGLES, windowOptions.API.API);
        Assert.Equal(ContextProfile.Core, windowOptions.API.Profile);
        Assert.Equal(ContextFlags.Default, windowOptions.API.Flags);
        Assert.Equal(new APIVersion(3, 0), windowOptions.API.Version);
    }

    /// <summary>
    /// 验证Window Timing Options Are Forwarded To Silk。
    /// </summary>
    [Fact]
    public void WindowTimingOptionsAreForwardedToSilk()
    {
        RenderWindowOptions options = new()
        {
            VSync = false,
            FramesPerSecond = 144,
            UpdatesPerSecond = 60,
        };

        WindowOptions windowOptions = RenderBackendSelector.CreateWindowOptions(options, RenderBackend.DesktopGl33);

        Assert.False(windowOptions.VSync);
        Assert.Equal(144, windowOptions.FramesPerSecond);
        Assert.Equal(60, windowOptions.UpdatesPerSecond);
        Assert.False(windowOptions.ShouldSwapAutomatically);
    }

    /// <summary>
    /// 验证Rejects Invalid Window Timing Rates。
    /// </summary>
    [Fact]
    public void RejectsInvalidWindowTimingRates()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RenderBackendSelector.CreateWindowOptions(
            new RenderWindowOptions { FramesPerSecond = double.NaN },
            RenderBackend.DesktopGl33));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RenderBackendSelector.CreateWindowOptions(
            new RenderWindowOptions { UpdatesPerSecond = -1 },
            RenderBackend.DesktopGl33));
    }

    /// <summary>Editor workspace placement 必须在创建平台窗口前完整转发。</summary>
    [Fact]
    public void InitialPlacementAndStateAreForwardedToSilk()
    {
        RenderWindowOptions options = new()
        {
            PositionX = -900,
            PositionY = 40,
            InitialState = RenderWindowState.Maximized,
        };

        WindowOptions actual = RenderBackendSelector.CreateWindowOptions(
            options,
            RenderBackend.DesktopGl33);

        Assert.Equal(-900, actual.Position.X);
        Assert.Equal(40, actual.Position.Y);
        Assert.Equal(WindowState.Maximized, actual.WindowState);
    }

    /// <summary>不完整位置或与 Player mode 冲突的状态必须在建窗前失败。</summary>
    [Fact]
    public void InvalidInitialPlacementOrConflictingStateIsRejected()
    {
        _ = Assert.Throws<ArgumentException>(() => RenderBackendSelector.CreateWindowOptions(
            new RenderWindowOptions { PositionX = 1 },
            RenderBackend.DesktopGl33));
        _ = Assert.Throws<ArgumentException>(() => RenderBackendSelector.CreateWindowOptions(
            new RenderWindowOptions
            {
                WindowMode = PlayerWindowMode.MaximizedWindow,
                InitialState = RenderWindowState.Maximized,
            },
            RenderBackend.DesktopGl33));
    }
}
