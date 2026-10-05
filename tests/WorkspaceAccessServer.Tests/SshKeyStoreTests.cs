using Microsoft.Extensions.Configuration;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Charac.Server.Ssh;

namespace Charac.Server.Tests;

public sealed class SshKeyStoreTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void WindowsAclRejectsBroadAccess()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var user = WindowsIdentity.GetCurrent().User!;
        var acl = new FileSecurity();
        acl.SetOwner(user);
        acl.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            AccessControlType.Allow));
        SshKeyStore.CheckWindowsAcl(acl);

        acl.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier("S-1-1-0"), FileSystemRights.Read,
            AccessControlType.Allow));
        Assert.Throws<InvalidOperationException>(() => SshKeyStore.CheckWindowsAcl(acl));
    }

    [Fact]
    public void KeyFileNamesCannotEscapeConfiguredDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rac-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ssh_keys:directory"] = directory
            }).Build();
            var store = new SshKeyStore(configuration);
            Assert.Equal(Path.Combine(directory, "fedora-key"), store.Resolve("fedora-key"));
            Assert.Throws<InvalidOperationException>(() => store.Resolve("../other"));
            Assert.Throws<InvalidOperationException>(() => store.Resolve("..\\other"));
            Assert.Throws<InvalidOperationException>(() => store.Resolve(".."));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }
}
