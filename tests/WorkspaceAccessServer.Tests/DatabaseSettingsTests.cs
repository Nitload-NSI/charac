using Microsoft.Extensions.Configuration;
using Npgsql;
using Charac.Server.Data;

namespace Charac.Server.Tests;

public sealed class DatabaseSettingsTests
{
    [Fact]
    public void SeparateFieldsKeepPasswordDistinctFromConnectionOptions()
    {
        var configuration = Settings(new Dictionary<string, string?>
        {
            ["database:host"] = "127.0.0.1",
            ["database:port"] = "5432",
            ["database:name"] = "ni_access_test_db",
            ["database:username"] = "ni_access_test",
            ["database:password"] = "test;Maximum Pool Size=1",
            ["database:timeout"] = "5",
            ["database:maximum_pool_size"] = "100"
        });

        var result = DatabaseSettings.ConnectionStringFromConfiguration(configuration);
        var parsed = new NpgsqlConnectionStringBuilder(result);

        Assert.Equal("127.0.0.1", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("ni_access_test_db", parsed.Database);
        Assert.Equal("ni_access_test", parsed.Username);
        Assert.Equal("test;Maximum Pool Size=1", parsed.Password);
        Assert.Equal(5, parsed.Timeout);
        Assert.Equal(100, parsed.MaxPoolSize);
    }

    [Fact]
    public void LegacyConnectionStringCannotBeMixedWithSeparateFields()
    {
        var configuration = Settings(new Dictionary<string, string?>
        {
            ["database:connection_string"] = "Host=127.0.0.1;Database=test",
            ["database:host"] = "localhost"
        });

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseSettings.ConnectionStringFromConfiguration(configuration));
    }

    [Fact]
    public void PartialSeparateConfigurationFailsExplicitly()
    {
        var configuration = Settings(new Dictionary<string, string?>
        {
            ["database:host"] = "127.0.0.1"
        });

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseSettings.ConnectionStringFromConfiguration(configuration));
    }

    private static IConfiguration Settings(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
