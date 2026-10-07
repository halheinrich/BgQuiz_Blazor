using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The review's verdict band at the two sides of the 641px edge
/// (halheinrich/backgammon#329, ruled 2026-10-07): below it — the band where
/// <c>SPEC-quiz-view.md</c> §2's law does not exist and the page flows
/// normally — the verdict wraps to the height it needs, every word shown; at
/// and above it the two-line clamp and the band's fixed height stand. Pinned
/// on the longest verdict the quiz writes: a practice answer of Double / Take
/// on <see cref="SyntheticXgMatch.ThreeAnswerBestBytes"/>, whose Best list is
/// three answers long — the case the measurement found cut at phone widths,
/// with the words that tell answers apart lost.
/// </summary>
public sealed class VerdictBandTests : E2eTestBase
{
    public VerdictBandTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private const string PracticeThreeAnswerVerdict =
        "Practice — Not best — Double / Take lost 0.2000. Best: No double, Double / Pass, No double / Pass.";

    /// <summary>The band's box and its text's geometry and clamp, read in one evaluation.</summary>
    private sealed record Band(
        double BandTop, double BandBottom, double BandHeight,
        double TextTop, double TextBottom, double TextScrollHeight, double TextClientHeight,
        int TextLines, string LineClamp, string Overflow, double RootFontSize);

    private async Task<Band> ReadBandAsync()
    {
        // Two frames first: the page's own re-fit after a resize has run.
        await Page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
        var json = await VerdictBand.EvaluateAsync<string>("""
            band => {
                const text = band.querySelector('.status-verdict-text');
                const b = band.getBoundingClientRect();
                const t = text.getBoundingClientRect();
                const style = getComputedStyle(text);
                return JSON.stringify({
                    BandTop: b.top, BandBottom: b.bottom, BandHeight: b.height,
                    TextTop: t.top, TextBottom: t.bottom,
                    TextScrollHeight: text.scrollHeight, TextClientHeight: text.clientHeight,
                    TextLines: Math.round(t.height / parseFloat(style.lineHeight)),
                    LineClamp: style.webkitLineClamp, Overflow: style.overflowY,
                    RootFontSize: parseFloat(getComputedStyle(document.documentElement).fontSize),
                });
            }
            """);
        return System.Text.Json.JsonSerializer.Deserialize<Band>(json)!;
    }

    /// <summary>Land on the practice review of the three-answer cube: answered, away, back, answered again.</summary>
    private async Task ReachThePracticeVerdictAsync()
    {
        await Page.SetViewportSizeAsync(1280, 768);
        await BootHomeAsync();
        await DisableMaximizeAsync();   // the score panel names the problem while answering
        await BootHomeAsync();
        await PickSynthesizedFileAsync(
            SyntheticXgMatch.ThreeAnswerBestStagedFileName, SyntheticXgMatch.ThreeAnswerBestBytes());
        await ApplyFilterAsync();
        await StartQuizAsync();
        await ExpectCubeProblemAsync(1);
        await AnswerCubeAsync(ExpectedText.DoubleTakePill);
        await NavButton(ExpectedText.NextButton).ClickAsync();
        await ExpectProblemNumberAsync(2);
        await NavButton(ExpectedText.BackButton).ClickAsync();
        await ExpectCubeProblemAsync(1);
        await AnswerCubeAsync(ExpectedText.DoubleTakePill);
        await Expect(VerdictBand).ToHaveTextAsync(PracticeThreeAnswerVerdict);
    }

    [Theory]
    [InlineData(360)]
    [InlineData(375)]
    public async Task BelowThe641pxEdge_TheWholeVerdictShows_WrappedToTheHeightItNeeds(int width)
    {
        await ReachThePracticeVerdictAsync();

        await Page.SetViewportSizeAsync(width, 768);
        var band = await ReadBandAsync();

        // More than two lines — the case the clamp cut — and every one of them
        // inside the band, nothing scrolled away or hidden.
        Assert.True(band.TextLines > 2, $"the verdict should need more than two lines at {width}px, took {band.TextLines}");
        Assert.Equal("none", band.LineClamp);
        Assert.Equal("visible", band.Overflow);
        Assert.True(band.TextScrollHeight <= band.TextClientHeight + 1,
            $"text overflows its box: scroll {band.TextScrollHeight}, client {band.TextClientHeight}");
        Assert.True(band.TextTop >= band.BandTop && band.TextBottom <= band.BandBottom,
            $"text [{band.TextTop}, {band.TextBottom}] spills out of the band [{band.BandTop}, {band.BandBottom}]");
        Assert.True(band.BandHeight > 3.7 * band.RootFontSize, "the band grew to hold the verdict");
        await Expect(VerdictBand).ToHaveTextAsync(PracticeThreeAnswerVerdict);
    }

    [Fact]
    public async Task AtThe641pxEdge_TheTwoLineClampAndTheFixedHeightStand()
    {
        await ReachThePracticeVerdictAsync();

        await Page.SetViewportSizeAsync(641, 768);
        var band = await ReadBandAsync();

        Assert.Equal("2", band.LineClamp);
        Assert.Equal("hidden", band.Overflow);
        Assert.Equal(3.7 * band.RootFontSize, band.BandHeight, 0.5);
    }
}
