using Microsoft.Extensions.Configuration;
using Charac.Server.Authentication;

namespace Charac.Server.Tests;

public sealed class OidcSettingsTests
{
    [Fact]
    public void ConfiguredIssuerUsesProviderDiscoveryByDefault()
    {
        var settings = OidcSettings.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["oidc:issuer"] = "https://auth.example.com/application/o/workspace-access/",
            ["oidc:client_id"] = "workspace-client"
        }));

        Assert.Equal("https://auth.example.com/application/o/workspace-access/", settings!.Issuer);
        Assert.Equal("workspace-client", settings.ClientId);
        Assert.Equal("https://auth.example.com/application/o/workspace-access/.well-known/openid-configuration",
            settings.DiscoveryUrl);
    }

    [Fact]
    public void GlobalIssuerCanUseProviderSpecificDiscovery()
    {
        var settings = OidcSettings.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["oidc:issuer"] = "https://auth.example.com/",
            ["oidc:client_id"] = "workspace-client",
            ["oidc:discovery_url"] =
                "https://auth.example.com/application/o/workspace-access/.well-known/openid-configuration"
        }));

        Assert.Equal("https://auth.example.com/", settings!.Issuer);
        Assert.Contains("/application/o/workspace-access/", settings.DiscoveryUrl);
    }

    [Fact]
    public void PartialOrInsecureOidcConfigurationIsRejected()
    {
        Assert.Null(OidcSettings.FromConfiguration(Configuration([])));
        Assert.Throws<InvalidOperationException>(() => OidcSettings.FromConfiguration(
            Configuration(new Dictionary<string, string?> { ["oidc:issuer"] = "https://auth.example.com/" })));
        Assert.Throws<InvalidOperationException>(() => OidcSettings.FromConfiguration(
            Configuration(new Dictionary<string, string?>
            {
                ["oidc:issuer"] = "http://auth.example.com/",
                ["oidc:client_id"] = "workspace-client"
            })));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
