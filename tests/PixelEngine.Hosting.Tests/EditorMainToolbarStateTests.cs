using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Unity-like 顶部 Play/Pause/Step 纯状态测试。
/// </summary>
public sealed class EditorMainToolbarStateTests
{
    /// <summary>
    /// 验证 Play 与 Paused 都保持活动 Play 图标，点击时承担 Stop/退出语义。
    /// </summary>
    [Fact]
    public void PlayAndPausedStatesKeepPlayActiveAndToggleBackToEdit()
    {
        EditorMainToolbarState edit = Create(isPlaying: false, isPaused: false);
        EditorMainToolbarState play = Create(isPlaying: true, isPaused: false);
        EditorMainToolbarState paused = Create(isPlaying: false, isPaused: true);

        Assert.False(edit.IsPlaySessionActive);
        Assert.False(edit.ShouldExitPlayOnToggle);
        Assert.False(edit.CanPause);
        Assert.False(edit.CanStep);

        Assert.True(play.IsPlaySessionActive);
        Assert.True(play.ShouldExitPlayOnToggle);
        Assert.True(play.CanPause);
        Assert.True(play.CanStep);

        Assert.True(paused.IsPlaySessionActive);
        Assert.True(paused.ShouldExitPlayOnToggle);
        Assert.True(paused.CanPause);
        Assert.True(paused.CanStep);
    }

    /// <summary>
    /// 验证底部状态栏只承载工程、场景与对象摘要，Play 模式由独立着色标签表达。
    /// </summary>
    [Fact]
    public void StatusTextKeepsWorkspaceSummarySeparateFromModeLabel()
    {
        EditorMainToolbarState state = Create(isPlaying: true, isPaused: false) with
        {
            IsDirty = true,
            ObjectCount = 3,
        };

        Assert.Equal("Demo  |  Scene*  |  3 GameObjects", state.StatusText);
        Assert.DoesNotContain("Play", state.StatusText, StringComparison.Ordinal);
    }

    private static EditorMainToolbarState Create(bool isPlaying, bool isPaused)
    {
        return new EditorMainToolbarState(
            HasOpenProject: true,
            HasSession: true,
            isPlaying,
            isPaused,
            IsDirty: false,
            ProjectName: "Demo",
            SceneName: "Scene",
            ObjectCount: 1,
            Mode: isPlaying ? "Play" : isPaused ? "Paused" : "Edit");
    }
}
