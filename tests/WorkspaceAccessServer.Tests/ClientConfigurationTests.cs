using Charac.Client;

namespace Charac.Server.Tests;

public sealed class ClientConfigurationTests
{
    [Fact]
    public void LoadsHttpsServerAndRejectsUnsafeOrAmbiguousSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "rac-client-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "; per-user endpoint\n[client]\nserver=\"https://access.example.com\"\n");
            Assert.Equal("https://access.example.com/", ClientConfiguration.LoadServer(path));

            File.WriteAllText(path, "[client]\nserver=http://access.example.com\n");
            Assert.Throws<InvalidOperationException>(() => ClientConfiguration.LoadServer(path));

            File.WriteAllText(path, "[client]\nserver=https://one.example\nserver=https://two.example\n");
            Assert.Throws<InvalidOperationException>(() => ClientConfiguration.LoadServer(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreatesEmptyConfigurationWhenMissing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rac-client-config-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "client.config");
        try
        {
            Assert.Throws<InvalidOperationException>(() => ClientConfiguration.LoadServer(path));
            Assert.True(File.Exists(path));
            Assert.Contains("[client]", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WritesDefaultServerConfiguration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rac-client-config-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "client.config");
        try
        {
            ClientConfiguration.SetDefaultServer(path, "https://access.example.com");
            Assert.Equal("https://access.example.com/", ClientConfiguration.LoadServer(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}