using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Submit stays reachable in the desktop band while answering
/// (halheinrich/backgammon#264, folded into <c>SPEC-quiz-view.md</c> §4's
/// "The action row under quiz navigation"): before the cube pills learned
/// their short form, the trailing cluster overflowed leftward over Submit at
/// 800–900 px. Pinned at both widths, for both answer kinds and in both view
/// modes, with the navigation panel showing — the narrower row of the two.
/// </summary>
/// <remarks>
/// Not pinned here: the band's narrowest stretch, 716–761 px with the panel
/// showing, where the cluster still covers Submit (measured 2026-10-02, leg 4
/// of halheinrich/backgammon#8). Every measured resolution there adds a row
/// inside SPEC-quiz-view.md §2's invariance floor, which the leg was told to
/// report rather than ship; the umbrella rules on it.
/// </remarks>
public sealed class DesktopActionRowTests : E2eTestBase
{
    public DesktopActionRowTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private ILocator ContinueButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton });

    [Theory]
    [InlineData(800, true, false)]
    [InlineData(800, true, true)]
    [InlineData(800, false, false)]
    [InlineData(800, false, true)]
    [InlineData(900, true, false)]
    [InlineData(900, true, true)]
    [InlineData(900, false, false)]
    [InlineData(900, false, true)]
    public async Task WhileAnswering_ATapAtSubmitsCentre_ReachesSubmit(int width, bool cube, bool normalView)
    {
        await Page.SetViewportSizeAsync(width, 900);
        await BootHomeAsync();
        if (normalView)
        {
            await DisableMaximizeAsync();
            await BootHomeAsync();
        }
        await PickFixtureAsync(cube ? CubeFixture : CheckerFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        // A complete answer lights Submit: a pill, or the checker fixture's
        // best play, 24/13, in two clicks (see QuizFlowTests' checker path).
        if (cube)
        {
            await CubePill(ExpectedText.NoDoublePill).CheckAsync();
        }
        else
        {
            await ClickBoardPointAsync(24);
            await ClickBoardPointAsync(18);
        }
        await Expect(SubmitButton).ToBeEnabledAsync();

        // What a finger on Submit's centre lands on — the button itself, or
        // something inside it — not whatever the row's tail laid over it.
        var hit = await SubmitButton.EvaluateAsync<string>(@"b => {
            const r = b.getBoundingClientRect();
            const e = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
            return b.contains(e) ? 'Submit' : `${e.tagName}.${e.className} '${(e.getAttribute('aria-label') || e.textContent || '').trim()}'`;
        }");
        Assert.Equal("Submit", hit);

        // And the real gesture: no force, so Playwright's own hit test must
        // agree the button receives it.
        await SubmitButton.ClickAsync();
        await Expect(ContinueButton).ToBeVisibleAsync();
    }
}
