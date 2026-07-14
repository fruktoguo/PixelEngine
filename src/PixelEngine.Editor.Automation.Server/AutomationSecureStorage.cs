using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace PixelEngine.Editor.Automation.Server;

internal static class AutomationSecureStorage
{
    public static void EnsurePrivateDirectory(string path)
    {
        string canonical = Path.GetFullPath(path);
        RejectRemoteOrDevicePath(canonical);
        // 先检查现存 ancestor，再创建最后几级目录，避免在 junction/symlink 目标中产生副作用后才拒绝。
        RejectReparseAncestors(canonical);
        DirectoryInfo directory = Directory.CreateDirectory(canonical);
        RejectReparseAncestors(directory.FullName);
        RejectReparsePoint(directory.FullName, directory.Attributes);
        if (OperatingSystem.IsWindows())
        {
            ApplyWindowsDirectoryAcl(directory);
        }
        else
        {
            File.SetUnixFileMode(
                directory.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public static void EnsurePrivateFile(string path)
    {
        string canonical = Path.GetFullPath(path);
        RejectRemoteOrDevicePath(canonical);
        string parent = Path.GetDirectoryName(canonical)
            ?? throw new IOException($"Automation secure storage 文件没有父目录：{canonical}");
        RejectReparseAncestors(parent);
        RejectReparsePoint(canonical, File.GetAttributes(canonical));
        if (OperatingSystem.IsWindows())
        {
            ApplyWindowsFileAcl(new FileInfo(canonical));
        }
        else
        {
            File.SetUnixFileMode(canonical, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsDirectoryAcl(DirectoryInfo directory)
    {
        SecurityIdentifier owner = GetCurrentOwner();
        DirectorySecurity security = new();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsFileAcl(FileInfo file)
    {
        SecurityIdentifier owner = GetCurrentOwner();
        FileSecurity security = new();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        file.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier GetCurrentOwner()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        return identity.User ?? throw new InvalidOperationException("无法读取当前 Windows 用户 SID。");
    }

    private static void RejectReparsePoint(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Automation secure storage 拒绝 reparse point：{path}");
        }
    }

    private static void RejectReparseAncestors(string path)
    {
        DirectoryInfo? current = new(Path.GetFullPath(path));
        while (current is not null)
        {
            if (current.Exists)
            {
                RejectReparsePoint(current.FullName, current.Attributes);
            }

            current = current.Parent;
        }
    }

    private static void RejectRemoteOrDevicePath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string? root = Path.GetPathRoot(path);
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || root is null ||
            new DriveInfo(root).DriveType == DriveType.Network)
        {
            throw new IOException($"Automation secure storage 拒绝 UNC/device path：{path}");
        }
    }
}
