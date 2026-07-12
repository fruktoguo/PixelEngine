using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell;

internal enum EditorShortcutCommand
{
    SaveScene,
    SaveSceneAs,
    Undo,
    Redo,
    Duplicate,
    TogglePlayMode,
    OpenBuildSettings,
    BuildAndRun,
    OpenPreferences,
}

internal readonly record struct EditorShortcutDefinition(
    EditorShortcutCommand Command,
    string Action,
    string DisplayText,
    int KeyChord);

/// <summary>
/// 菜单、键盘调度和 Preferences 共用的快捷键真相源。
/// </summary>
internal static class EditorShortcutCatalog
{
    private static readonly EditorShortcutDefinition[] Definitions =
    [
        Create(EditorShortcutCommand.SaveScene, "Save Scene", "Ctrl+S", ImGuiKey.ModCtrl | ImGuiKey.S),
        Create(EditorShortcutCommand.SaveSceneAs, "Save Scene As", "Ctrl+Shift+S", ImGuiKey.ModCtrl | ImGuiKey.ModShift | ImGuiKey.S),
        Create(EditorShortcutCommand.Undo, "Undo", "Ctrl+Z", ImGuiKey.ModCtrl | ImGuiKey.Z),
        Create(EditorShortcutCommand.Redo, "Redo", "Ctrl+Y", ImGuiKey.ModCtrl | ImGuiKey.Y),
        Create(EditorShortcutCommand.Duplicate, "Duplicate", "Ctrl+D", ImGuiKey.ModCtrl | ImGuiKey.D),
        Create(EditorShortcutCommand.TogglePlayMode, "Play / Stop", "Ctrl+P", ImGuiKey.ModCtrl | ImGuiKey.P),
        Create(EditorShortcutCommand.OpenBuildSettings, "Build Settings", "Ctrl+Shift+B", ImGuiKey.ModCtrl | ImGuiKey.ModShift | ImGuiKey.B),
        Create(EditorShortcutCommand.BuildAndRun, "Build And Run", "Ctrl+B", ImGuiKey.ModCtrl | ImGuiKey.B),
        Create(EditorShortcutCommand.OpenPreferences, "Preferences", "Ctrl+,", ImGuiKey.ModCtrl | ImGuiKey.Comma),
    ];

    public static ReadOnlySpan<EditorShortcutDefinition> All => Definitions;

    public static EditorShortcutDefinition Get(EditorShortcutCommand command)
    {
        for (int i = 0; i < Definitions.Length; i++)
        {
            if (Definitions[i].Command == command)
            {
                return Definitions[i];
            }
        }

        throw new ArgumentOutOfRangeException(nameof(command), command, "未知 Editor 快捷键命令。");
    }

    public static bool IsPressed(EditorShortcutCommand command)
    {
        // plan 19：Editor 命令属于全局工作台路由；不使用 RouteOverActive，
        // 让 InputText 继续拥有文本级 Ctrl+Z/Ctrl+Y/Ctrl+D 等按键语义。
        return ImGui.Shortcut(Get(command).KeyChord, ImGuiInputFlags.RouteGlobal);
    }

    private static EditorShortcutDefinition Create(
        EditorShortcutCommand command,
        string action,
        string displayText,
        ImGuiKey keyChord)
    {
        return new EditorShortcutDefinition(command, action, displayText, (int)keyChord);
    }
}
