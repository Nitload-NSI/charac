using System.IO.Pipes;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Charac.Server.Hosting;

internal static class ServerControlClient
{
    public static async Task<int> RunAsync(string command, string? configPath)
    {
        try
        {
            var reply = await SendAsync(command, configPath);
            Console.WriteLine($"{reply.Message} PID={reply.ProcessId}, workspaces={reply.Workspaces}.");
            if (reply.Disconnected is { } disconnected)
                Console.WriteLine($"Disconnected client connections: {disconnected}.");
            if (reply.Sessions is { } sessions)
                foreach (var session in sessions)
                    Console.WriteLine($"{session.Id} target={session.TargetId} issuer={Safe(session.Issuer)} " +
                        $"subject={Safe(session.Subject)} account={Safe(session.Account)} state={session.State} " +
                        $"peer={session.ConnectionPeer ?? "-"} created={session.CreatedAt:O} " +
                        $"attached={session.AttachedAt?.ToString("O") ?? "-"}");
            return reply.Ok ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or
            JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not reach the running Server control channel: {exception.Message}");
            return 1;
        }
    }

    private static string Safe(string value) => new(value.Select(ch =>
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(ch);
        return char.IsControl(ch) || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or
            UnicodeCategory.ParagraphSeparator ? '?' : ch;
    }).ToArray());

    internal static async Task<ServerControlReply> SendAsync(string command, string? configPath)
    {
        var resolved = WorkspaceAccessConfiguration.PathToLoad(configPath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", ServerControlChannel.PipeName(resolved),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(command.AsMemory(), timeout.Token);
        var response = await reader.ReadLineAsync(timeout.Token);
        return JsonSerializer.Deserialize<ServerControlReply>(response ?? "") ??
            throw new InvalidDataException("The running Server returned an empty control response.");
    }
}
