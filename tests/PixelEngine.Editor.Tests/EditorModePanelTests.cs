using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// 编辑/运行模式面板与输入仲裁测试。
/// </summary>
public sealed class EditorModePanelTests
{
    /// <summary>
    /// 验证面板会把当前态 Play 与退出 Play 请求委托给服务。
    /// </summary>
    [Fact]
    public void EditorModePanelDelegatesPlayCurrentAndExit()
    {
        RecordingPlaySessionService service = new();
        EditorModePanel panel = new(service);

        panel.EnterPlayCurrent();
        panel.ExitPlay();

        Assert.Equal(["current", "exit"], service.Calls);
        Assert.Equal(EditorMode.Edit, panel.LastSnapshot.Mode);
    }

    /// <summary>
    /// 验证临时快照存储通过存读档服务保存和恢复固定槽位。
    /// </summary>
    [Fact]
    public void SaveLoadPlaySnapshotStoreUsesSaveLoadService()
    {
        RecordingSaveLoadService saveLoad = new();
        SaveLoadPlaySnapshotStore store = new(saveLoad, "temp");

        SaveLoadOperationResult save = store.SaveTemporarySnapshot();
        SaveLoadOperationResult load = store.RestoreTemporarySnapshot();

        Assert.True(save.Success);
        Assert.True(load.Success);
        Assert.Equal(["save:temp", "load:temp"], saveLoad.Calls);
    }

    /// <summary>
    /// 验证输入 gate 在 Edit/Play 下分别让编辑工具或游戏输入接管未被 ImGui 捕获的输入。
    /// </summary>
    [Theory]
    [InlineData(EditorMode.Edit, false, false, true, true, false, false)]
    [InlineData(EditorMode.Edit, true, false, false, true, false, false)]
    [InlineData(EditorMode.Play, false, true, false, false, true, false)]
    [InlineData(EditorMode.Play, false, false, false, false, true, true)]
    public void InputGateRoutesByModeAndImguiCapture(
        EditorMode mode,
        bool captureMouse,
        bool captureKeyboard,
        bool editorMouse,
        bool editorKeyboard,
        bool gameMouse,
        bool gameKeyboard)
    {
        EditorInputGate gate = new();
        EditorPlaySessionSnapshot session = new(mode, EditorPlaySource.CurrentState, false, string.Empty);

        EditorInputRoute route = gate.Route(session, new EditorInputSnapshot(captureMouse, captureKeyboard));

        Assert.Equal(editorMouse, route.AllowEditorMouse);
        Assert.Equal(editorKeyboard, route.AllowEditorKeyboard);
        Assert.Equal(gameMouse, route.AllowGameMouse);
        Assert.Equal(gameKeyboard, route.AllowGameKeyboard);
    }

    private sealed class RecordingPlaySessionService : IEditorPlaySessionService
    {
        private EditorMode _mode = EditorMode.Edit;

        public List<string> Calls { get; } = [];

        public EditorPlaySessionSnapshot Capture()
        {
            return new EditorPlaySessionSnapshot(_mode, EditorPlaySource.CurrentState, false, string.Empty);
        }

        public EditorPlaySessionResult EnterPlayCurrent()
        {
            Calls.Add("current");
            _mode = EditorMode.Play;
            return new EditorPlaySessionResult(true, Capture(), "current");
        }

        public EditorPlaySessionResult EnterPlayTemporary()
        {
            Calls.Add("temp");
            _mode = EditorMode.Play;
            return new EditorPlaySessionResult(true, Capture(), "temp");
        }

        public EditorPlaySessionResult ExitPlay()
        {
            Calls.Add("exit");
            _mode = EditorMode.Edit;
            return new EditorPlaySessionResult(true, Capture(), "exit");
        }
    }

    private sealed class RecordingSaveLoadService : ISaveLoadService
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<SaveSlotInfo> ListSaveSlots()
        {
            return [];
        }

        public SaveLoadOperationResult Save(string slotId)
        {
            Calls.Add($"save:{slotId}");
            return new SaveLoadOperationResult(true, "saved", null, null);
        }

        public SaveLoadOperationResult Load(string slotId)
        {
            Calls.Add($"load:{slotId}");
            return new SaveLoadOperationResult(true, "loaded", null, null);
        }
    }
}
