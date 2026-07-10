using BgQuiz_Blazor.Components.Layout;
using Bunit;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The host's sidebar nav is the <b>only</b> entry point to <c>/help</c> — the quiz
/// action row deliberately carries no "?" button, because its fixed height is
/// load-bearing for board sizing. These pin that entry point: a dropped or
/// mis-targeted NavLink would leave the help page reachable by URL alone.
/// </summary>
public class NavMenuTests : BunitContext
{
    [Fact]
    public void NavMenu_LinksToHelp_AlongsideHome()
    {
        var cut = Render<NavMenu>();

        var links = cut.FindAll("nav a.nav-link");
        var hrefs = links.Select(a => a.GetAttribute("href")).ToList();

        Assert.Contains("", hrefs);      // Home
        Assert.Contains("help", hrefs);  // Help

        var help = links.Single(a => a.GetAttribute("href") == "help");
        Assert.Equal("Help", help.TextContent.Trim());
    }
}
