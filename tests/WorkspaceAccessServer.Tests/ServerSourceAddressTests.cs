using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Charac.Server.Hosting;

namespace Charac.Server.Tests;

public sealed class ServerSourceAddressTests
{
    [Fact]
    public void TrustedProxyMustBeNumericIpAddress()
    {
        var valid = Settings("127.0.0.1");
        Assert.Equal(IPAddress.Loopback, valid.TrustedProxy);
        Assert.Throws<InvalidOperationException>(() => Settings("caddy.local"));
    }

    [Theory]
    [InlineData("127.0.0.1", "203.0.113.7")]
    [InlineData("10.10.0.103", "127.0.0.1")]
    public async Task ForwardedClientAddressIsUsedOnlyFromConfiguredProxy(string trustedProxy, string expected)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        ServerApplication.AddTrustedProxy(builder.Services, IPAddress.Parse(trustedProxy));
        await using var app = builder.Build();
        app.UseForwardedHeaders();
        app.MapGet("/peer", (HttpContext context) => context.Connection.RemoteIpAddress?.ToString());
        await app.StartAsync(timeout.Token);
        try
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(app.Urls.Single()), "/peer"));
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.7");
            using var response = await http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            Assert.Equal(expected, await response.Content.ReadAsStringAsync(timeout.Token));
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    private static ServerSettings Settings(string proxy) => ServerSettings.FromConfiguration(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["server:domain"] = "127.0.0.1",
            ["server:listen"] = "http://127.0.0.1:5080",
            ["server:trusted_proxy"] = proxy
        }).Build());
}
