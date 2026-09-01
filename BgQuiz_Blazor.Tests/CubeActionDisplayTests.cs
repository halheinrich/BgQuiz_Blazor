using BgDataTypes_Lib;
using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Pins <see cref="CubeActionDisplay"/>'s five strings — four actions and the
/// three claims, two of which share an action's spelling. These deliberately
/// mirror <c>DiagramRenderer</c>'s cube-banner wording ("Too Good" is its
/// pair-level label); the duplication across the submodule boundary is
/// accepted precisely because both sides test-pin their labels, so a drift on
/// either side fails a test rather than silently disagreeing on screen.
/// </summary>
public class CubeActionDisplayTests
{
    [Theory]
    [InlineData(CubeAction.NoDouble, "No Double")]
    [InlineData(CubeAction.Double, "Double")]
    [InlineData(CubeAction.Take, "Take")]
    [InlineData(CubeAction.Pass, "Pass")]
    public void Label_MatchesRendererBannerWording(CubeAction action, string expected)
    {
        Assert.Equal(expected, CubeActionDisplay.Label(action));
    }

    [Theory]
    [InlineData(CubeClaim.NoDouble, "No Double")]
    [InlineData(CubeClaim.Double, "Double")]
    [InlineData(CubeClaim.TooGood, "Too Good")]
    public void Label_Claim_MatchesRendererBannerWording(CubeClaim claim, string expected)
    {
        Assert.Equal(expected, CubeActionDisplay.Label(claim));
    }

    [Fact]
    public void Label_ClaimAndActionAgreeWhereTheyCollapse()
    {
        // The claim layer's defining collapse (CubeClaimExtensions.ToCubeAction):
        // No Double and Double are one board action each, and the review names
        // the claim in the same words the diagram names the action, so a user
        // reading the two side by side never meets two spellings of one thing.
        Assert.Equal(
            CubeActionDisplay.Label(CubeClaim.NoDouble.ToCubeAction()),
            CubeActionDisplay.Label(CubeClaim.NoDouble));
        Assert.Equal(
            CubeActionDisplay.Label(CubeClaim.Double.ToCubeAction()),
            CubeActionDisplay.Label(CubeClaim.Double));
    }

    [Fact]
    public void Label_UndefinedAction_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CubeActionDisplay.Label((CubeAction)999));
    }

    [Fact]
    public void Label_UndefinedClaim_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CubeActionDisplay.Label((CubeClaim)999));
    }
}
