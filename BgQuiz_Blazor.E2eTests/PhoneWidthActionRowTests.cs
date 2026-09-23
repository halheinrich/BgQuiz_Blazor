using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Below 641px the action row's tail wraps onto its own line
/// (<c>SPEC-quiz-view.md</c> §4, ruled 2026-09-22, issue
/// <c>halheinrich/backgammon#236</c>). Before the ruling the tail, held on the
/// answer instruments' line and unable to shrink further, overflowed leftward
/// over them: at 375×812 a tap on Continue reached the XGID copy button. Pinned
/// at the mobile preset's size, at review, on the committed cube fixture.
/// </summary>
public sealed class PhoneWidthActionRowTests : E2eTestBase
{
    public PhoneWidthActionRowTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private ILocator ContinueButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton });

    private async Task ReachReviewAtPhoneWidthAsync()
    {
        await Page.SetViewportSizeAsync(375, 812);
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueButton.ScrollIntoViewIfNeededAsync();
    }

    [Fact]
    public async Task AtReview_ContinueIsWhatItsCentreHits_AndATapAdvances()
    {
        await ReachReviewAtPhoneWidthAsync();

        // What a finger on Continue's centre lands on — the element itself, or
        // something inside it — not whatever the tail laid over it.
        var hit = await ContinueButton.EvaluateAsync<string>(@"b => {
            const r = b.getBoundingClientRect();
            const e = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
            return b.contains(e) ? 'Continue' : `${e.tagName}.${e.className} '${(e.textContent || '').trim()}'`;
        }");
        Assert.Equal("Continue", hit);

        // And the real gesture: no force, so Playwright's own hit test must
        // agree the button receives it (a click — the suite's context does
        // not enable touch).
        await ContinueButton.ClickAsync();
        await ExpectUrlAsync("/done");
    }

    [Fact]
    public async Task AtReview_EveryControlLiesInsideTheViewport()
    {
        await ReachReviewAtPhoneWidthAsync();

        var outside = await Page.EvaluateAsync<string[]>(@"() => {
            const w = document.documentElement.clientWidth;
            return [...document.querySelectorAll('main button, main input, main select, main a')]
              .filter(e => e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden')
              .filter(e => { const r = e.getBoundingClientRect(); return r.left < -0.5 || r.right > w + 0.5; })
              .map(e => `${e.tagName} '${(e.getAttribute('aria-label') || e.textContent || '').trim()}'`);
        }");

        Assert.Empty(outside);
    }
}
