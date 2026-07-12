using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D11.Debug;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using Vortice.DXGI.Debug;
using static Vortice.Direct3D11.D3D11;

namespace PixelEngine.Rendering;

/// <summary>
/// 将 desktop GL presentation framebuffer 通过 WGL_NV_DX_interop2 映射到 D3D11 texture，
/// 再由 D3D11 fullscreen pass 纵向翻转并输出到同一 HWND 的 DXGI swap-chain。
/// 该路径不做 CPU framebuffer readback。
/// </summary>
internal sealed unsafe class WindowsDxgiGlPresenter : IDisposable
{
    private const uint GlRenderbuffer = 0x8D41;
    private const uint WglAccessWriteDiscardNv = 0x0002;
    private const string FlipVertexShaderSource = """
        float4 main(uint vertexId : SV_VertexID) : SV_Position
        {
            float2 corner = float2((vertexId << 1) & 2, vertexId & 2);
            return float4(corner.x * 2.0f - 1.0f, 1.0f - corner.y * 2.0f, 0.0f, 1.0f);
        }
        """;
    private const string FlipPixelShaderSource = """
        Texture2D<float4> Source : register(t0);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            uint width;
            uint height;
            Source.GetDimensions(width, height);
            uint2 source = uint2((uint)position.x, height - 1u - (uint)position.y);
            return Source.Load(int3(source, 0));
        }
        """;

    private readonly GL _gl;
    private readonly delegate* unmanaged[Stdcall]<IntPtr, int> _closeDevice;
    private readonly delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, IntPtr> _registerObject;
    private readonly delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> _unregisterObject;
    private readonly delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int> _lockObjects;
    private readonly delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int> _unlockObjects;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _deviceContext;
    private readonly IDXGISwapChain _swapChain;
    private IntPtr _interopDevice;
    private IntPtr _interopColor;
    private ID3D11Texture2D? _sharedColor;
    private ID3D11ShaderResourceView? _sharedColorView;
    private ID3D11RenderTargetView? _backBufferView;
    private ID3D11VertexShader? _flipVertexShader;
    private ID3D11PixelShader? _flipPixelShader;
    private uint _colorRenderbuffer;
    private uint _depthStencilRenderbuffer;
    private int _width;
    private int _height;
    private bool _locked;
    private bool _disposed;

    private WindowsDxgiGlPresenter(
        GL gl,
        delegate* unmanaged[Stdcall]<IntPtr, int> closeDevice,
        delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, IntPtr> registerObject,
        delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> unregisterObject,
        delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int> lockObjects,
        delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int> unlockObjects,
        ID3D11Device device,
        ID3D11DeviceContext deviceContext,
        IDXGISwapChain swapChain,
        IntPtr interopDevice)
    {
        _gl = gl;
        _closeDevice = closeDevice;
        _registerObject = registerObject;
        _unregisterObject = unregisterObject;
        _lockObjects = lockObjects;
        _unlockObjects = unlockObjects;
        _device = device;
        _deviceContext = deviceContext;
        _swapChain = swapChain;
        _interopDevice = interopDevice;
    }

    /// <summary>
    /// 当前由 D3D11 shared texture 承载的 GL presentation framebuffer。
    /// </summary>
    public uint Framebuffer { get; private set; }

