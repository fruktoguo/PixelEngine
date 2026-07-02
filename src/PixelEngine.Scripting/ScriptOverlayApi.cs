namespace PixelEngine.Scripting;

/// <summary>
/// 脚本可见的屏幕空间 overlay 绘制 API；命令只影响本帧渲染，不写入权威 cell 网格。
/// </summary>
public sealed class ScriptOverlayApi : IOverlayApi
{
    private readonly List<ScriptOverlayCommand> _commands = new(128);

    /// <summary>
    /// 当前待渲染 overlay 命令数量。
    /// </summary>
    public int CommandCount => _commands.Count;

    /// <inheritdoc />
    public void SolidRectangle(float x, float y, float width, float height, uint colorBgra)
    {
        Add(ScriptOverlayCommand.SolidRectangle(x, y, width, height, colorBgra));
    }

    /// <inheritdoc />
    public void OutlineRectangle(float x, float y, float width, float height, float thickness, uint colorBgra)
    {
        Add(ScriptOverlayCommand.OutlineRectangle(x, y, width, height, thickness, colorBgra));
    }

    /// <inheritdoc />
    public void Line(float startX, float startY, float endX, float endY, float thickness, uint colorBgra)
    {
        Add(ScriptOverlayCommand.Line(startX, startY, endX, endY, thickness, colorBgra));
    }

    /// <summary>
    /// 按索引读取一条 overlay 命令。
    /// </summary>
    /// <param name="index">命令索引。</param>
    /// <returns>overlay 命令。</returns>
    public ScriptOverlayCommand GetCommand(int index)
    {
        return _commands[index];
    }

    /// <summary>
    /// 清空当前帧已提交的 overlay 命令。
    /// </summary>
    public void Clear()
    {
        _commands.Clear();
    }

    private void Add(ScriptOverlayCommand command)
    {
        command.Validate();
        _commands.Add(command);
    }
}
