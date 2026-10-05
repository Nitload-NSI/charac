using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Charac.Client;

namespace Charac.Server.Tests;

public sealed class ResourceMenuTests
{
    [Fact]
    public void SelectionDistinguishesExistingSessionFromNewTargetAndBlocksOccupiedSession()
    {
        var target = new ResourceTarget(Guid.NewGuid(), "Fedora", "alice");
        var session = new ResourceSession(Guid.NewGuid(), target.Id, target.Name, target.Account,
            DateTimeOffset.UtcNow, "detached");
        var occupied = session with { Id = Guid.NewGuid(), State = "attached" };
        var resources = new ResourceList([target], [occupied, session]);
        using var output = new StringWriter();
        Assert.Equal(new ResourceSelection(SessionId: session.Id),
            ResourceMenu.ReadSelection(resources, new StringReader("1\n"), output));
        Assert.Contains("[占用]", output.ToString());
        Assert.Equal(new ResourceSelection(TargetId: target.Id),
            ResourceMenu.ReadSelection(resources, new StringReader("3\n2\n"), output));
        Assert.Contains("请选择列表中的编号", output.ToString());
    }

    [Fact]
    public void EmptyResourcesAllowRefreshOrExitAndNamesCannotInjectTerminalEscapes()
    {
        using var output = new StringWriter();
        Assert.Null(ResourceMenu.ReadSelection(new([], []), new StringReader("q\n"), output));
        Assert.Contains("请管理员登记", output.ToString());
        Assert.True(ResourceMenu.ReadSelection(new([], []), new StringReader("r\n"), output)!.Refresh);
        Assert.Null(ResourceMenu.ReadSelection(new([], []), new StringReader(""), output));
        ResourceMenu.ReadSelection(new([new(Guid.NewGuid(), "name\u001b[2J\nspoof", "alice")], []),
            new StringReader("q\n"), output);
        Assert.DoesNotContain("\u001b", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\nspoof", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshFetchesNewAuthorizedResourcesWithTheSameBearerToken()
    {
        var target = new ResourceTarget(Guid.NewGuid(), "Fedora", "alice");
        using var handler = new ResourceHandler(target);
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        using var output = new StringWriter();
        var selected = await ResourceMenu.SelectAsync(http, new Uri("https://access.example/"),
            new StringReader("r\n1\n"), output, TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.Requests);
        Assert.Equal(new ResourceSelection(TargetId: target.Id), selected);
    }

    private sealed class ResourceHandler(ResourceTarget target) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://access.example/resources", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            Requests++;
            var resources = new ResourceList(Requests == 1 ? [] : [target], []);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(resources) });
        }
    }
}