    /// <summary>
    /// 创建 Windows capture-compatible presenter；任一步失败均由调用者回退普通 desktop GL。
    /// </summary>
    public static WindowsDxgiGlPresenter Create(
        GL gl,
        INativeContext nativeContext,
        IntPtr hwnd,
        int width,
        int height)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DXGI GL presenter 仅支持 Windows。");
        }

        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(nativeContext);
        _ = hwnd != IntPtr.Zero
            ? hwnd
            : throw new ArgumentException("DXGI GL presenter 需要有效 HWND。", nameof(hwnd));

        int normalizedWidth = Math.Max(1, width);
        int normalizedHeight = Math.Max(1, height);
        delegate* unmanaged[Stdcall]<IntPtr, IntPtr> openDevice =
            (delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)Resolve(nativeContext, "wglDXOpenDeviceNV");
        delegate* unmanaged[Stdcall]<IntPtr, int> closeDevice =
            (delegate* unmanaged[Stdcall]<IntPtr, int>)Resolve(nativeContext, "wglDXCloseDeviceNV");
        delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, IntPtr> registerObject =
            (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, IntPtr>)Resolve(nativeContext, "wglDXRegisterObjectNV");
        delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> unregisterObject =
            (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Resolve(nativeContext, "wglDXUnregisterObjectNV");
        delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int> lockObjects =
            (delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Resolve(nativeContext, "wglDXLockObjectsNV");
        delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int> unlockObjects =
            (delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Resolve(nativeContext, "wglDXUnlockObjectsNV");

        SwapChainDescription1 description = new(
            (uint)normalizedWidth,
            (uint)normalizedHeight,
            Format.R8G8B8A8_UNorm,
            stereo: false,
            Usage.RenderTargetOutput,
            bufferCount: 2,
            Scaling.Stretch,
            SwapEffect.FlipDiscard,
            AlphaMode.Ignore,
            SwapChainFlags.None);
        FeatureLevel[] featureLevels =
        [
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0,
        ];
        IDXGISwapChain? swapChain = null;
        ID3D11Device? device = null;
        ID3D11DeviceContext? deviceContext = null;
        IntPtr interopDevice = IntPtr.Zero;
        string creationStage = "D3D11CreateDevice";
        try
        {
            DeviceCreationFlags creationFlags = DeviceCreationFlags.BgraSupport;
            if (string.Equals(
                Environment.GetEnvironmentVariable("PIXELENGINE_D3D_DEBUG"),
                "1",
                StringComparison.Ordinal))
            {
                creationFlags |= DeviceCreationFlags.Debug;
            }

            D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Hardware,
                creationFlags,
                featureLevels,
                out device,
                out _,
                out deviceContext).CheckError();

            ID3D11Device resolvedDevice = device
                ?? throw new InvalidOperationException("D3D11 未返回 graphics device。");
            creationStage = "IDXGIFactory2.CreateSwapChainForHwnd";
            using (IDXGIDevice dxgiDevice = resolvedDevice.QueryInterface<IDXGIDevice>())
            using (IDXGIAdapter adapter = dxgiDevice.GetAdapter())
            using (IDXGIFactory2 factory = adapter.GetParent<IDXGIFactory2>())
            {
                swapChain = factory.CreateSwapChainForHwnd(
                    resolvedDevice,
                    hwnd,
                    description,
                    null,
                    null);
                creationStage = "IDXGIFactory.MakeWindowAssociation";
                factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter).CheckError();
            }

            IDXGISwapChain resolvedSwapChain = swapChain
                ?? throw new InvalidOperationException("D3D11 未返回 DXGI swap-chain。");
            ID3D11DeviceContext resolvedDeviceContext = deviceContext
                ?? throw new InvalidOperationException("D3D11 未返回 immediate context。");
            creationStage = "wglDXOpenDeviceNV";
            interopDevice = openDevice(resolvedDevice.NativePointer);
            if (interopDevice == IntPtr.Zero)
            {
                throw new InvalidOperationException("WGL_NV_DX_interop2 无法打开 D3D11 device；GL 与 DXGI adapter 可能不一致。");
            }

            WindowsDxgiGlPresenter presenter = new(
                gl,
                closeDevice,
                registerObject,
                unregisterObject,
                lockObjects,
                unlockObjects,
                resolvedDevice,
                resolvedDeviceContext,
                resolvedSwapChain,
                interopDevice);
            swapChain = null;
            device = null;
            deviceContext = null;
            interopDevice = IntPtr.Zero;
            try
            {
                creationStage = "WindowsDxgiGlPresenter.Initialize";
                presenter.Initialize(normalizedWidth, normalizedHeight);
                return presenter;
            }
            catch
            {
                presenter.Dispose();
                throw;
            }
        }
        catch (Exception ex)
        {
            if (interopDevice != IntPtr.Zero)
            {
                _ = closeDevice(interopDevice);
            }

            deviceContext?.Dispose();
            swapChain?.Dispose();
            device?.Dispose();
            throw new InvalidOperationException(
                $"DXGI presenter 创建阶段 {creationStage} 失败。{ReadDxgiDebugMessages()}",
                ex);
        }
    }

    /// <summary>
    /// 处理 framebuffer resize 并确保 interop FBO 已锁定、绑定供本帧绘制。
    /// </summary>
    public void PrepareFrame(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int normalizedWidth = Math.Max(1, width);
        int normalizedHeight = Math.Max(1, height);
        if (normalizedWidth != _width || normalizedHeight != _height)
        {
            RecreateBackBuffer(normalizedWidth, normalizedHeight, resizeSwapChain: true);
        }

        if (!_locked)
        {
            BeginFrame();
        }
        else
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
        }
    }

    /// <summary>
    /// 解锁 GL/D3D shared texture，经 GPU fullscreen flip 与 DXGI 呈现后重新锁定下一帧。
    /// </summary>
    public void Present(bool vsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EndFrame();
        ID3D11ShaderResourceView sharedColorView = _sharedColorView
            ?? throw new InvalidOperationException("DXGI presenter 尚未创建 shared color SRV。");
        ID3D11VertexShader vertexShader = _flipVertexShader
            ?? throw new InvalidOperationException("DXGI presenter 尚未创建 flip vertex shader。");
        ID3D11PixelShader pixelShader = _flipPixelShader
            ?? throw new InvalidOperationException("DXGI presenter 尚未创建 flip pixel shader。");
        ID3D11RenderTargetView backBufferView = _backBufferView
            ?? throw new InvalidOperationException("DXGI presenter 尚未创建 backbuffer RTV。");
        _deviceContext.OMSetRenderTargets(backBufferView, null);
        _deviceContext.RSSetViewport(0f, 0f, _width, _height, 0f, 1f);
        _deviceContext.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _deviceContext.VSSetShader(vertexShader);
        _deviceContext.PSSetShader(pixelShader);
        _deviceContext.PSSetShaderResource(0, sharedColorView);
        _deviceContext.Draw(3, 0);
        _deviceContext.ClearState();

        _swapChain.Present(vsync ? 1u : 0u, PresentFlags.None).CheckError();
        BeginFrame();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_locked)
        {
            EndFrame();
        }

        if (_interopColor != IntPtr.Zero)
        {
            _ = _unregisterObject(_interopDevice, _interopColor);
            _interopColor = IntPtr.Zero;
        }

        _sharedColorView?.Dispose();
        _sharedColorView = null;
        ReleaseTexture(ref _sharedColor);

        if (Framebuffer != 0)
        {
            _gl.DeleteFramebuffer(Framebuffer);
            Framebuffer = 0;
        }

        if (_depthStencilRenderbuffer != 0)
        {
            _gl.DeleteRenderbuffer(_depthStencilRenderbuffer);
            _depthStencilRenderbuffer = 0;
        }

        if (_colorRenderbuffer != 0)
        {
            _gl.DeleteRenderbuffer(_colorRenderbuffer);
            _colorRenderbuffer = 0;
        }

        if (_interopDevice != IntPtr.Zero)
        {
            _ = _closeDevice(_interopDevice);
            _interopDevice = IntPtr.Zero;
        }

        _deviceContext.ClearState();
        _deviceContext.Flush();
        ReleaseBackBufferViews();
        _flipPixelShader?.Dispose();
        _flipPixelShader = null;
        _flipVertexShader?.Dispose();
        _flipVertexShader = null;
        _swapChain.Dispose();
        _deviceContext.Dispose();
        _device.Dispose();
        _disposed = true;
    }

    private void BeginFrame()
    {
        if (_locked || _interopColor == IntPtr.Zero)
        {
            return;
        }

        IntPtr interopColor = _interopColor;
        if (_lockObjects(_interopDevice, 1, &interopColor) == 0)
        {
            throw new InvalidOperationException("wglDXLockObjectsNV 锁定 DXGI backbuffer 失败。");
        }

        _locked = true;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
    }

    private void EndFrame()
    {
        if (!_locked)
        {
            return;
        }

        _gl.Flush();
        IntPtr interopColor = _interopColor;
        if (_unlockObjects(_interopDevice, 1, &interopColor) == 0)
        {
            throw new InvalidOperationException("wglDXUnlockObjectsNV 解锁 DXGI backbuffer 失败。");
        }

        _locked = false;
    }

    private void RecreateBackBuffer(int width, int height, bool resizeSwapChain)
    {
        if (_locked)
        {
            EndFrame();
        }

        if (_interopColor != IntPtr.Zero)
        {
            // 先从 FBO 解绑，再 unregister；否则部分 WDDM 驱动会延迟持有 shared texture。
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
            _gl.FramebufferRenderbuffer(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                RenderbufferTarget.Renderbuffer,
                0);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Finish();
            if (_unregisterObject(_interopDevice, _interopColor) == 0)
            {
                throw new InvalidOperationException("wglDXUnregisterObjectNV 注销旧 DXGI backbuffer 失败。");
            }

            _interopColor = IntPtr.Zero;
            _sharedColorView?.Dispose();
            _sharedColorView = null;
            ReleaseTexture(ref _sharedColor);
            _gl.DeleteRenderbuffer(_colorRenderbuffer);
            _colorRenderbuffer = _gl.GenRenderbuffer();
        }

        if (resizeSwapChain)
        {
            _deviceContext.ClearState();
            _deviceContext.Flush();
            ReleaseBackBufferViews();
            try
            {
                _swapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None).CheckError();
            }
            catch (Exception ex)
            {
                ReportLiveD3D11Objects();
                throw new InvalidOperationException(
                    $"DXGI resize 失败。{ReadD3D11DebugMessages()} {ReadDxgiDebugMessages()}",
                    ex);
            }
        }

        if (_backBufferView is null)
        {
            CreateBackBufferView();
        }

        Texture2DDescription sharedDescription = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };
        _sharedColor = _device.CreateTexture2D(sharedDescription);
        _sharedColorView = _device.CreateShaderResourceView(_sharedColor, null);
        _interopColor = _registerObject(
            _interopDevice,
            _sharedColor.NativePointer,
            _colorRenderbuffer,
            GlRenderbuffer,
            WglAccessWriteDiscardNv);
        if (_interopColor == IntPtr.Zero)
        {
            throw new InvalidOperationException("wglDXRegisterObjectNV 注册 D3D11 shared texture 失败。");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer,
            _colorRenderbuffer);
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthStencilRenderbuffer);
        _gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer,
            InternalFormat.Depth24Stencil8,
            (uint)width,
            (uint)height);
        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer,
            _depthStencilRenderbuffer);
        GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"DXGI interop framebuffer 不完整：{status}。");
        }

        _width = width;
        _height = height;
    }

    private void CreateBackBufferView()
    {
        ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        try
        {
            _backBufferView = _device.CreateRenderTargetView(backBuffer, null);
        }
        finally
        {
            _ = backBuffer.Release();
            backBuffer.NativePointer = IntPtr.Zero;
        }
    }

    private void ReleaseBackBufferViews()
    {
        _backBufferView?.Dispose();
        _backBufferView = null;
    }

    private static void ReleaseTexture(ref ID3D11Texture2D? texture)
    {
        ID3D11Texture2D? resource = texture;
        texture = null;
        if (resource is null)
        {
            return;
        }

        _ = resource.Release();
        resource.NativePointer = IntPtr.Zero;
    }

    private static IntPtr Resolve(INativeContext nativeContext, string name)
    {
        return nativeContext.TryGetProcAddress(name, out IntPtr address) && address != IntPtr.Zero
            ? address
            : throw new InvalidOperationException($"desktop GL driver 未提供 {name}；无法启用 capture-compatible DXGI presenter。");
    }

    private void Initialize(int width, int height)
    {
        ShaderFlags shaderFlags =
            ShaderFlags.OptimizationLevel3 |
            ShaderFlags.EnableStrictness |
            ShaderFlags.WarningsAreErrors;
        ReadOnlyMemory<byte> vertexBytecode = Compiler.Compile(
            FlipVertexShaderSource,
            "main",
            "PixelEngine.DxgiPresent.vs.hlsl",
            "vs_5_0",
            shaderFlags,
            EffectFlags.None);
        ReadOnlyMemory<byte> pixelBytecode = Compiler.Compile(
            FlipPixelShaderSource,
            "main",
            "PixelEngine.DxgiPresent.ps.hlsl",
            "ps_5_0",
            shaderFlags,
            EffectFlags.None);
        _flipVertexShader = _device.CreateVertexShader(vertexBytecode.Span, null);
        _flipPixelShader = _device.CreatePixelShader(pixelBytecode.Span, null);
        _colorRenderbuffer = _gl.GenRenderbuffer();
        _depthStencilRenderbuffer = _gl.GenRenderbuffer();
        Framebuffer = _gl.GenFramebuffer();
        RecreateBackBuffer(width, height, resizeSwapChain: false);
        BeginFrame();
    }

    private string ReadD3D11DebugMessages()
    {
        using ID3D11InfoQueue? queue = _device.QueryInterfaceOrNull<ID3D11InfoQueue>();
        if (queue is null)
        {
            return "D3D11 debug info queue 不可用。";
        }

        List<string> messages = [];
        ulong count = queue.NumStoredMessages;
        ulong first = count > 16 ? count - 16 : 0;
        for (ulong i = first; i < count; i++)
        {
            Message message = queue.GetMessage(i);
            messages.Add($"{message.Severity}/{message.Id}: {message.Description}");
        }

        return messages.Count == 0
            ? "D3D11 debug info queue 为空。"
            : string.Join(" | ", messages);
    }

    private void ReportLiveD3D11Objects()
    {
        using ID3D11Debug? debug = _device.QueryInterfaceOrNull<ID3D11Debug>();
        debug?.ReportLiveDeviceObjects(
            ReportLiveDeviceObjectFlags.Detail |
            ReportLiveDeviceObjectFlags.IgnoreInternal);
    }

    private static string ReadDxgiDebugMessages()
    {
        IDXGIInfoQueue? queue = null;
        try
        {
            queue = Vortice.DXGI.DXGI.DXGIGetDebugInterface1<IDXGIInfoQueue>();
            List<string> messages = [];
            ulong count = queue.GetNumStoredMessages(Vortice.DXGI.DXGI.DebugAll);
            ulong first = count > 16 ? count - 16 : 0;
            for (ulong i = first; i < count; i++)
            {
                InfoQueueMessage message = queue.GetMessage(Vortice.DXGI.DXGI.DebugAll, i);
                messages.Add($"{message.Severity}/{message.Id}: {message.Description}");
            }

            return messages.Count == 0
                ? "DXGI debug info queue 为空。"
                : string.Join(" | ", messages);
        }
        catch
        {
            return "DXGI debug info queue 不可用。";
        }
        finally
        {
            queue?.Dispose();
        }
    }
}
