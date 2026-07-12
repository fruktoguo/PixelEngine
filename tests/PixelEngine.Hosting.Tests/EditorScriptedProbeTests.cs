using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 验证 Editor Shell 脚本化真实窗口探针不会发布部分通过的假绿结果。
/// </summary>
public sealed class EditorScriptedProbeTests
{
    /// <summary>
    /// 菜单布局探针只有在全部真实动作都完成时才允许发布成功证据。
    /// </summary>
    [Fact]
    public void MenuLayoutProbeRequiresEveryAcceptanceCondition()
    {
        ScriptedMenuLayoutProbeState state = new()
        {
            Completed = true,
            RequiredPanelsShown = true,
            ResetRequested = true,
            CreatedObject = true,
            DuplicatedObject = true,
            RenamedObject = true,
            DeletedObject = true,
            NewSceneCreated = true,
            OpenedOriginalScene = true,
        };

        Assert.True(state.Succeeded);

        state.DeletedObject = false;

        Assert.False(state.Succeeded);
    }
}
