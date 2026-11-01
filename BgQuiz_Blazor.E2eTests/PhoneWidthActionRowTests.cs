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
    public PhoneWidthActionRowTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    private ILocator ContinueButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton });

    private async Task ReachReviewAtPhoneWidthAsync()
    {
        await Page.SetViewportSizeAsync(375, 812);
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueButton.ScrollIntoViewIfNeededAsync();
    }

    [Fact]
    public async Task AtReview_ContinueIsWhatItsCentreHits_AndATapAdvances()
    {
        await ReachReviewAtPhoneWidthAsync();

        // What a finger on Continue's centre lands on — the element itself, or
        // something inside it — not whatever the tail laid over it. A centre
        // off-screen hits nothing (elementFromPoint returns null), and says so
        // rather than throwing in the page (halheinrich/backgammon#376).
        var hit = await ContinueButton.EvaluateAsync<string>(@"b => {
            const r = b.getBoundingClientRect();
            const e = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
            if (e === null) return `covered by nothing (off-screen): Continue's centre is outside the viewport`;
            return b.contains(e) ? 'Continue' : `${e.tagName}.${e.className} '${(e.textContent || '').trim()}'`;
        }");
        Assert.Equal("Continue", hit);

        // And the real gesture: no force, so Playwright's own hit test must
        // agree the button receives it (a click — the suite's context does
        // not enable touch).
        await ContinueButton.ClickAsync();
        await ExpectUrlAsync(AppRoute.Done);
    }

    /// <summary>
    /// What a finger on each navigation button's centre lands on, by name:
    /// the button itself, or what covers it. A dark button's attribute is
    /// lifted for the test — Chromium does not hit-test a disabled button, so
    /// one that is merely unavailable would otherwise read as covered. A centre
    /// off-screen hits nothing (elementFromPoint returns null) and reads as
    /// covered by nothing, rather than throwing in the page
    /// (halheinrich/backgammon#376).
    /// </summary>
    private Task<string[]> NavigationCentreHitsAsync() =>
        Page.EvaluateAsync<string[]>(@"() =>
            [...document.querySelectorAll('.action-row .quiz-nav button')].map(b => {
              const was = b.disabled; b.disabled = false;
              const r = b.getBoundingClientRect();
              const e = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
              b.disabled = was;
              const name = b.getAttribute('aria-label');
              if (e === null) return `${name} covered by nothing (off-screen)`;
              return b.contains(e) ? name : `${name} covered by ${e.tagName}.${e.className}`;
            })");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EachNavigationButton_IsWhatItsCentreHits(bool atReview)
    {
        // The four navigation buttons sit in the leading cluster, with Submit
        // or Continue; below 641px the tail has its own line, so nothing may lie
        // over any of them in either state (SPEC-quiz-view.md §4).
        await Page.SetViewportSizeAsync(375, 812);
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        if (atReview) await AnswerCubeNoDoubleAsync();
        // The measured row: pending, the tail is behind its "⋯" on the
        // buttons' line, where measured it takes a line of its own, so a hit
        // test then would be of the other presentation
        // (halheinrich/backgammon#333).
        await ExpectRowFittedAsync();
        await Page.Locator(".action-row .quiz-nav").ScrollIntoViewIfNeededAsync();

        var next = atReview ? ExpectedText.NextButton : ExpectedText.SkipButton;
        Assert.Equal(
            [ExpectedText.GoToFirstButton, ExpectedText.BackButton, next, ExpectedText.GoToLastButton],
            await NavigationCentreHitsAsync());
    }

    [Fact]
    public async Task ATapOnEachNavigationButton_MovesTheRunAsThatButtonDoes()
    {
        // The real gestures, at the mobile preset's size: ▶ defers the first
        // problem, ◀ returns to it, ⏭ goes to the frontier and ⏮ back to the
        // first. Normal view, so the problem number stays on screen while
        // answering — set at the desktop width, where the Settings link is in
        // the open navigation panel rather than behind the phone's toggle.
        await BootHomeAsync();
        await DisableMaximizeAsync();
        await Page.SetViewportSizeAsync(375, 812);
        await BootHomeAsync();
        await PickCubeProblemsAsync(2);
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        await ExpectProblemNumberAsync(1);

        await NavButton(ExpectedText.SkipButton).ClickAsync();
        await ExpectProblemNumberAsync(2);
        await NavButton(ExpectedText.BackButton).ClickAsync();
        await ExpectProblemNumberAsync(1);
        await NavButton(ExpectedText.GoToLastButton).ClickAsync();
        await ExpectProblemNumberAsync(2);
        await NavButton(ExpectedText.GoToFirstButton).ClickAsync();
        await ExpectProblemNumberAsync(1);
    }

    /// <summary>
    /// The controls the fitted row offers on <see cref="E2eTestBase.CubeFixture"/>
    /// at a phone's width, in row order: the four answers (their short form,
    /// the fourth reading No double / Pass on this fixture), Submit, the four
    /// navigation buttons and the tail, on its own line.
    /// </summary>
    private static readonly string[] AnsweringRowControls =
    [
        "ND (No double)", "D/T (Double / Take)", "D/P (Double / Pass)", "NP (No double / Pass)",
        ExpectedText.SubmitButton,
        ExpectedText.GoToFirstButton, ExpectedText.BackButton, ExpectedText.SkipButton, ExpectedText.GoToLastButton,
        "Copy XGID to clipboard", "Show stats", "End quiz",
    ];

    /// <summary>
    /// That <paramref name="reach"/> examined the row's controls, so that a
    /// claim about all of them cannot hold of an empty or a different set.
    /// </summary>
    private static void AssertExaminedTheRow(ActionRowGeometry.RowReach reach) =>
        Assert.True(
            reach.Controls.Select(c => c.Control).SequenceEqual(AnsweringRowControls),
            $"examined a set other than the row's {AnsweringRowControls.Length} controls\n  {reach}");

    /// <summary>That <paramref name="reach"/> found the row's every control off-screen.</summary>
    private static void AssertEveryControlOffScreen(ActionRowGeometry.RowReach reach)
    {
        AssertExaminedTheRow(reach);
        Assert.True(reach.Controls.All(c => c.OffScreen), $"every control is off-screen before the scroll\n  {reach}");
    }

    /// <summary>
    /// How many animation frames running the page must hold still for
    /// <see cref="ExpectPageAtRestAtTheTopAsync"/>. Frames, not a time, because
    /// what it must outlast is counted in frames: after the fit the page read
    /// the top for at most 2 frames before it rose, in 132 runs measured for
    /// halheinrich/backgammon#434 on 24 logical processors and on 2, at up to
    /// 8× CPU throttling. The count did not grow with the load; only each
    /// frame's length did.
    /// </summary>
    private const int FramesAtRest = 10;

    /// <summary>
    /// Wait until the page has come to rest at the top of its scroll
    /// (halheinrich/backgammon#434), failing by name with the positions seen
    /// if it never does.
    ///
    /// <para>
    /// <b>What settles.</b> In a window this short, Start is far down Home, so
    /// the click leaves Home scrolled to about 896. Navigating away asks for
    /// <c>window.scrollTo(0, 0)</c> (blazor.web.js), and Bootstrap's
    /// <c>scroll-behavior: smooth</c> makes that an animation wherever the
    /// browser animates scrolls. It outlives Home. The quiz page reads 0 until
    /// its row makes it taller than the window; in the frames after the fit
    /// the animation then carries it to the bottom of its range (192 here,
    /// bringing the whole row into view), and it glides back to the top about
    /// 400 ms after the fit. So a read straight after the fit decides nothing
    /// about the fold: it passes in the first frames or once the glide is over,
    /// and between them finds every control reached, the failure seen in CI.
    /// The transient breaks no contract, so the page is left alone: the row
    /// lies below the fold once the page has settled, and that is the state
    /// the pin is about.
    /// </para>
    ///
    /// <para>
    /// <b>Why this watches the position.</b> Nothing the page exposes marks the
    /// end. <c>scrollend</c> fires with Home's first tick and at the glide's
    /// end, and only the first of those where a scroll does not animate, so it
    /// cannot tell a scroll to come from none. <c>Not.ToBeInViewportAsync</c>
    /// is met in the frames before the rise. Reduced motion would test a page
    /// no user gets (<c>HelpAndTitlesTests</c>). So the position itself is
    /// sampled, frame by frame in one evaluation, until it has held for
    /// <see cref="FramesAtRest"/>. This polls the state the assertion needs,
    /// never the assertion.
    /// </para>
    ///
    /// <para>
    /// <b>Where it shows.</b> Not everywhere. Chromium did not animate this
    /// scroll on the Windows machine it was measured on, whose animation
    /// effects are off, and there the pin never lost the race until launched
    /// with <c>--enable-smooth-scrolling</c>. CI's Linux runner lost it. So
    /// the proof (<see cref="BelowTheFold_TheFirstRead_WaitsOutTheScrollReset"/>)
    /// replays the scroll itself.
    /// </para>
    /// </summary>
    private Task ExpectPageAtRestAtTheTopAsync() =>
        ExpectToPassAsync(async () =>
        {
            var scrollYs = await Page.EvaluateAsync<double[]>(@"frames => new Promise(done => {
                const seen = [];
                const sample = () => {
                  seen.push(window.scrollY);
                  if (seen.length === frames) done(seen); else requestAnimationFrame(sample);
                };
                requestAnimationFrame(sample);
              })", FramesAtRest);
            Assert.True(
                scrollYs.All(y => y == 0),
                $"the page has not come to rest at the top: scrollY over {FramesAtRest} frames was {string.Join(", ", scrollYs)}");
        });

    /// <summary>The row's reach once the page has settled (<see cref="ExpectPageAtRestAtTheTopAsync"/>).</summary>
    private async Task<ActionRowGeometry.RowReach> RowReachAtRestAsync()
    {
        await ExpectPageAtRestAtTheTopAsync();
        return await ActionRowGeometry.RowReachAsync(Page);
    }

    /// <summary>The quiz page at a phone's width and a short window, its row fitted.</summary>
    private async Task ReachFittedRowInAShortWindowAsync()
    {
        await Page.SetViewportSizeAsync(375, 400);
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        await ExpectRowFittedAsync();
    }

    /// <summary>
    /// <b>A row below the fold is off-screen, not covered, and is not left
    /// out</b> (halheinrich/backgammon#341). At a phone's width and a short
    /// window the measured row lies below the fold, where a tap cannot reach
    /// it until it is scrolled to. The reach check reports every one of the
    /// row's controls, each as off-screen, never as covered by nothing; and
    /// once the row is scrolled into view, none. Each half reads the controls
    /// and their reach in one browser evaluation
    /// (<see cref="ActionRowGeometry.RowReachAsync"/>), and a failure carries
    /// that whole reading (halheinrich/backgammon#434). The first waits for
    /// the page to settle (<see cref="ExpectPageAtRestAtTheTopAsync"/>).
    /// </summary>
    [Fact]
    public async Task BelowTheFold_EveryRowControlIsReportedOffScreen_AndScrolledToNoneIs()
    {
        await ReachFittedRowInAShortWindowAsync();

        AssertEveryControlOffScreen(await RowReachAtRestAsync());

        await Page.Locator(".action-row").ScrollIntoViewIfNeededAsync();
        await ExpectToPassAsync(async () =>
        {
            var scrolledTo = await ActionRowGeometry.RowReachAsync(Page);
            AssertExaminedTheRow(scrolledTo);
            Assert.True(scrolledTo.Controls.All(c => c.Reached), $"no control is unreachable after the scroll\n  {scrolledTo}");
        });
    }

    /// <summary>
    /// Replays in the page what the scroll reset does after the fit, as
    /// measured (see <see cref="ExpectPageAtRestAtTheTopAsync"/>): the top for
    /// two frames, the bottom of the page's range from the third, the top
    /// again at the twenty-fourth, about 400 ms at 60 frames a second. Each
    /// move is instant, so what the page does is this script and not a real
    /// animation, on a machine that animates scrolls or one that does not.
    /// <c>window.__scrollResetOver</c> says the replay has finished.
    /// </summary>
    private const string ReplayScrollReset = @"() => {
        window.__scrollResetOver = false;
        let frame = 0;
        const step = () => {
          frame++;
          if (frame === 3) window.scrollTo({ top: document.documentElement.scrollHeight, behavior: 'instant' });
          if (frame === 24) {
            window.scrollTo({ top: 0, behavior: 'instant' });
            window.__scrollResetOver = true;
            return;
          }
          requestAnimationFrame(step);
        };
        requestAnimationFrame(step);
      }";

    /// <summary>
    /// <b>The first read waits out the scroll reset</b>
    /// (halheinrich/backgammon#434). The row-fit holds cannot stage this: they
    /// hold the row's fit, and the scroll is the browser's, there only where
    /// it animates scrolls. So the page is put through the scroll the fit is
    /// followed by (<see cref="ReplayScrollReset"/>), and the read taken during
    /// it comes back only once it is over. A read that does not wait, or one
    /// taken at the first frames at the top, returns before the replay has
    /// finished.
    /// </summary>
    [Fact]
    public async Task BelowTheFold_TheFirstRead_WaitsOutTheScrollReset()
    {
        await ReachFittedRowInAShortWindowAsync();
        await ExpectPageAtRestAtTheTopAsync(); // the real reset, where this browser animates it

        await Page.EvaluateAsync(ReplayScrollReset);
        var belowTheFold = await RowReachAtRestAsync();
        var replayOver = await Page.EvaluateAsync<bool>("() => window.__scrollResetOver");

        Assert.True(replayOver, $"the read came back while the page was still scrolling\n  {belowTheFold}");
        AssertEveryControlOffScreen(belowTheFold);
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
