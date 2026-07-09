using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Windows 原生文件夹选择对话框封装，供项目路径与构建输出目录选取。
/// </summary>
internal static partial class NativeFolderPicker
{
    private const uint FosPickFolders = 0x00000020;
    private const uint FosForceFileSystem = 0x00000040;
    private const uint FosNoChangeDir = 0x00000008;
    private const uint FosPathMustExist = 0x00000800;
    private const uint SigDnFileSystemPath = 0x80058000;
    private const int HResultCancelled = unchecked((int)0x800704C7);
    private const int SOk = 0;
    private static readonly Guid FileOpenDialogClsid = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid ShellItemIid = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    public static bool TryPickFolder(string initialPath, out string selectedPath, out string diagnostic)
    {
        selectedPath = string.Empty;
        diagnostic = string.Empty;
        if (!OperatingSystem.IsWindows())
        {
            diagnostic = "当前平台暂未实现原生文件夹选择器，请直接粘贴路径。";
            return false;
        }

        try
        {
            Type dialogType = Type.GetTypeFromCLSID(FileOpenDialogClsid, throwOnError: true)
                ?? throw new NotSupportedException("无法创建 Windows FileOpenDialog。");
            object dialogObject = Activator.CreateInstance(dialogType)
                ?? throw new NotSupportedException("无法创建 Windows FileOpenDialog 实例。");
            using ComReleaser<IFileOpenDialog> dialog = new((IFileOpenDialog)dialogObject);
            dialog.Instance.SetOptions(FosPickFolders | FosForceFileSystem | FosNoChangeDir | FosPathMustExist);
            using ComReleaser<IShellItem>? defaultFolder = TryCreateShellFolder(initialPath);
            if (defaultFolder is not null)
            {
                dialog.Instance.SetDefaultFolder(defaultFolder.Instance);
                dialog.Instance.SetFolder(defaultFolder.Instance);
            }

            int hr = dialog.Instance.Show(IntPtr.Zero);
            if (hr == HResultCancelled)
            {
                return false;
            }

            Marshal.ThrowExceptionForHR(hr);
            dialog.Instance.GetResult(out IShellItem item);
            using ComReleaser<IShellItem> result = new(item);
            result.Instance.GetDisplayName(SigDnFileSystemPath, out IntPtr pathPointer);
            try
            {
                selectedPath = Marshal.PtrToStringUni(pathPointer) ?? string.Empty;
                return !string.IsNullOrWhiteSpace(selectedPath);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pathPointer);
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or NotSupportedException)
        {
            diagnostic = $"打开文件夹选择器失败：{exception.Message}";
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static ComReleaser<IShellItem>? TryCreateShellFolder(string initialPath)
    {
        if (string.IsNullOrWhiteSpace(initialPath))
        {
            return null;
        }

        string folderPath = initialPath;
        if (File.Exists(initialPath))
        {
            folderPath = Path.GetDirectoryName(Path.GetFullPath(initialPath)) ?? string.Empty;
        }
        else if (!Directory.Exists(initialPath))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return null;
        }

        Guid riid = ShellItemIid;
        int hr = SHCreateItemFromParsingName(Path.GetFullPath(folderPath), IntPtr.Zero, ref riid, out IntPtr itemPointer);
        if (hr != SOk || itemPointer == IntPtr.Zero)
        {
            if (itemPointer != IntPtr.Zero)
            {
                _ = Marshal.Release(itemPointer);
            }

            return null;
        }

        object shellItem;
        try
        {
            shellItem = Marshal.GetObjectForIUnknown(itemPointer);
        }
        finally
        {
            _ = Marshal.Release(itemPointer);
        }

        return new ComReleaser<IShellItem>((IShellItem)shellItem);
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid riid,
        out IntPtr item);

    private sealed class ComReleaser<T>(T instance) : IDisposable
        where T : class
    {
        public T Instance { get; } = instance;

        public void Dispose()
        {
            if (OperatingSystem.IsWindows())
            {
                _ = Marshal.FinalReleaseComObject(Instance);
            }
        }
    }

    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint fileTypes, IntPtr filterSpec);

        void SetFileTypeIndex(uint fileTypeIndex);

        void GetFileTypeIndex(out uint fileTypeIndex);

        void Advise(IntPtr events, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void SetDefaultFolder(IShellItem folder);

        void SetFolder(IShellItem folder);

        void GetFolder(out IShellItem folder);

        void GetCurrentSelection(out IShellItem selection);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem item);

        void AddPlace(IShellItem item, int place);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

        void Close(int hr);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(IntPtr filter);

        void GetResults(out IntPtr items);

        void GetSelectedItems(out IntPtr items);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid riid, out IntPtr ppv);

        void GetParent(out IShellItem parent);

        void GetDisplayName(uint sigdnName, out IntPtr name);

        void GetAttributes(uint sfgaoMask, out uint attributes);

        void Compare(IShellItem item, uint hint, out int order);
    }
}
