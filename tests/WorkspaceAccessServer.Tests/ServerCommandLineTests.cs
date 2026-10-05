using Charac.Server.Hosting;

namespace Charac.Server.Tests;

public sealed class ServerCommandLineTests
{
    [Fact]
    public void ConfigOptionCanPrecedeOrFollowCommand()
    {
        var first = ServerCommandLine.Parse(["--config", "custom.config", "database", "migrate"]);
        var last = ServerCommandLine.Parse(["database", "migrate", "--config=custom.config"]);

        Assert.Equal("custom.config", first.ConfigPath);
        Assert.Equal(["database", "migrate"], first.Command);
        Assert.Equal(first.ConfigPath, last.ConfigPath);
        Assert.Equal(first.Command, last.Command);
    }

    [Theory]
    [InlineData("--config")]
    [InlineData("--config=")]
    [InlineData("--config", "--help")]
    [InlineData("--config=a.config", "--config=b.config")]
    public void MissingOrRepeatedConfigPathIsRejected(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => ServerCommandLine.Parse(args));
    }

    [Fact]
    public void ExplicitConfigDoesNotFallBackWhenMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".config");
        Assert.Throws<FileNotFoundException>(() => WorkspaceAccessConfiguration.PathToLoad(missing));
    }
}
