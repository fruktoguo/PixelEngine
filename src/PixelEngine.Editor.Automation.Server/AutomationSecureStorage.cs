using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace PixelEngine.Editor.Automation.Server;

internal static class AutomationSecureStorage
{
    public static void EnsurePrivateDirectory(string path)
    {
        DirectoryInfo directory = Directory.CreateDirectory(path);
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
        RejectReparsePoint(path, File.GetAttributes(path));
        if (OperatingSystem.IsWindows())
        {
            ApplyWindowsFileAcl(new FileInfo(path));
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
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
}
