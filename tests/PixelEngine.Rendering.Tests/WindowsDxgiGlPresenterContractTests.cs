using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// Windows desktop GL 到 DXGI 的 capture-compatible 呈现路径源码契约。
/// </summary>
public sealed class WindowsDxgiGlPresenterContractTests
{
    /// <summary>
    /// 验证 presenter 只做 GPU 共享、包含完整的 lock/present/resize 生命周期，且不引入 CPU readback。
    /// </summary>
    [Fact]
    public void PresenterUsesGpuInteropAndOwnsCompleteBackBufferLifecycle()
    {
        string source = File.ReadAllText(ProjectPath(
            "src",
            "PixelEngine.Rendering",
            "WindowsDxgiGlPresenter.cs"));

        Assert.Contains("D3D11CreateDevice(", source, StringComparison.Ordinal);
        Assert.Contains("CreateSwapChainForHwnd", source, StringComparison.Ordinal);
        Assert.Contains("Format.R8G8B8A8_UNorm", source, StringComparison.Ordinal);
        Assert.Contains("bufferCount: 2", source, StringComparison.Ordinal);
        Assert.Contains("SwapEffect.FlipDiscard", source, StringComparison.Ordinal);
        Assert.Contains("GetBuffer<ID3D11Texture2D>(0)", source, StringComparison.Ordinal);
        Assert.Contains("_device.CreateTexture2D(sharedDescription)", source, StringComparison.Ordinal);
        Assert.Contains("BindFlags.RenderTarget | BindFlags.ShaderResource", source, StringComparison.Ordinal);
        Assert.Contains("CpuAccessFlags.None", source, StringComparison.Ordinal);
        Assert.Contains("Compiler.Compile(", source, StringComparison.Ordinal);
        Assert.Contains("height - 1u - (uint)position.y", source, StringComparison.Ordinal);
        Assert.Contains("_device.CreateShaderResourceView(_sharedColor, null)", source, StringComparison.Ordinal);
        Assert.Contains("_deviceContext.PSSetShaderResource(0, sharedColorView)", source, StringComparison.Ordinal);
        Assert.Contains("_deviceContext.Draw(3, 0)", source, StringComparison.Ordinal);
        Assert.Contains("wglDXOpenDeviceNV", source, StringComparison.Ordinal);
        Assert.Contains("wglDXCloseDeviceNV", source, StringComparison.Ordinal);
        Assert.Contains("wglDXRegisterObjectNV", source, StringComparison.Ordinal);
        Assert.Contains("wglDXUnregisterObjectNV", source, StringComparison.Ordinal);
        Assert.Contains("wglDXLockObjectsNV", source, StringComparison.Ordinal);
        Assert.Contains("wglDXUnlockObjectsNV", source, StringComparison.Ordinal);
        Assert.Contains("WglAccessWriteDiscardNv", source, StringComparison.Ordinal);
        Assert.Contains("_swapChain.Present", source, StringComparison.Ordinal);
        Assert.Contains("_swapChain.ResizeBuffers", source, StringComparison.Ordinal);
        Assert.Contains("RecreateBackBuffer(normalizedWidth, normalizedHeight", source, StringComparison.Ordinal);
        Assert.Contains("_unregisterObject(_interopDevice, _interopColor)", source, StringComparison.Ordinal);
        Assert.Contains("ReleaseTexture(ref _sharedColor)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadPixels", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTexImage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyResource", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Marshal.Copy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDC", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证窗口、render pipeline、overlay 和直接截图都统一使用实际 presentation framebuffer。
    /// </summary>
    [Fact]
    public void WindowAndFinalPassesUseSelectedPresentationFramebuffer()
    {
        string window = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderWindow.cs"));
        string present = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "PresentPass.cs"));
        string overlay = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "OverlayRenderer.cs"));
        string pipeline = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderPipeline.cs"));
        string editor = File.ReadAllText(ProjectPath("apps", "PixelEngine.Editor.Shell", "EditorShellApp.cs"));
        string demo = File.ReadAllText(ProjectPath("demo", "PixelEngine.Demo", "DemoProgram.cs"));

        Assert.Contains("public uint PresentationFramebuffer", window, StringComparison.Ordinal);
        Assert.Contains("public void BindPresentationFramebuffer()", window, StringComparison.Ordinal);
        Assert.Contains("_dxgiPresenter?.PrepareFrame", window, StringComparison.Ordinal);
        Assert.Contains("_dxgiPresenter.Present", window, StringComparison.Ordinal);
        Assert.Contains("WindowsDxgiGlPresenter.Create", window, StringComparison.Ordinal);
        Assert.Contains("_presentationFramebuffer = window.PresentationFramebuffer", present, StringComparison.Ordinal);
        Assert.Contains("BindFramebuffer(FramebufferTarget.Framebuffer, _presentationFramebuffer)", present, StringComparison.Ordinal);
        Assert.Contains("_presentationFramebuffer = presentationFramebuffer", overlay, StringComparison.Ordinal);
        Assert.Contains("new PresentPass(window, profile)", pipeline, StringComparison.Ordinal);
        Assert.Contains("window.PresentationFramebuffer", pipeline, StringComparison.Ordinal);
        Assert.Contains("shellWindow.Window.BindPresentationFramebuffer();", editor, StringComparison.Ordinal);
        Assert.Contains("window.BindPresentationFramebuffer();", demo, StringComparison.Ordinal);
    }

    private static string ProjectPath(params string[] segments)
    {
        string? current = AppContext.BaseDirectory;
        while (current is not null)
        {
            string candidate = Path.Combine(current, "PixelEngine.sln");
            if (File.Exists(candidate))
            {
                return Path.Combine([current, .. segments]);
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("未找到 PixelEngine repository root。");
    }
}
