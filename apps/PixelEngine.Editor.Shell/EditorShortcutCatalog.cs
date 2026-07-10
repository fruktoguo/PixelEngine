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
        return ImGui.Shortcut(Get(command).KeyChord);
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
