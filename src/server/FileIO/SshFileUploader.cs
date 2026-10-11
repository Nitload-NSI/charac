using Renci.SshNet;
using Charac.Server.Authentication;
using Charac.Server.Ssh;

namespace Charac.Server.FileIO;

internal sealed class SshFileUploader(SshKeyStore keys)
{
    private const long MaximumFileSize = 512L * 1024 * 1024;

    public async Task<long> UploadAsync(SshAccessDecision decision, Stream source,
        string relativePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(source);
        if (!decision.CanUpload)
            throw new UnauthorizedAccessException("File upload is not authorized for this grant.");
        if (decision.SshLoginKeyFileName is null)
            throw new InvalidOperationException("File upload requires an SSH login key.");

        var remotePath = BuildRemotePath(decision.Account, relativePath);
        var privateKey = keys.Load(decision.SshLoginKeyFileName);
        try
        {
            var trust = new SshHostKeyTrust(decision.HostPublicKeys);
            var connection = new Renci.SshNet.ConnectionInfo(decision.Address, decision.Port, decision.Account,
                new PrivateKeyAuthenticationMethod(decision.Account, privateKey))
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            using var client = new SftpClient(connection);
            client.HostKeyReceived += (_, eventArgs) => eventArgs.CanTrust = trust.IsTrusted(eventArgs.HostKey);
            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                    client.Connect();
                    var resolvedPath = ResolveRemotePath(client, remotePath);
                    EnsureRemoteDirectory(client, resolvedPath);
                    if (client.Exists(resolvedPath))
                        throw new IOException("The remote file already exists.");
                    try
                    {
                        using var limited = new LimitedReadStream(source, MaximumFileSize);
                        await client.UploadFileAsync(limited, resolvedPath, false, null, cancellationToken);
                        return limited.BytesRead;
                    }
                    catch
                    {
                        if (client.IsConnected && client.Exists(resolvedPath))
                            client.DeleteFile(resolvedPath);
                        throw;
                    }
            }, cancellationToken);
        }
        finally
        {
            privateKey.Dispose();
        }
    }

    internal static string BuildRemotePath(string account, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255 ||
            fileName is "." or ".." || fileName.Contains('/') || fileName.Contains('\\') ||
            fileName.Contains('\0') || fileName.Any(char.IsControl))
            throw new ArgumentException("The file name must be a single safe file name.", nameof(fileName));
        return $"~/characfile/{fileName}";
    }

    private static string ResolveRemotePath(SftpClient client, string remotePath)
    {
        if (!remotePath.StartsWith("~/", StringComparison.Ordinal))
            return remotePath;
        return client.WorkingDirectory.TrimEnd('/') + remotePath[1..];
    }

    private static void EnsureRemoteDirectory(SftpClient client, string remotePath)
    {
        var directory = remotePath[..remotePath.LastIndexOf('/')];
        if (!client.Exists(directory))
            client.CreateDirectory(directory);
    }

    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => BytesRead;
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var remaining = limit - BytesRead;
            if (remaining <= 0)
                throw new IOException("The uploaded file exceeds the maximum size.");
            var count = inner.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
            BytesRead += count;
            return count;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = limit - BytesRead;
            if (remaining <= 0)
                throw new IOException("The uploaded file exceeds the maximum size.");
            var count = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, remaining)], cancellationToken);
            BytesRead += count;
            return count;
        }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
