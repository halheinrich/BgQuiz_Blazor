using BgQuiz_Blazor.Components.Layout;
using Bunit;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// MainLayout is wrapped by <c>RouteView</c>, which passes <c>@Body</c> in as a
/// <c>RenderFragment</c> parameter — and a <c>RenderFragment</c> can't cross into a
/// component's own interactive rendermode boundary (declaring <c>@rendermode</c>
/// directly on MainLayout throws at runtime: "Cannot pass the parameter 'Body' ...
/// this is because the parameter is of the delegate type RenderFragment, which is
/// arbitrary code and cannot be serialized"). So MainLayout renders static/
/// non-interactive, and the desktop sidebar-collapse toggle can't be C# state on
/// it — it's pure CSS instead, mirroring the existing mobile navbar-toggler
/// checkbox-hack (NavMenu.razor.css). bUnit's AngleSharp DOM has no CSS engine, so
/// it can't evaluate the actual collapse (verified live in a browser instead) —
/// these tests pin the DOM contract the CSS depends on: the toggle checkbox must
/// be a PRECEDING sibling of .sidebar for the `~` combinator to reach it.
/// </summary>
public class MainLayoutTests : BunitContext
{
    [Fact]
    public void SidebarToggleCheckbox_PrecedesSidebar_AsRequiredByCssSiblingSelector()
    {
        var cut = Render<MainLayout>(parameters => parameters
            .Add(p => p.Body, "body content"));

        var children = cut.Find(".page").Children;

        var checkboxIndex = -1;
        var sidebarIndex = -1;
        for (var i = 0; i < children.Length; i++)
        {
            if (children[i].ClassList.Contains("sidebar-toggle-checkbox")) checkboxIndex = i;
            if (children[i].ClassList.Contains("sidebar")) sidebarIndex = i;
        }

        Assert.True(checkboxIndex >= 0, "sidebar-toggle-checkbox not found");
        Assert.True(sidebarIndex >= 0, ".sidebar not found");
        Assert.True(checkboxIndex < sidebarIndex,
            "The toggle checkbox must be a PRECEDING sibling of .sidebar — CSS's " +
            "general sibling combinator (~) only selects LATER siblings, so " +
            "reordering these would silently break the collapse feature.");
    }

    [Fact]
    public void SidebarToggleCheckbox_IsAnUncheckedCheckboxByDefault()
    {
        var cut = Render<MainLayout>(parameters => parameters
            .Add(p => p.Body, "body content"));

        var checkbox = cut.Find("input.sidebar-toggle-checkbox");
        Assert.Equal("checkbox", checkbox.GetAttribute("type"));
        Assert.False(checkbox.HasAttribute("checked"));
    }

    [Fact]
    public void Body_RendersInsideMainArticle()
    {
        var cut = Render<MainLayout>(parameters => parameters
            .Add(p => p.Body, "distinctive-body-marker"));

        var article = cut.Find("main article.content");
        Assert.Contains("distinctive-body-marker", article.TextContent);
    }
}
