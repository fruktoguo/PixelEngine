using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using PixelEngine.Editor.Shell;
using PixelEngine.Rendering;
using PixelEngine.Testing;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 系统 file-drop 的窗口坐标桥测试。
/// </summary>
public sealed partial class EditorExternalAssetDropConnectorTests
{
    /// <summary>
    /// Silk 逻辑鼠标坐标必须按各轴 framebuffer scale 独立映射，供 Project 命中高 DPI 矩形。
    /// </summary>
    [Fact]
    public void LogicalDropPointMapsToFramebufferPerAxis()
    {
        Vector2 point = EditorExternalAssetDropConnector.ToFramebufferPoint(
            new Vector2(100f, 80f),
            scaleX: 1.5f,
            scaleY: 2f);

        Assert.Equal(new Vector2(150f, 160f), point);
    }

    /// <summary>
    /// Windows 原生 WM_DROPFILES 必须经 Silk.NET、Editor connector 与 Project import 完整落盘，
    /// 并把成功结果写入 Console；该探针不绕过生产事件或直接调用导入方法。
    /// </summary>
    [NativeSmokeFact]
    [Trait("Category", "NativeSmoke")]
    public void NativeWindowDropImportsExternalTextureIntoVisibleProjectPanel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "Project"), "Native Drop");
        string externalDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "External")).FullName;
        string sourcePath = Path.Combine(externalDirectory, "native-drop.png");
        File.WriteAllBytes(sourcePath, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+XfQ3WQAAAABJRU5ErkJggg=="));

        EditorShellApp app = EditorShellApp.CreateForTests();
        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine native editor file-drop smoke",
            Width = 1280,
            Height = 720,
            BackendPreference = RenderBackendPreference.Auto,
        });
        using EditorProjectSession session = EditorProjectSession.Open(project, window, app);
        for (int i = 0; i < 4; i++)
        {
            session.RunOneTick(1d / 60d);
        }

        // 默认布局的 Project 位于客户区中右下；使用逻辑坐标设置 Silk mouse，
        // connector 会按每轴 framebuffer scale 命中上一帧记录的 Project rectangle。
        window.Input.Mice[0].Position = new Vector2(
            window.LogicalWidth * 0.72f,
            window.LogicalHeight * 0.78f);
        Assert.True(window.TryGetWin32WindowHandle(out IntPtr hwnd));

        WindowsNativeFileDropProbe.Send(hwnd, sourcePath);
        for (int i = 0; i < 2; i++)
        {
            session.RunOneTick(1d / 60d);
        }

        string importedPath = Path.Combine(project.ContentRootPath, "native-drop.png");
        Assert.True(File.Exists(importedPath), importedPath);
        Assert.Equal(File.ReadAllBytes(sourcePath), File.ReadAllBytes(importedPath));
        Assert.Contains(app.ConsoleStore.Snapshot(), entry =>
            entry.Category == EditorConsoleCategory.Asset &&
            entry.Severity == EditorConsoleSeverity.Info &&
            entry.Source == "project-file-drop" &&
            entry.Text.Contains("发现 1", StringComparison.Ordinal) &&
            entry.Text.Contains("已导入 1", StringComparison.Ordinal) &&
            entry.Text.Contains("拒绝 0", StringComparison.Ordinal));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PixelEngine",
                "EditorExternalDropTests",
                Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private static partial class WindowsNativeFileDropProbe
    {
        private const uint GmemMoveable = 0x0002;
        private const uint GmemZeroInit = 0x0040;
        private const uint WmDropFiles = 0x0233;
        private const int DropFilesHeaderBytes = 20;

        public static void Send(IntPtr hwnd, string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            byte[] paths = Encoding.Unicode.GetBytes(path + "\0\0");
            IntPtr dropHandle = GlobalAlloc(
                GmemMoveable | GmemZeroInit,
                checked((nuint)(DropFilesHeaderBytes + paths.Length)));
            if (dropHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"GlobalAlloc 创建 HDROP 失败：{Marshal.GetLastWin32Error()}。");
            }

            bool ownershipTransferred = false;
            try
            {
                IntPtr dropData = GlobalLock(dropHandle);
                if (dropData == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"GlobalLock HDROP 失败：{Marshal.GetLastWin32Error()}。");
                }

                try
                {
                    Marshal.WriteInt32(dropData, 0, DropFilesHeaderBytes);
                    Marshal.WriteInt32(dropData, 16, 1); // DROPFILES.fWide = TRUE
                    Marshal.Copy(paths, 0, IntPtr.Add(dropData, DropFilesHeaderBytes), paths.Length);
                }
                finally
                {
                    _ = GlobalUnlock(dropHandle);
                }

                DragAcceptFiles(hwnd, accept: true);
                ownershipTransferred = true;
                _ = SendMessage(hwnd, WmDropFiles, dropHandle, IntPtr.Zero);
                // WM_DROPFILES 的接收方在解析完成后调用 DragFinish，并取得 HDROP 所有权。
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    _ = GlobalFree(dropHandle);
                }
            }
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr GlobalLock(IntPtr memory);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial int GlobalUnlock(IntPtr memory);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr GlobalFree(IntPtr memory);

        [LibraryImport("shell32.dll")]
        private static partial void DragAcceptFiles(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool accept);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        private static partial IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    }
}
