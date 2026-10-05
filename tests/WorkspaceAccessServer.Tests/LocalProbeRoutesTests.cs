using Charac.Server.Hosting;

namespace Charac.Server.Tests;

public sealed class LocalProbeRoutesTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5080", true)]
    [InlineData("http://[::1]:5080", true)]
    [InlineData("http://0.0.0.0:5080", false)]
    [InlineData("http://10.10.0.100:5080", false)]
    [InlineData("https://127.0.0.1:5080", false)]
    public void LocalProbeAcceptsOnlyHttpLoopbackListenAddress(string origin, bool expected)
    {
        Assert.Equal(expected, LocalProbeRoutes.IsLoopbackOrigin(origin));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("localhost", false)]
    [InlineData("10.10.0.100", false)]
    public void LocalProbeAcceptsOnlyNumericLoopbackDomain(string host, bool expected)
    {
        Assert.Equal(expected, LocalProbeRoutes.IsLoopbackHost(host));
    }
}
