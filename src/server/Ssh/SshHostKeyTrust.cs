using System.Security.Cryptography;

namespace Charac.Server.Ssh;

internal sealed class SshHostKeyTrust
{
    private readonly byte[][] _keyBlobs;

    public SshHostKeyTrust(IReadOnlyList<string> publicKeys)
    {
        ArgumentNullException.ThrowIfNull(publicKeys);
        if (publicKeys.Count == 0)
            throw new InvalidOperationException("The target has no trusted SSH host keys.");

        _keyBlobs = publicKeys.Select(ParseKeyBlob).ToArray();
    }

    public bool IsTrusted(ReadOnlySpan<byte> presentedKey)
    {
        var trusted = false;
        foreach (var key in _keyBlobs)
            trusted |= CryptographicOperations.FixedTimeEquals(key, presentedKey);
        return trusted;
    }

    private static byte[] ParseKeyBlob(string publicKey)
    {
        var fields = publicKey.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || fields[0] is not ("ssh-ed25519" or "ecdsa-sha2-nistp256" or
            "ecdsa-sha2-nistp384" or "ecdsa-sha2-nistp521" or "ssh-rsa"))
            throw new InvalidOperationException("An SSH host key has an invalid or unsupported format. Use the '<algorithm> <base64>' line from the host public key file.");

        try
        {
            var blob = Convert.FromBase64String(fields[1]);
            if (blob.Length < 8 || blob.Length > 8192)
                throw new InvalidOperationException("An SSH host key blob has an invalid length.");
            var nameLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(blob);
            if (nameLength != fields[0].Length || blob.Length < 4 + nameLength ||
                !System.Text.Encoding.ASCII.GetBytes(fields[0]).AsSpan().SequenceEqual(blob.AsSpan(4, (int)nameLength)))
                throw new InvalidOperationException("An SSH host key algorithm does not match its blob.");
            return blob;
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("An SSH host key is not valid base64.", exception);
        }
    }
}
