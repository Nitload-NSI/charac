using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Charac.Server.Authentication;
using Charac.Server.Connections;
using Charac.Server.Data;
using Charac.Server.Hosting;
using Charac.Server.Ssh;
using Charac.Client;

namespace Charac.Server.Tests;

public sealed class ClientSessionTransportTests
{
    [Theory]
    [InlineData("/session", "integration-test-token")]
    [InlineData("/local/session", null)]
    public async Task ConnectSendsBearerOnlyInHeaderAndReceivesOutput(string path, string? token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.UseWebSockets();
        var observed = new TaskCompletionSource<(string Authorization, string Query, string Device)>();
        var closed = new TaskCompletionSource<WebSocketMessageType>();
        app.MapGet(path, async context =>
        {
            observed.TrySetResult((context.Request.Headers.Authorization.ToString(), context.Request.QueryString.Value!,
                context.Request.Headers[ClientDeviceCredential.HeaderName].ToString()));
            if (path == "/session" && context.Request.Headers.Authorization != "Bearer integration-test-token")
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            using var peer = await context.WebSockets.AcceptWebSocketAsync();
            await peer.SendAsync(Encoding.UTF8.GetBytes("remote output"), WebSocketMessageType.Text, true, timeout.Token);
            var frame = await peer.ReceiveAsync(new byte[256], timeout.Token);
            closed.TrySetResult(frame.MessageType);
            await peer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Detached", timeout.Token);
        });
        await app.StartAsync(timeout.Token);
        try
        {
            var id = Guid.NewGuid();
            var device = token is null ? null : new string('A', 43);
            using var socket = await WorkspaceClient.OpenSessionSocketAsync(new Uri(app.Urls.Single()), id,
                token, path, timeout.Token, device);
            var request = await observed.Task.WaitAsync(timeout.Token);
            Assert.Equal(token is null ? "" : "Bearer " + token, request.Authorization);
            Assert.Equal($"?id={id}", request.Query);
            Assert.Equal(device ?? "", request.Device);
            var bytes = new byte[256];
            var output = await socket.ReceiveAsync(bytes, timeout.Token);
            Assert.Equal("remote output", Encoding.UTF8.GetString(bytes.AsSpan(0, output.Count)));
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Detached", timeout.Token);
            Assert.Equal(WebSocketMessageType.Close, await closed.Task.WaitAsync(timeout.Token));
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ManagedSessionGetDoesNotRequireTakeoverQuery()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthorization(options => options.DefaultPolicy =
            new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true).Build());
        builder.Services.AddSingleton<SshWorkspaceManager>();
        builder.Services.AddSingleton<ActiveClientDevices>();
        builder.Services.AddScoped(_ => new SshAccessResolver(null!, TimeProvider.System));
        builder.Services.AddScoped<SshBroker>(_ => null!);
        builder.Services.AddScoped<AccessDbContext>(_ => null!);
        await using var app = builder.Build();
        app.UseWebSockets();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("iss", "https://issuer.example/"), new Claim("sub", "test-user")], "test"));
            await next();
        });
        app.UseAuthorization();
        ClientSessionRoutes.Map(app);
        await app.StartAsync(timeout.Token);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var response = await http.GetAsync($"/session?id={Guid.NewGuid()}", timeout.Token);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }
    [Theory]
    [InlineData("https://access.example.com", "/session", null)]
    [InlineData("https://access.example.com", "/local/session", null)]
    [InlineData("http://127.0.0.1:5080", "/local/session", "token")]
    [InlineData("http://10.10.0.103:5080", "/session", "token")]
    public async Task RejectsMissingAuthenticationAndUnsafeEndpoints(string origin, string path, string? token)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => WorkspaceClient.OpenSessionSocketAsync(
            new Uri(origin), Guid.NewGuid(), token, path, TestContext.Current.CancellationToken));
    }
}
