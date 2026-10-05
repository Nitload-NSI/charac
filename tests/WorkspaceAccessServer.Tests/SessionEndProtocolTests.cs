using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Charac.Server.Hosting;
using Charac.Server.Ssh;

namespace Charac.Server.Tests;

public sealed class SessionEndProtocolTests
{
    [Fact]
    public async Task SshEndIsSentAfterAllOutputAndBeforeWebSocketClose()
    {
        var channel = Channel.CreateUnbounded<SshOutput>();
        Assert.True(channel.Writer.TryWrite(new SshOutput(1, Encoding.UTF8.GetBytes("last output"))));
        channel.Writer.TryComplete();
        using var socket = new RecordingWebSocket();

        var ended = await ClientSessionRoutes.SendOutputAsync(socket, channel.Reader,
            Task.FromResult("ssh_closed"), CancellationToken.None);

        Assert.True(ended);
        Assert.Equal(2, socket.Messages.Count);
        using var output = JsonDocument.Parse(socket.Messages[0]);
        Assert.Equal("output", output.RootElement.GetProperty("type").GetString());
        Assert.Equal("last output", Encoding.UTF8.GetString(Convert.FromBase64String(
            output.RootElement.GetProperty("data").GetString()!)));
        using var end = JsonDocument.Parse(socket.Messages[1]);
        Assert.Equal("ended", end.RootElement.GetProperty("type").GetString());
        Assert.Equal("ssh_closed", end.RootElement.GetProperty("reason").GetString());
        Assert.True(socket.CloseOutputCalled);
    }

    [Fact]
    public async Task ViewerReplacementDoesNotClaimSshEnded()
    {
        var channel = Channel.CreateUnbounded<SshOutput>();
        channel.Writer.TryComplete();
        using var socket = new RecordingWebSocket();
        var workspaceEnd = new TaskCompletionSource<string>();

        var ended = await ClientSessionRoutes.SendOutputAsync(socket, channel.Reader,
            workspaceEnd.Task, CancellationToken.None);

        Assert.False(ended);
        Assert.Empty(socket.Messages);
        Assert.False(socket.CloseOutputCalled);
    }

    [Fact]
    public async Task WorkspaceEndDrainsFinalOutputEvenWhenDisconnectSignalWins()
    {
        var sender = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = ClientSessionRoutes.CompleteStreamAsync(sender.Task, receiver.Task,
            Task.CompletedTask, Task.CompletedTask, CancellationToken.None);

        Assert.False(completion.IsCompleted);
        sender.SetResult(true);
        receiver.SetResult();
        await completion;
    }

    [Fact]
    public async Task ViewerDisconnectDoesNotWaitForWorkspaceOutput()
    {
        var sender = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await ClientSessionRoutes.CompleteStreamAsync(sender.Task, new TaskCompletionSource().Task,
            Task.CompletedTask, ended.Task, CancellationToken.None);
        Assert.False(sender.Task.IsCompleted);
    }
    private sealed class RecordingWebSocket : WebSocket
    {
        public List<byte[]> Messages { get; } = [];
        public bool CloseOutputCalled { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken)
        {
            CloseOutputCalled = true;
            return Task.CompletedTask;
        }
        public override void Dispose() { }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken)
        {
            Assert.Equal(WebSocketMessageType.Text, messageType);
            Assert.True(endOfMessage);
            Messages.Add(buffer.ToArray());
            return Task.CompletedTask;
        }
    }
}
