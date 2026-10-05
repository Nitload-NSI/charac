using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Charac.Server.Authentication;
using Charac.Server.Connections;
using Charac.Server.Hosting;
using Charac.Server.Ssh;

namespace Charac.Server.Tests;

public sealed class ClientResourceTests
{
    [Fact]
    public void SnapshotFiltersOwnerIssuerRevokedTargetAndChangedAccount()
    {
        var owner = new ExternalIdentity("https://auth.example/", "alice");
        var target = new SshTargetResource(Guid.NewGuid(), "Fedora", "alice");
        var now = DateTimeOffset.UtcNow;
        var detached = new SshWorkspaceSnapshot(Guid.NewGuid(), target.Id, owner, "alice", now, "detached");
        var attached = detached with { Id = Guid.NewGuid(), CreatedAt = now.AddMinutes(-1), State = "attached" };
        var snapshots = new[]
        {
            detached, attached,
            detached with { Owner = new(owner.Issuer, "bob") },
            detached with { Owner = new("https://another.example/", owner.Subject) },
            detached with { TargetId = Guid.NewGuid() },
            detached with { Account = "previous-account" }
        };
        var resources = ClientResourceRoutes.CreateSnapshot(owner, [target], snapshots);
        Assert.Equal(new[] { attached.Id, detached.Id }, resources.Sessions.Select(x => x.Id));
        Assert.All(resources.Sessions, session => Assert.Equal("Fedora", session.TargetName));
        Assert.Empty(ClientResourceRoutes.CreateSnapshot(owner, [], snapshots).Sessions);
        Assert.Empty(ClientResourceRoutes.CreateSnapshot(owner, [target with { Account = "new-account" }], snapshots).Sessions);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(resources, JsonSerializerOptions.Web));
        var sessionProperties = json.RootElement.GetProperty("sessions")[0].EnumerateObject().Select(x => x.Name);
        Assert.Equal(new[] { "id", "targetId", "targetName", "account", "createdAt", "state" }, sessionProperties);
        Assert.Equal(new[] { "id", "name", "account" },
            json.RootElement.GetProperty("targets")[0].EnumerateObject().Select(x => x.Name));
    }

    [Fact]
    public async Task AnonymousResourceRequestIsRejectedBeforeReadingDatabase()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<SshAccessResolver>(_ => throw new InvalidOperationException("Database must not be resolved."));
        builder.Services.AddSingleton<SshWorkspaceManager>();
        builder.Services.AddSingleton<ActiveClientDevices>();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        ClientResourceRoutes.Map(app);
        await app.StartAsync(timeout.Token);
        try
        {
            using var http = new HttpClient();
            using var response = await http.GetAsync(new Uri(new Uri(app.Urls.Single()), "/resources"), timeout.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }
}
