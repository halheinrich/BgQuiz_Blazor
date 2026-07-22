using BgDataTypes_Lib;
using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Pins <see cref="CubeActionDisplay.Label"/>'s four strings. These
/// deliberately mirror <c>DiagramRenderer</c>'s cube-banner row wording; the
/// duplication across the submodule boundary is accepted precisely because
/// both sides test-pin their labels, so a drift on either side fails a test
/// rather than silently disagreeing on screen.
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

    [Fact]
    public void Label_UndefinedAction_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CubeActionDisplay.Label((CubeAction)999));
    }
}
