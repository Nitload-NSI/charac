using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;

namespace Charac.Server.Ssh;

/// <summary>Copies a locally supplied login key into the service-owned key directory.</summary>
internal sealed class SshKeyImporter(SshKeyStore store)
{
    public string Import(string endpointName, string sourcePath, string? existingFileName = null)
    {
        if (string.IsNullOrWhiteSpace(endpointName) || endpointName.Length > 128 ||
            endpointName.Any(char.IsControl))
            throw new ArgumentException("The endpoint name must be nonempty and at most 128 characters.");
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Provide a local private key path.");

        var source = new FileInfo(Path.GetFullPath(sourcePath));
        if (!source.Exists || source.Length is < 1 or > 65536 ||
            (source.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The source SSH private key is missing, unsafe or too large.");

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpointName)));
        var fileName = existingFileName ?? "endpoint-" + digest;
        var destination = store.Resolve(fileName);
        if (OperatingSystem.IsWindows())
            SshKeyStore.CheckWindowsAcl(new DirectoryInfo(store.DirectoryPath!).GetAccessControl());

        using (var validationStream = new FileStream(source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var parsed = new PrivateKeyFile(validationStream))
        {
            // Validate before writing any key material into the managed directory.
        }
        using var input = new FileStream(source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is < 1 or > 65536)
            throw new InvalidOperationException("The source SSH private key is missing or too large.");

        if (File.Exists(destination))
        {
            using var registered = store.Load(fileName);
            using var existing = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(input), SHA256.HashData(existing)))
                throw new InvalidOperationException("This endpoint already has a different managed login key.");
            return fileName;
        }

        var temporaryName = ".import-" + Guid.NewGuid().ToString("N");
        var temporaryPath = store.Resolve(temporaryName);
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var output = new FileStream(temporaryPath, options))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            using (var imported = store.Load(temporaryName))
            {
                // Recheck the destination ACL/mode and parsed content before publishing it.
            }
            File.Move(temporaryPath, destination);
            return fileName;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
