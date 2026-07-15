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
    string UiCommandId,
    string Action,
    string Key,
    bool Control,
    bool Shift,
    bool Alt,
    bool Super,
    string DisplayText,
    int KeyChord);

/// <summary>
/// 菜单、键盘调度和 Preferences 共用的快捷键真相源。
/// </summary>
internal static class EditorShortcutCatalog
{
    private static readonly EditorShortcutDefinition[] Definitions =
    [
        Create(EditorShortcutCommand.SaveScene, "shortcut.ctrl-s", "Save Scene", "S", ImGuiKey.S, control: true),
        Create(EditorShortcutCommand.SaveSceneAs, "shortcut.ctrl-shift-s", "Save Scene As", "S", ImGuiKey.S, control: true, shift: true),
        Create(EditorShortcutCommand.Undo, "shortcut.ctrl-z", "Undo", "Z", ImGuiKey.Z, control: true),
        Create(EditorShortcutCommand.Redo, "shortcut.ctrl-y", "Redo", "Y", ImGuiKey.Y, control: true),
        Create(EditorShortcutCommand.Duplicate, "shortcut.ctrl-d", "Duplicate", "D", ImGuiKey.D, control: true),
        Create(EditorShortcutCommand.TogglePlayMode, "shortcut.ctrl-p", "Play / Stop", "P", ImGuiKey.P, control: true),
        Create(EditorShortcutCommand.OpenBuildSettings, "shortcut.ctrl-shift-b", "Build Settings", "B", ImGuiKey.B, control: true, shift: true),
        Create(EditorShortcutCommand.BuildAndRun, "shortcut.ctrl-b", "Build And Run", "B", ImGuiKey.B, control: true),
        Create(EditorShortcutCommand.OpenPreferences, "shortcut.ctrl-comma", "Preferences", ",", ImGuiKey.Comma, control: true),
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
        string uiCommandId,
        string action,
        string keyName,
        ImGuiKey key,
        bool control = false,
        bool shift = false,
        bool alt = false,
        bool super = false)
    {
        ImGuiKey keyChord = key;
        if (control)
        {
            keyChord |= ImGuiKey.ModCtrl;
        }

        if (shift)
        {
            keyChord |= ImGuiKey.ModShift;
        }

        if (alt)
        {
            keyChord |= ImGuiKey.ModAlt;
        }

        if (super)
        {
            keyChord |= ImGuiKey.ModSuper;
        }

        string displayText = string.Join(
            '+',
            new[]
            {
                control ? "Ctrl" : null,
                shift ? "Shift" : null,
                alt ? "Alt" : null,
                super ? "Super" : null,
                keyName,
            }.Where(static part => part is not null));
        return new EditorShortcutDefinition(
            command,
            uiCommandId,
            action,
            keyName,
            control,
            shift,
            alt,
            super,
            displayText,
            (int)keyChord);
    }
}
