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
        VerifyOwner(directory.GetAccessControl(AccessControlSections.Owner), owner, directory.FullName);
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        directory.SetAccessControl(security);
        VerifyCurrentUserOnlyAcl(
            directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access),
            owner,
            directory.FullName,
            requireChildInheritance: true);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsFileAcl(FileInfo file)
    {
        SecurityIdentifier owner = GetCurrentOwner();
        VerifyOwner(file.GetAccessControl(AccessControlSections.Owner), owner, file.FullName);
        FileSecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        file.SetAccessControl(security);
        VerifyCurrentUserOnlyAcl(
            file.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access),
            owner,
            file.FullName,
            requireChildInheritance: false);
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier GetCurrentOwner()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        return identity.User ?? throw new InvalidOperationException("无法读取当前 Windows 用户 SID。");
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyOwner(
        FileSystemSecurity security,
        SecurityIdentifier expectedOwner,
        string path)
    {
        IdentityReference? actualOwner = security.GetOwner(typeof(SecurityIdentifier));
        if (actualOwner is not SecurityIdentifier actualOwnerSid || !actualOwnerSid.Equals(expectedOwner))
        {
            throw new UnauthorizedAccessException(
                $"Automation secure storage 拒绝非当前用户拥有的路径：{path}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyCurrentUserOnlyAcl(
        FileSystemSecurity security,
        SecurityIdentifier owner,
        string path,
        bool requireChildInheritance)
    {
        VerifyOwner(security, owner, path);
        FileSystemAccessRule[] rules =
        [
            .. security.GetAccessRules(
                    includeExplicit: true,
                    includeInherited: true,
                    typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>(),
        ];
        InheritanceFlags expectedInheritance = requireChildInheritance
            ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
            : InheritanceFlags.None;
        bool validRule = rules.Length == 1 &&
            rules[0].IdentityReference.Equals(owner) &&
            rules[0].AccessControlType == AccessControlType.Allow &&
            !rules[0].IsInherited &&
            rules[0].FileSystemRights.HasFlag(FileSystemRights.FullControl) &&
            rules[0].InheritanceFlags == expectedInheritance &&
            rules[0].PropagationFlags == PropagationFlags.None;
        if (!security.AreAccessRulesProtected || !validRule)
        {
            throw new UnauthorizedAccessException(
                $"Automation secure storage 无法验证 current-user-only ACL：{path}");
        }
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
