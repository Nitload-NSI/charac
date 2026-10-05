using System.Security.Cryptography;

namespace Charac.Client;

/// <summary>Stable, private identifier for one CLI installation; sent only in HTTPS headers.</summary>
internal static class ClientDeviceCredential
{
    public const string HeaderName = "X-RAC-Device";

    public static string LoadOrCreate(string? directory = null)
    {
        if (directory is null)
        {
            var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(applicationData))
                throw new InvalidOperationException("Cannot locate the user application-data directory.");
            directory = Path.Combine(applicationData, "workspace-access");
        }
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Cannot locate the user application-data directory.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "device-key");
        if (!File.Exists(path))
        {
            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var temporary = Path.Combine(directory, ".device-key-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
                    Share = FileShare.None };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var stream = new FileStream(temporary, options))
                using (var writer = new StreamWriter(stream))
                    writer.Write(secret);
                File.Move(temporary, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another CLI invocation created the credential first.
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The client device credential must not be a symbolic link.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) &
            (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
             UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new InvalidOperationException("The client device credential is readable by another Unix user.");
        var value = File.ReadAllText(path).Trim();
        if (value.Length != 43 || value.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
            throw new InvalidOperationException("The client device credential is invalid.");
        return value;
    }
}
