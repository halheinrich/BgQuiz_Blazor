using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The spacebar performs the primary action, in a real browser (issue
/// <c>halheinrich/backgammon#149</c>, ruled 2026-09-02: always on, no setting).
/// Space does what clicking the dice already does — Continue at review, Submit
/// once a complete answer has enabled it — and only when focus is on nothing
/// that consumes space itself. The state rule is unit-pinned through the page's
/// callback; what only a browser can judge is the half in front of it: which
/// presses reach the page at all, decided by <c>quizKeys.js</c> from the real
/// event and the real focus. So every scenario here states where focus is
/// before it presses, and asserts it — a press from an unstated focus proves
/// nothing about the filter.
///
/// <para>
/// <b>Every absence carries a positive precondition</b> and is followed by the
/// same gesture succeeding once the state allows it: a listener that was never
/// attached would satisfy "Space did nothing" for free, so the scenarios that
/// expect nothing go on to expect something from the very same key.
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

    private ILocator ContinueButton => Page.GetByRole(AriaRole.Button, new() { Name = "Continue" });
    private ILocator RedoButton => Page.GetByRole(AriaRole.Button, new() { Name = "Redo" });
    private ILocator NoDoublePill => Page.GetByRole(AriaRole.Radio, new() { Name = "No double" });
    private ILocator TakePill => Page.GetByRole(AriaRole.Radio, new() { Name = "Take" });

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

    private Task<double> ScrollYAsync() => Page.EvaluateAsync<double>("() => window.scrollY");

    /// <summary>Boot, pick the one-problem cube fixture, and start — the staging every scenario shares.</summary>
    private async Task StartCubeQuizAsync()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();
    }

    [Fact]
    public async Task AtReview_WithFocusOnThePage_SpaceContinues()
    {
        await StartCubeQuizAsync();
        await AnswerCubeNoDoubleTakeAsync();

        // Focus is on the body, and that is the ordinary case, not staging: the
        // Submit click left focus on a button the review render then removed,
        // so after every Submit a user's next press lands on the page itself.
        // Asserted rather than assumed — the filter's "everything else fires"
        // branch is the one under test.
        await Expect(ContinueButton).ToBeVisibleAsync();
        Assert.Equal("body", await ActiveElementAsync());

        await Page.Keyboard.PressAsync("Space");

        // Exactly what Continue does: the one problem is behind us, so Done.
        await ExpectUrlAsync("/done");
    }

    [Fact]
    public async Task WhileAnsweringACube_SpaceFromACheckedPill_Submits()
    {
        await StartCubeQuizAsync();

        // Both halves chosen by clicking, which leaves focus on the pill clicked
        // last — a CHECKED radio, the one focus state the ruling carves out for
        // firing: space on it does nothing natively, so the shortcut may have it.
        await NoDoublePill.CheckAsync();
        await TakePill.CheckAsync();
        await Expect(SubmitButton).ToBeEnabledAsync();
        Assert.Equal("radio checked", await ActiveElementAsync());

        await Page.Keyboard.PressAsync("Space");

        // Submitted, and scored as the two clicks answered it.
        await Expect(ContinueButton).ToBeVisibleAsync();
        await Expect(VerdictBand).ToContainTextAsync("No Double: correct · Take: correct");
    }

    [Fact]
    public async Task WhileAnsweringACube_SpaceWithHalfAnAnswer_DoesNothing_UntilTheAnswerIsComplete()
    {
        await StartCubeQuizAsync();

        // Half an answer: one pill checked (and focused), Submit dark.
        await NoDoublePill.CheckAsync();
        await Expect(NoDoublePill).ToBeCheckedAsync();
        await Expect(SubmitButton).ToBeDisabledAsync();
        Assert.Equal("radio checked", await ActiveElementAsync());
        double scrollBefore = await ScrollYAsync();

        await Page.Keyboard.PressAsync("Space");

        // Still answering: the pill is still checked (space on a checked radio
        // changed nothing, and nothing un-chose it), Submit is still there and
        // still dark, no review appeared, and the page did not scroll (the
        // press was swallowed, not passed on as a page-down).
        await Expect(NoDoublePill).ToBeCheckedAsync();
        await Expect(SubmitButton).ToBeDisabledAsync();
        await Expect(ContinueButton).ToHaveCountAsync(0);
        Assert.Equal(scrollBefore, await ScrollYAsync());

        // The positive half of the absence above: the same key, once the state
        // allows it, does the thing — so the listener was live when it refused.
        await TakePill.CheckAsync();
        await Expect(SubmitButton).ToBeEnabledAsync();
        Assert.Equal("radio checked", await ActiveElementAsync());

        await Page.Keyboard.PressAsync("Space");

        await Expect(ContinueButton).ToBeVisibleAsync();
    }

    [Fact]
    public async Task OnAFocusedUncheckedPill_SpaceSelectsIt_AndDoesNotSubmit()
    {
        // The other side of the radio carve-out: space on an UNCHECKED focused
        // radio must still select it — the browser's own behaviour, which the
        // filter must not pre-empt. So the shortcut yields, and the selection
        // completes the answer without submitting it; the next press, now from
        // a checked pill, is the one that submits.
        await StartCubeQuizAsync();
        await NoDoublePill.CheckAsync();
        await TakePill.FocusAsync();
        await Expect(TakePill).Not.ToBeCheckedAsync();
        Assert.Equal("radio unchecked", await ActiveElementAsync());

        await Page.Keyboard.PressAsync("Space");

        // Selected by the browser, not submitted by the shortcut: the answer is
        // now complete (Submit lit) and the page is still answering.
        await Expect(TakePill).ToBeCheckedAsync();
        await Expect(SubmitButton).ToBeEnabledAsync();
        await Expect(ContinueButton).ToHaveCountAsync(0);
        Assert.Equal("radio checked", await ActiveElementAsync());

        await Page.Keyboard.PressAsync("Space");

        await Expect(ContinueButton).ToBeVisibleAsync();
    }

    [Fact]
    public async Task OnAFocusedButton_SpaceIsThatButtonsOwnPress_NotTheShortcut()
    {
        await StartCubeQuizAsync();
        await AnswerCubeNoDoubleTakeAsync();
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
}
