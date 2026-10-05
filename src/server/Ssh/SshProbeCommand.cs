using Renci.SshNet.Common;
using System.Net.Sockets;

namespace Charac.Server.Ssh;

/// <summary>Operator-only one-shot transport test; it does not create a managed workspace.</summary>
internal static class SshProbeCommand
{
    public static async Task<int> RunAsync(string[] args, string? configPath = null)
    {
        SshProbeSettings settings;
        try
        {
            settings = SshProbeSettings.FromCommand(args, "probe", configPath);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FileNotFoundException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("An interactive terminal is required to enter the SSH password.");
            return 2;
        }

        Console.Write("SSH password: ");
        var password = ReadPassword();
        Console.WriteLine();
        try
        {
            using var session = await SshPasswordConnection.OpenAsync(
                Guid.CreateVersion7(), Guid.CreateVersion7(), settings.Address, settings.Port, settings.Account,
                [settings.HostPublicKey], password, new SshTerminalSize(80, 24), "xterm-256color", _ => { });
            Console.WriteLine("SSH host key verified, password accepted, PTY shell opened.");
            return 0;
        }
        catch (Exception exception) when (exception is SshException or SocketException or
            InvalidOperationException or ArgumentException or TimeoutException or IOException)
        {
            Console.Error.WriteLine($"SSH probe failed ({exception.GetType().Name}). Check target reachability, host key, account, password and sshd policy.");
            return 1;
        }
    }

    internal static string ReadPassword()
    {
        var characters = new List<char>();
        try
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    var copy = characters.ToArray();
                    try { return new string(copy); }
                    finally { Array.Clear(copy); }
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (characters.Count > 0)
                        characters.RemoveAt(characters.Count - 1);
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    characters.Add(key.KeyChar);
                }
            }
        }
        finally
        {
            for (var index = 0; index < characters.Count; index++)
                characters[index] = '\0';
        }
    }
}
