namespace PixelEngine.Rendering;

/// <summary>
/// 进程内 OpenGL native object live-count 计数器。
/// </summary>
/// <remarks>
/// 该计数器只覆盖引擎 GL 封装显式创建/删除的对象，用于定位托管封装层泄漏；
/// 它不能替代外部 driver 级 native leak detector。
/// </remarks>
public static class GlResourceTracker
{
    private static int _textures;
    private static int _buffers;
    private static int _framebuffers;
    private static int _shaderPrograms;
    private static int _computePrograms;
    private static int _shaders;
    private static int _vertexArrays;
    private static int _timerQueries;

    /// <summary>
    /// 获取当前进程内已跟踪 GL object 的 live-count 快照。
    /// </summary>
    /// <returns>按资源种类拆分的计数快照。</returns>
    public static GlResourceCounts Snapshot()
    {
        return new GlResourceCounts(
            Volatile.Read(ref _textures),
            Volatile.Read(ref _buffers),
            Volatile.Read(ref _framebuffers),
            Volatile.Read(ref _shaderPrograms),
            Volatile.Read(ref _computePrograms),
            Volatile.Read(ref _shaders),
            Volatile.Read(ref _vertexArrays),
            Volatile.Read(ref _timerQueries));
    }

    internal static void TrackCreated(GlResourceKind kind, uint handle)
    {
        if (handle == 0)
        {
            return;
        }

        _ = Interlocked.Increment(ref Counter(kind));
    }

    internal static void TrackDeleted(GlResourceKind kind, uint handle)
    {
        if (handle == 0)
        {
            return;
        }

        ref int counter = ref Counter(kind);
        int value = Interlocked.Decrement(ref counter);
        if (value >= 0)
        {
            return;
        }

        _ = Interlocked.Increment(ref counter);
        throw new InvalidOperationException($"GL resource tracker detected an unmatched delete for {kind}.");
    }

    private static ref int Counter(GlResourceKind kind)
    {
        switch (kind)
        {
            case GlResourceKind.Texture:
                return ref _textures;
            case GlResourceKind.Buffer:
                return ref _buffers;
            case GlResourceKind.Framebuffer:
                return ref _framebuffers;
            case GlResourceKind.ShaderProgram:
                return ref _shaderPrograms;
            case GlResourceKind.ComputeProgram:
                return ref _computePrograms;
            case GlResourceKind.Shader:
                return ref _shaders;
            case GlResourceKind.VertexArray:
                return ref _vertexArrays;
            case GlResourceKind.TimerQuery:
                return ref _timerQueries;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), "未知 GL 资源种类。");
        }
    }
}
