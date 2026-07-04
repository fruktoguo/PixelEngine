using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorShellLayout
{
    private readonly EditorDockSpace _dockSpace = new();

    public EditorShellLayout(string layoutPath)
    {
        LayoutPath = layoutPath;
        _dockSpace.ResetLayoutState(buildDefaultLayout: !File.Exists(layoutPath));
    }

    public string LayoutPath { get; }

    public void ConfigureImGui()
    {
        ImGui.GetIO().ConfigFlags |= EditorDockSpace.BuildConfigFlags(enableMultiViewport: false);
    }

    public void DrawDockSpace()
    {
        _dockSpace.Draw();
    }

    public void ResetLayout()
    {
        if (File.Exists(LayoutPath))
        {
            File.Delete(LayoutPath);
        }

        _dockSpace.ResetLayoutState(buildDefaultLayout: true);
    }
}
