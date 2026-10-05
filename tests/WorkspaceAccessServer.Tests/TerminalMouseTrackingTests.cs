using System.Text;
using Charac.Client;

namespace Charac.Server.Tests;

public sealed class TerminalMouseTrackingTests
{
    [Fact]
    public void MouseReportsReachTuiButNotShellAfterAlternateScreenEnds()
    {
        var tracking = new TerminalMouseTracking();
        var report = Encoding.ASCII.GetBytes("\u001b[<35;88;8M");

        Assert.Empty(tracking.FilterInput(report));
        tracking.ObserveOutput(Encoding.ASCII.GetBytes("\u001b[?104"));
        tracking.ObserveOutput(Encoding.ASCII.GetBytes("9h\u001b[?1003h\u001b[?1006h"));
        Assert.True(tracking.InAlternateScreen);
        Assert.Equal(report, tracking.FilterInput(report));

        tracking.ObserveOutput(Encoding.ASCII.GetBytes("\u001b[?1049l"));
        Assert.False(tracking.InAlternateScreen);
        Assert.Empty(tracking.FilterInput(report));
        Assert.Equal(Encoding.ASCII.GetBytes("\u001b[A"), tracking.FilterInput(Encoding.ASCII.GetBytes("\u001b[A")));
    }

    [Fact]
    public void SplitInputPreservesKeyboardAndDropsStaleMouseReport()
    {
        var tracking = new TerminalMouseTracking();
        Assert.Empty(tracking.FilterInput(Encoding.ASCII.GetBytes("\u001b[<35;88")));
        Assert.True(tracking.HasPendingInput);
        Assert.Empty(tracking.FilterInput(Encoding.ASCII.GetBytes(";8M")));
        Assert.False(tracking.HasPendingInput);

        Assert.Empty(tracking.FilterInput(Encoding.ASCII.GetBytes("\u001b")));
        Assert.Equal(Encoding.ASCII.GetBytes("\u001b"), tracking.FlushPendingInput());
        Assert.Equal(Encoding.ASCII.GetBytes("ab\u001b[A"), tracking.FilterInput(Encoding.ASCII.GetBytes("ab\u001b[A")));
    }
}
