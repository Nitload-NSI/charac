using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Configuration;
using Renci.SshNet;

namespace Charac.Server.Ssh;

/// <summary>Loads registered login keys from a service-owned directory, never from HTTP input.</summary>
internal sealed class SshKeyStore
{
    public string? DirectoryPath { get; }

    public SshKeyStore(IConfiguration configuration)
    {
        var configured = configuration["ssh_keys:directory"]?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(configured))
            return;
        if (!Path.IsPathFullyQualified(configured))
            throw new InvalidOperationException("[ssh_keys] directory must be an absolute path.");
        DirectoryPath = Path.GetFullPath(configured);
    }

    public PrivateKeyFile Load(string fileName)
    {
        var path = Resolve(fileName);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 1 or > 65536 ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The SSH login key file is missing, unsafe or too large.");
        if (OperatingSystem.IsWindows())
        {
            CheckWindowsAcl(new DirectoryInfo(DirectoryPath!).GetAccessControl());
            CheckWindowsAcl(info.GetAccessControl());
        }
        if (!OperatingSystem.IsWindows() &&
            (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite |
                UnixFileMode.OtherExecute)) != 0)
            throw new InvalidOperationException("The SSH login key file must be private to the service account.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new PrivateKeyFile(stream);
    }

    [SupportedOSPlatform("windows")]
    internal static void CheckWindowsAcl(FileSystemSecurity security)
    {
        var account = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The SSH service account could not be identified.");
        HashSet<string> allowed = [account, "S-1-5-18", "S-1-5-32-544"];
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
            ?? throw new InvalidOperationException("The SSH login key owner could not be identified.");
        if (!allowed.Contains(owner.Value))
            throw new InvalidOperationException("The SSH login key owner must be the service account, SYSTEM or Administrators.");
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow &&
                !allowed.Contains(((SecurityIdentifier)rule.IdentityReference).Value))
                throw new InvalidOperationException("The SSH login key ACL grants access beyond the service account, SYSTEM and Administrators.");
        }
    }

    public string Resolve(string fileName)
    {
        if (DirectoryPath is null)
            throw new InvalidOperationException("Configure [ssh_keys] directory before using SSH login keys.");
        if (!IsValidFileName(fileName))
            throw new InvalidOperationException("Invalid SSH login key file name.");
        if (!Directory.Exists(DirectoryPath))
            throw new DirectoryNotFoundException($"SSH login key directory does not exist: {DirectoryPath}");
        if ((File.GetAttributes(DirectoryPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The SSH login key directory must not be a symbolic link or junction.");
        if (!OperatingSystem.IsWindows() &&
            (File.GetUnixFileMode(DirectoryPath) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            throw new InvalidOperationException("The SSH login key directory must not be writable by other users.");
        return Path.Combine(DirectoryPath, fileName);
    }

    public static bool IsValidFileName(string? fileName) =>
        fileName is { Length: > 0 and <= 255 } and not "." and not ".." &&
        fileName.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-');
}
