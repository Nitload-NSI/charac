using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Extensions.Configuration;
using Charac.Server.Ssh;

namespace Charac.Server.Tests;

public sealed class SshKeyImporterTests
{
    [Fact]
    public void ImportsLocalPrivateKeyWithoutOverwritingAnExistingEndpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), "rac-key-import-" + Guid.NewGuid().ToString("N"));
        var managed = Path.Combine(root, "managed");
        Directory.CreateDirectory(managed);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var user = WindowsIdentity.GetCurrent().User!;
                var acl = new DirectorySecurity();
                acl.SetOwner(user);
                acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                acl.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                try
                {
                    new DirectoryInfo(managed).SetAccessControl(acl);
                }
                catch (UnauthorizedAccessException)
                {
                    Assert.Skip("The test runner cannot create a private NTFS key directory.");
                }
            }
            else
                File.SetUnixFileMode(managed, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ssh_keys:directory"] = managed
            }).Build();
            var store = new SshKeyStore(configuration);
            var importer = new SshKeyImporter(store);
            var source = Path.Combine(root, "source-key");
            using (var rsa = RSA.Create(2048))
                File.WriteAllText(source, rsa.ExportRSAPrivateKeyPem());

            var first = importer.Import("target-1", source);
            Assert.Equal(first, importer.Import("target-1", source));
            using (var loaded = store.Load(first))
                Assert.NotNull(loaded);

            using (var rsa = RSA.Create(2048))
                File.WriteAllText(source, rsa.ExportRSAPrivateKeyPem());
            Assert.Throws<InvalidOperationException>(() => importer.Import("target-1", source));
            using var original = store.Load(first);
            Assert.NotNull(original);
        }
        finally
        {
            foreach (var path in Directory.GetFiles(managed))
                File.Delete(path);
            Directory.Delete(managed);
            foreach (var path in Directory.GetFiles(root))
                File.Delete(path);
            Directory.Delete(root);
        }
    }
}
