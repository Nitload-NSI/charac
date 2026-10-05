using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Configuration;
using Charac.Server.Ssh;
using Charac.Server.Data;

namespace Charac.Server.Tests;

public sealed class SshHostKeyTrustTests
{
    [Fact]
    public void ExactPinnedBlobIsTrustedButDifferentBlobIsRejected()
    {
        var first = MakeKey(1);
        var second = MakeKey(2);
        var trust = new SshHostKeyTrust([first.PublicKey]);

        Assert.True(trust.IsTrusted(first.Blob));
        Assert.False(trust.IsTrusted(second.Blob));
    }

    [Fact]
    public void MissingOrMalformedPinsFailClosed()
    {
        Assert.Throws<InvalidOperationException>(() => new SshHostKeyTrust([]));
        Assert.Throws<InvalidOperationException>(() => new SshHostKeyTrust(["ssh-ed25519 invalid-base64"]));
        Assert.Throws<InvalidOperationException>(() => new SshHostKeyTrust(["ssh-rsa " + MakeKey(1).PublicKey.Split(' ')[1]]));
    }

    [Fact]
    public void ProbeConfigurationAcceptsQuotedHostPublicKey()
    {
        var key = MakeKey(1);
        var ini = $"[ssh_probe]\naddress=10.10.0.101\nport=22\naccount=test\nhost_public_key=\"{key.PublicKey}\"\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(ini));
        var configuration = new ConfigurationBuilder().AddIniStream(input).Build();

        var settings = SshProbeSettings.FromConfiguration(configuration);

        Assert.Equal(key.PublicKey, settings.HostPublicKey);
    }

    [Fact]
    public void RegistrationAcceptsOneTrustedHostKeyLineAndRejectsMultipleKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), "rac-host-key-" + Guid.NewGuid().ToString("N") + ".pub");
        try
        {
            var key = MakeKey(3);
            File.WriteAllText(path, key.PublicKey + " host-comment\n");
            Assert.Equal(key.PublicKey, AccessRegistrationCommand.ReadHostKey(path));
            File.AppendAllText(path, MakeKey(4).PublicKey + "\n");
            Assert.Throws<InvalidOperationException>(() => AccessRegistrationCommand.ReadHostKey(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
    private static (string PublicKey, byte[] Blob) MakeKey(byte marker)
    {
        const string algorithm = "ssh-ed25519";
        var name = Encoding.ASCII.GetBytes(algorithm);
        var blob = new byte[4 + name.Length + 32];
        BinaryPrimitives.WriteUInt32BigEndian(blob, (uint)name.Length);
        name.CopyTo(blob, 4);
        blob.AsSpan(4 + name.Length).Fill(marker);
        return ($"{algorithm} {Convert.ToBase64String(blob)}", blob);
    }
}
