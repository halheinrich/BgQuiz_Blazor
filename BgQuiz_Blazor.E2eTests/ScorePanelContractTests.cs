using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The score panel's fixed-height contract (<c>SPEC-quiz-view.md</c> §2, ruled
/// 2026-09-22, issue <c>halheinrich/backgammon#111</c>): at and above the width
/// where the review line fits, <c>.score-panel</c> is one fixed line whose
/// height is the same while answering and at review — the accuracy suffix's
/// slot is reserved from the first render, and a long name is cut by the app's
/// one middle truncation. Below that width the wrap is tolerated by ruling, and
/// the control scenario pins that nothing is clipped there.
///
/// <para>
/// Measured in the Normal-view composition: the default maximized view hides
/// the panel while answering, which is the other contract (§3), not this one.
/// The Done page renders the same component under the same class, so it takes
/// the same rule without a scenario of its own.
/// </para>
/// </summary>
public sealed class ScorePanelContractTests : E2eTestBase
{
    public ScorePanelContractTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>A 60-character file name — what "Set:" shows for a one-file pick.</summary>
    private const string LongFileName =
        "Tournament-Finals-2026-Player-One-v-Player-Two-Match-Sixteen.xgp";

    private ILocator ScorePanel => Page.Locator(".score-panel");

    /// <summary>
    /// The panel's height, its one line's height, and whether every figure in
    /// it (each <c>strong</c>) and its folder name lie wholly inside its box —
    /// the "nothing clipped" half, which a fixed-height nowrap line could
    /// otherwise fail silently by cutting off Avg loss.
    /// </summary>
    private async Task<PanelGeometry> MeasureAsync() =>
        JsonSerializer.Deserialize<PanelGeometry>(await Page.EvaluateAsync<string>(@"() => {
            const p = document.querySelector('.score-panel');
            const box = p.getBoundingClientRect();
            const parts = [...p.querySelectorAll('strong, code')];
            const inside = parts.every(e => {
              const r = e.getBoundingClientRect();
              return r.width > 0 && r.left >= box.left - 0.5 && r.right <= box.right + 0.5
                  && r.top >= box.top - 0.5 && r.bottom <= box.bottom + 0.5;
            });
            return JSON.stringify({
              Height: box.height,
              LineHeight: parseFloat(getComputedStyle(p).lineHeight),
              AllInside: inside,
              Parts: parts.length });
        }"))!;

    private sealed record PanelGeometry(double Height, double LineHeight, bool AllInside, int Parts);

    private async Task StartInNormalViewAsync(int width, int height, string stagedFileName)
    {
        await Page.SetViewportSizeAsync(width, height);
        await BootHomeAsync();
        await DisableMaximizeAsync();
        await BootHomeAsync();
        await PickFixtureUnderNameAsync(CubeFixture, stagedFileName, folderName: "ScorePanel");
        await ApplyFilterAsync();
        await StartQuizAsync();
        await Expect(ScorePanel).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(CubeFixture)]
    [InlineData(LongFileName)]
    public async Task AboveTheFitWidth_ThePanelIsOneLine_AndTheSameHeightAnsweringAndAtReview(
        string stagedFileName)
    {
        await StartInNormalViewAsync(1440, 900, stagedFileName);

        var answering = await MeasureAsync();
        await AnswerCubeNoDoubleAsync();
        await Expect(ScorePanel).ToContainTextAsync("(100%)");
        var review = await MeasureAsync();

        Assert.Equal(answering.Height, review.Height);
        Assert.Equal(review.LineHeight, review.Height, 0.5);
        Assert.True(answering.AllInside, "a figure of the answering line lies outside the panel");
        Assert.True(review.AllInside, "a figure of the review line lies outside the panel");
        Assert.Equal(6, review.Parts); // the name, Problem N, and the four figures
    }

    [Fact]
    public async Task BelowTheFitWidth_TheLineWraps_AndNothingIsClipped()
    {
        // The control: at 800px wide the review line cannot be one line (the
        // stats alone outgrow the content column), so the contract does not
        // apply — the panel wraps, as ruled, and every figure is still shown.
        // Answered at the desktop width and measured at 800: the panel is what
        // is under test, and the answer row has its own contract.
        await StartInNormalViewAsync(1440, 900, LongFileName);
        await AnswerCubeNoDoubleAsync();
        await Expect(ScorePanel).ToContainTextAsync("(100%)");
        await Page.SetViewportSizeAsync(800, 900);

        var review = await MeasureAsync();

        Assert.True(review.Height > review.LineHeight + 0.5,
            $"expected a wrapped panel at 800px, measured {review.Height}px at a {review.LineHeight}px line");
        Assert.True(review.AllInside, "a figure of the wrapped line lies outside the panel");
        Assert.Equal(6, review.Parts);
    }
}
