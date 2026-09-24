using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The spacebar, in a real browser (issue <c>halheinrich/backgammon#149</c>,
/// ruled 2026-09-02: always on, no setting; amended by
/// <c>halheinrich/backgammon#200</c>, ruled 2026-09-23). Space presses
/// Continue on the solution view and Skip while answering — Skip whenever Skip
/// is available, a complete answer included, so Space never submits — and
/// only when focus is on nothing that consumes space itself. The state rule is
/// unit-pinned through the page's callback; what only a browser can judge is
/// the half in front of it: which presses reach the page at all, decided by
/// <c>quizKeys.js</c> from the real event and the real focus, and whether the
/// published callback does what the buttons do. So every scenario here states
/// where focus is before it presses, and asserts it — a press from an unstated
/// focus proves nothing about the filter.
///
/// <para>
/// <b>Every scenario waits for the readiness mark before its first press</b>
/// (<c>halheinrich/backgammon#198</c>). The page attaches the listener only
/// once its module import lands, and on a cold host that is after every
/// control has rendered; a press is not a retrying assertion, so without the
/// wait a press can land on nothing and fail — or, for an absence, pass for
/// free.
/// </para>
///
/// <para>
/// <b>Every absence is followed by the same key succeeding</b> once the state
/// allows it, so a scenario that expects Space to do nothing also proves the
/// listener was live when it did nothing.
/// </para>
///
/// <para>
/// A skip is read off the score panel on <c>Done</c> — <c>Skipped</c> up and
/// <c>Submitted</c> still zero — because the fixtures here are one problem
/// long, so skipping the problem ends the run. That pair is the whole claim:
/// the answer was moved past, not scored.
/// </para>
///
/// <para>
/// This is also the app's first <c>[JSInvokable]</c> callback surviving the
/// published artifact's trimming and AOT — the fixture publishes exactly what
/// ships, so a callback the trimmer dropped would fail here, not in production.
/// </para>
/// </summary>
public sealed class KeyboardShortcutTests : E2eTestBase
{
    public KeyboardShortcutTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private ILocator ContinueButton => Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton });
    private ILocator RedoButton => Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.RedoButton });
    private ILocator NoDoublePill => Page.GetByRole(AriaRole.Radio, new() { Name = ExpectedText.NoDoublePill });
    private ILocator DoubleTakePill => Page.GetByRole(AriaRole.Radio, new() { Name = ExpectedText.DoubleTakePill });

    /// <summary>The XGID badge's text — the identity of the problem on screen.</summary>
    private ILocator XgidBadgeText => Page.Locator(".action-row-tail .xgid-label-text");

    /// <summary>
    /// The document's active element, described the way the filter in
    /// <c>quizKeys.js</c> sees it: tag name, and for a radio whether it is
    /// checked. Read at the moment of the press, which is the moment the filter
    /// reads it.
    /// </summary>
    private Task<string> ActiveElementAsync() => Page.EvaluateAsync<string>("""
        () => {
          const el = document.activeElement;
          if (!el) return '(none)';
          const tag = el.tagName.toLowerCase();
          if (el instanceof HTMLInputElement && el.type === 'radio')
            return 'radio ' + (el.checked ? 'checked' : 'unchecked');
          return tag === 'button' ? 'button ' + (el.textContent || '').trim() : tag;
        }
        """);

    /// <summary>
    /// Boot, pick <paramref name="fixture"/> (one problem), start, and wait for
    /// the keyboard module to attach — the staging every scenario shares.
    /// </summary>
    private async Task StartQuizOnAsync(string fixture)
    {
        await BootHomeAsync();
        await PickFixtureAsync(fixture);
        await ApplyFilterAsync();
        await StartQuizAsync();
        await ExpectKeyboardShortcutReadyAsync();
    }

    /// <summary>
    /// The one-problem run ended by a skip: on <c>Done</c>, one problem
    /// skipped and nothing submitted.
    /// </summary>
    private async Task ExpectSkippedUnsubmittedAsync()
    {
        await ExpectUrlAsync("/done");
        var body = Page.Locator("body");
        await Expect(body).ToContainTextAsync(ExpectedText.Skipped(1));
        await Expect(body).ToContainTextAsync(ExpectedText.Submitted(0));
    }

    [Fact]
    public async Task AtReview_WithFocusOnThePage_SpaceContinues()
    {
        await StartQuizOnAsync(CubeFixture);
        await AnswerCubeNoDoubleAsync();

        // Focus is on the body, and that is the ordinary case, not staging: the
        // Submit click left focus on a button the review render then removed,
        // so after every Submit a user's next press lands on the page itself.
        // Asserted rather than assumed — the filter's "everything else fires"
        // branch is the one under test.
        await Expect(ContinueButton).ToBeVisibleAsync();
        Assert.Equal("body", await ActiveElementAsync());

        await Page.Keyboard.PressAsync("Space");

        // Exactly what Continue does: the one problem is behind us, so Done —
        // with the answer scored and nothing skipped.
        await ExpectUrlAsync("/done");
        await Expect(Page.Locator("body")).ToContainTextAsync(ExpectedText.Skipped(0));
    }

    [Fact]
    public async Task WhileAnsweringACube_SpaceFromACheckedPill_Skips_AndDoesNotSubmit()
    {
        await StartQuizOnAsync(CubeFixture);

        // The answer chosen by clicking — one pill is a complete pair since
        // halheinrich/backgammon#187 — which leaves focus on the pill clicked:
        // a CHECKED radio, where space does nothing natively, so the shortcut
        // may have it. Submit is lit: this is the complete-answer case, where
        // Space used to submit.
        await NoDoublePill.CheckAsync();
        await Expect(SubmitButton).ToBeEnabledAsync();
        Assert.Equal("radio checked", await ActiveElementAsync());

        await Page.Keyboard.PressAsync("Space");

        // Skipped, the chosen answer with it: no review was ever shown, and the
        // run's totals say moved past rather than scored.
        await ExpectSkippedUnsubmittedAsync();
    }

    [Fact]
    public async Task WhileAnsweringACube_SpaceWithNothingChosen_Skips()
    {
        await StartQuizOnAsync(CubeFixture);

        // Nothing chosen, Submit dark, and focus on the body — where Start
        // left it, the Start button having gone with the setup page. The body
        // is the filter's "everything else fires" branch, so the press reaches
        // the page, and Skip is available.
        await Expect(SubmitButton).ToBeDisabledAsync();
        Assert.Equal("body", await ActiveElementAsync());

        await Page.Keyboard.PressAsync("Space");

        await ExpectSkippedUnsubmittedAsync();
    }

    [Fact]
    public async Task WhileAnsweringACheckerPlay_SpaceWithTheWholePlayEntered_Skips_AndDoesNotSubmit()
    {
        // The one surprise the ruling carries for users of v1.11.0, on the
        // problem kind where it bites hardest: a whole play built on the board,
        // Submit lit, and Space moves past it unscored.
        await StartQuizOnAsync(CheckerFixture);

        // The fixture's 6-5 is entered as QuizFlowTests enters it: one-click
        // source-advance takes the leftmost die first, so 24 moves 24/18 and 18
        // moves 18/13 — the whole play. Clicking the board's SVG takes no
        // focus, so the press comes from the body.
        await ClickBoardPointAsync(24);
        await ClickBoardPointAsync(18);
        await Expect(SubmitButton).ToBeEnabledAsync();
        Assert.Equal("body", await ActiveElementAsync());

        await Page.Keyboard.PressAsync("Space");

        await ExpectSkippedUnsubmittedAsync();
    }

    [Fact]
    public async Task OnAFocusedUncheckedPill_SpaceSelectsIt_AndDoesNotSkip()
    {
        // The other side of the radio carve-out: space on an UNCHECKED focused
        // radio must still select it — the browser's own behaviour, which the
        // filter must not pre-empt. So the shortcut yields, and the selection
        // changes the answer without skipping it; the next press, now from a
        // checked pill, is the one that reaches the page — and skips.
        await StartQuizOnAsync(CubeFixture);
        await NoDoublePill.CheckAsync();
        await DoubleTakePill.FocusAsync();
        await Expect(DoubleTakePill).Not.ToBeCheckedAsync();
        Assert.Equal("radio unchecked", await ActiveElementAsync());

        await Page.Keyboard.PressAsync("Space");

        // Selected by the browser, not skipped by the shortcut: the answer
        // moved to Double / Take (Submit still lit) and the page is still
        // answering the same problem.
        await Expect(DoubleTakePill).ToBeCheckedAsync();
        await Expect(NoDoublePill).Not.ToBeCheckedAsync();
        await Expect(SubmitButton).ToBeEnabledAsync();
        await ExpectUrlAsync("/quiz");
        Assert.Equal("radio checked", await ActiveElementAsync());

        await Page.Keyboard.PressAsync("Space");

        await ExpectSkippedUnsubmittedAsync();
    }

    [Fact]
    public async Task OnAFocusedButton_SpaceIsThatButtonsOwnPress_NotTheShortcut()
    {
        await StartQuizOnAsync(CubeFixture);
        await AnswerCubeNoDoubleAsync();
        string xgid = (await XgidBadgeText.TextContentAsync())!;
        Assert.False(string.IsNullOrWhiteSpace(xgid));

        // Focus on Redo — a button, which space activates natively. Had the
        // shortcut fired as well, Continue would have taken the one-problem
        // quiz to Done; Redo's own effect is the opposite direction, back to
        // answering the same problem, which is what makes the two
        // distinguishable from outside. The order is deterministic in the
        // shortcut's disfavour: keydown (where the shortcut listens) precedes
        // the click a button synthesizes on keyup, so a shortcut that fired
        // would Continue first and Redo would find the controller busy.
        await RedoButton.FocusAsync();
        Assert.Equal("button Redo", await ActiveElementAsync());

        await Page.Keyboard.PressAsync("Space");

        // Redo's effect, and only Redo's: answering again, same problem, still
        // on the quiz page.
        await Expect(SubmitButton).ToBeVisibleAsync();
        await Expect(ContinueButton).ToHaveCountAsync(0);
        await ExpectUrlAsync("/quiz");
        await Expect(XgidBadgeText).ToHaveTextAsync(xgid);
    }

    [Fact]
    public async Task TheReadinessMark_ReturnsWithTheNextQuizPage_AndSoDoesTheKey()
    {
        // Show stats disposes the quiz page and Back to quiz renders a new one
        // — the round trip a user makes, and the one that re-instantiates the
        // page, so the new instance must attach (and mark) afresh. Deliberately
        // no "the mark is gone at /stats" step: that holds whatever detach
        // does, because the move is an enhanced navigation and Blazor merges
        // the server's <html> element, which carries no mark, into the live
        // one (measured 2026-09-24 with a throwaway attribute). Detach's own
        // removal is pinned where it can fail — the next scenario.
        await StartQuizOnAsync(CubeFixture);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Show stats" }).ClickAsync();
        await ExpectUrlAsync("/stats");

        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToQuizButton }).ClickAsync();
        await ExpectUrlAsync("/quiz");
        await ExpectKeyboardShortcutReadyAsync();

        // The mark back means the key is back: the new page's listener
        // answers the very next press.
        Assert.Equal("body", await ActiveElementAsync());
        await Page.Keyboard.PressAsync("Space");
        await ExpectSkippedUnsubmittedAsync();
    }

    [Fact]
    public async Task Detaching_TakesTheMarkAndTheListenerAway_Together()
    {
        // The mark's promise is "present exactly while Space is listened
        // for". In the app, detach only ever runs as the page leaves, where
        // navigation strips the mark anyway (see the round-trip scenario), so
        // the module's own half of the promise is pinned at the module: detach
        // is called on the very instance the page attached, and afterwards the
        // mark is gone AND a press does nothing. The module's path is this
        // suite's own literal — the import map resolves it to the same
        // fingerprinted URL, so the import returns the page's instance.
        await StartQuizOnAsync(CubeFixture);
        Assert.Equal("body", await ActiveElementAsync());

        await Page.EvaluateAsync("async () => (await import('./js/quizKeys.js')).detach()");

        await Expect(KeyboardShortcutMark).ToHaveCountAsync(0);

        // A listener left behind would not reach the page — detach drops the
        // reference it calls through — so "nothing skipped" alone cannot see
        // one. What it would do is swallow the key and throw on the null
        // reference, an uncaught page error; so the press is also watched for
        // errors, which is what makes the listener half of detach observable.
        var pageErrors = new List<string>();
        Page.PageError += (_, error) => pageErrors.Add(error);
        await Page.Keyboard.PressAsync("Space");

        // Nothing skipped: still answering the one problem. The positive half
        // is every other scenario here — the same press, listener attached,
        // skips.
        await Expect(SubmitButton).ToBeVisibleAsync();
        await ExpectUrlAsync("/quiz");
        // A round trip through the page after the press: any error the
        // keydown raised has been dispatched by the time this returns.
        await Page.EvaluateAsync("() => new Promise(requestAnimationFrame)");
        Assert.Empty(pageErrors);
    }
}
