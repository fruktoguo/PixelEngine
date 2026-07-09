using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// RmlUi native renderer 的 OpenGL 入口注入冷路径。
/// </summary>
public static unsafe class RmlUiGlBootstrap
{
    /// <summary>
    /// 使用当前 <see cref="RenderWindow" /> 的 OpenGL context 初始化 native GL 函数表，并按 gate 选择 desktop/GLES shader profile。
    /// </summary>
    /// <param name="window">已初始化且当前线程拥有的渲染窗口。</param>
    /// <param name="version">native renderer 加载到的 GL 版本。</param>
    /// <returns>成功返回 true；缺 native 库、入口缺失、profile 拒绝或 GL 函数不可用时返回 false。</returns>
    public static bool TryLoad(RenderWindow window, out RmlUiGlVersion version)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!RmlUiNativeProfileGate.CanUseNativeRenderer(window.Backend, window.Capabilities, out _))
        {
            version = default;
            return false;
        }

        RmlUiNativeProfileDecision decision = RmlUiNativeProfileGate.Evaluate(window.Backend, window.Capabilities);
        try
        {
            int profileOk = RmlUiNative.SetRendererProfile(RmlUiNativeProfileGate.ToNativeProfileId(decision.RequestedProfile));
            if (profileOk != 1)
            {
                version = default;
                return false;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            version = default;
            return false;
        }

        GCHandle handle = GCHandle.Alloc(window);
        try
        {
            int ok = RmlUiNative.LoadGl(&ResolveProc, GCHandle.ToIntPtr(handle), out int major, out int minor);
            version = ok == 1 ? new RmlUiGlVersion(major, minor) : default;
            return ok == 1;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            version = default;
            return false;
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// 在当前 OpenGL context 上探测 native renderer 能否真实创建并销毁。
    /// </summary>
    /// <param name="window">已初始化且当前线程拥有的渲染窗口。</param>
    /// <param name="version">native renderer 加载到的 GL 版本。</param>
    /// <returns>renderer 能创建且销毁则返回 true。</returns>
    public static bool TryProbeRenderer(RenderWindow window, out RmlUiGlVersion version)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!TryLoad(window, out version))
        {
            return false;
        }

        IntPtr renderer = IntPtr.Zero;
        try
        {
            renderer = RmlUiNative.CreateRenderer(window.Width, window.Height);
            return renderer != IntPtr.Zero;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            version = default;
            return false;
        }
        finally
        {
            if (renderer != IntPtr.Zero)
            {
                RmlUiNative.DestroyRenderer(renderer);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr ResolveProc(IntPtr user, byte* name)
    {
        if (user == IntPtr.Zero || name is null)
        {
            return IntPtr.Zero;
        }

        GCHandle handle = GCHandle.FromIntPtr(user);
        if (handle.Target is not RenderWindow window)
        {
            return IntPtr.Zero;
        }

        string? functionName = Marshal.PtrToStringUTF8((IntPtr)name);
        return !string.IsNullOrWhiteSpace(functionName) &&
            window.TryGetProcAddress(functionName, out IntPtr address)
            ? address
            : IntPtr.Zero;
    }
}

/// <summary>
/// RmlUi native renderer 加载到的 GL 版本。
/// </summary>
/// <param name="Major">主版本。</param>
/// <param name="Minor">次版本。</param>
public readonly record struct RmlUiGlVersion(int Major, int Minor);
