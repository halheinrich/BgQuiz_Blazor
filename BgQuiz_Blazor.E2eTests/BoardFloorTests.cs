using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The vertical law's floor (<c>SPEC-quiz-view.md</c> §2, ruled 2026-09-22,
/// issue <c>halheinrich/backgammon#112</c>): the board region keeps a minimum
/// height — the board's height at the 641px-wide edge of the law's domain —
/// and where the viewport cannot give it, the page scrolls instead of the
/// board shrinking further.
///
/// <para>
/// The floor is read from the page's own <c>--board-min-height</c>, never
/// pasted here: the number is a measurement <c>app.css</c> states and cites,
/// and this suite's job is that the page honours whatever it states. Measured
/// at review, where the Normal composition's board is the smaller one — the
/// maximized answering board at 720×450 is already above the floor.
/// </para>
/// </summary>
public sealed class BoardFloorTests : E2eTestBase
{
    public BoardFloorTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private sealed record Geometry(double Floor, double Region, double Board, double ScrollHeight, double ClientHeight);

    private async Task<Geometry> MeasureAsync() =>
        JsonSerializer.Deserialize<Geometry>(await Page.EvaluateAsync<string>(@"() => {
            const region = document.querySelector('.board-container');
            const page = document.querySelector('.board-page');
            return JSON.stringify({
              Floor: parseFloat(getComputedStyle(region).getPropertyValue('--board-min-height')),
              Region: region.getBoundingClientRect().height,
              Board: document.querySelector('.bg-diagram').getBoundingClientRect().height,
              ScrollHeight: page.scrollHeight,
              ClientHeight: page.clientHeight });
        }"))!;

    private async Task ReachReviewAsync(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
    }

    [Fact]
    public async Task At200PercentZoom_TheBoardHoldsTheFloor_AndThePageScrolls()
    {
        // 1440x900 at 200% zoom is 720x450 CSS pixels: the case that gave a
        // 136x76 board with no scroll before the floor.
        await ReachReviewAsync(720, 450);

        var g = await MeasureAsync();

        Assert.True(g.Floor > 0, "the page states no --board-min-height");
        Assert.True(g.Region >= g.Floor - 0.5, $"region {g.Region}px is below the {g.Floor}px floor");
        Assert.True(g.Board >= g.Floor - 0.5, $"board {g.Board}px is below the {g.Floor}px floor");
        Assert.True(g.ScrollHeight > g.ClientHeight,
            $"the page does not scroll: {g.ScrollHeight} in {g.ClientHeight}");

        // The chrome it pushed past the fold is reachable by that scroll.
        var continueButton = Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton });
        await continueButton.ScrollIntoViewIfNeededAsync();
        await Expect(continueButton).ToBeInViewportAsync();
    }

    [Fact]
    public async Task AtTheDesktopViewport_TheFloorIsNotEngaged_AndNothingScrolls()
    {
        // The control: where the law already gives the board more than the
        // floor, the floor changes nothing — the region is above it and the
        // page has nothing to scroll.
        await ReachReviewAsync(1440, 900);

        var g = await MeasureAsync();

        Assert.True(g.Region > g.Floor, $"region {g.Region}px is not above the {g.Floor}px floor");
        Assert.Equal(g.ClientHeight, g.ScrollHeight);
    }
}
