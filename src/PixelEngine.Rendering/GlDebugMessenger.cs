using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// OpenGL debug callback 注册句柄。
/// </summary>
public sealed unsafe class GlDebugMessenger : IDisposable
{
    private readonly GL _gl;
    private readonly DebugProc _callback;
    private readonly GCHandle _sinkHandle;
    private bool _disposed;

    private GlDebugMessenger(GL gl, Action<string> sink)
    {
        _gl = gl;
        _callback = OnDebugMessage;
        _sinkHandle = GCHandle.Alloc(sink);
        gl.Enable(EnableCap.DebugOutput);
        gl.DebugMessageCallback(_callback, (void*)GCHandle.ToIntPtr(_sinkHandle));
    }

    /// <summary>
    /// 尝试注册 OpenGL debug callback。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="capabilities">能力快照。</param>
    /// <param name="sink">诊断消息接收器。</param>
    /// <returns>注册成功时返回句柄，否则返回 null。</returns>
    public static GlDebugMessenger? TryCreate(GL gl, GlCapabilities capabilities, Action<string> sink)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(sink);

        bool supported = capabilities.Extensions.Contains("GL_KHR_debug", StringComparer.Ordinal) ||
            (!capabilities.IsGles && (capabilities.MajorVersion > 4 ||
                (capabilities.MajorVersion == 4 && capabilities.MinorVersion >= 3)));
        return supported ? new GlDebugMessenger(gl, sink) : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gl.DebugMessageCallback(null, null);
        _sinkHandle.Free();
        _disposed = true;
    }

    private static void OnDebugMessage(
        GLEnum source,
        GLEnum type,
        int id,
        GLEnum severity,
        int length,
        nint message,
        nint userParam)
    {
        if (userParam == 0)
        {
            return;
        }

        GCHandle handle = GCHandle.FromIntPtr(userParam);
        if (handle.Target is not Action<string> sink)
        {
            return;
        }

        string text = Marshal.PtrToStringUTF8(message, length) ?? string.Empty;
        sink($"GL debug [{severity}] {source}/{type}/{id}: {text}");
    }
}
