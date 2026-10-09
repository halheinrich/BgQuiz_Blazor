using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Typing into the filter panel's fields in a real browser — the checks the
/// producer reviews handed to this host (halheinrich/backgammon#374, comments
/// 6072588111 and 6075603033): bUnit sets a value, it does not type one, so
/// what real keystrokes, blur, focus and the caret do is only observable here.
///
/// <list type="bullet">
/// <item><b>The bound boxes</b> (halheinrich/backgammon#379): blank text is no
/// bound; unfinished text is invalid and kept as typed; either decimal mark
/// reads as one; text survives navigating away and back; a correction clears
/// its message; <c>1e999</c> is not a finite bound.</item>
/// <item><b>The halheinrich/backgammon#272 sequence</b> (<c>SPEC-filtering.md</c>
/// §1, "How an invalid field is shown"): a newly typed error's message waits
/// for the user to leave the field and clears as soon as the value is right,
/// including the match-score field's distinct fault kinds.</item>
/// <item><b>Focus, caret and layout</b>: typing keeps both, and nothing below
/// the field moves while an error is shown, corrected or retyped — measured
/// as geometry, never as a timing.</item>
/// </list>
///
/// <para>
/// Every scenario picks a folder and waits for the panel to report its
/// restoration settled before touching it (<see cref="E2eTestBase.WaitForFilterRestorationAsync"/>).
/// Validity is read off the panel's own gate (Apply) and Home's (Start); the
/// words off the feedback lines, by id while shown. A corrected line keeps its
/// box, saying nothing, which the producer marks with <c>data-held-for</c>.
/// </para>
/// </summary>
public sealed class FilterFieldEntryTests : E2eTestBase
{
    public FilterFieldEntryTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private ILocator ErrorMin => Page.Locator("#errorMin");

    private ILocator ErrorMax => Page.Locator("#errorMax");

    private ILocator ApplyButton => Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ApplyFilterButton });

    /// <summary>The error range's feedback line while it is shown — it carries its id only then.</summary>
    private ILocator ErrorRangeLine => Page.Locator("#errorRangeFeedback");

    /// <summary>The error range's feedback box while corrected: the same box, saying nothing.</summary>
    private ILocator ErrorRangeHeldBox => Page.Locator("[data-held-for='errorRangeFeedback']");

    private ILocator MatchScores => Page.Locator("input[aria-describedby^='facetHint_MatchScores']");

    private ILocator MatchScoreLine => Page.Locator("#matchScoreFeedback");

    /// <summary>A control below the error range, whose place says whether the range above it moved.</summary>
    private ILocator BelowTheRange => Page.Locator("#moreFiltersToggle");

    private async Task PickAndSettleAsync()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ExpectFilterInEffectAsync();
    }

    /// <summary>Type <paramref name="text"/> key by key into the focused field.</summary>
    private Task TypeAsync(ILocator field, string text) => field.PressSequentiallyAsync(text);

    /// <summary>Empty the field with keystrokes, as a user would: select all, then delete.</summary>
    private async Task EraseAsync(ILocator field)
    {
        await field.PressAsync("Control+a");
        await field.PressAsync("Backspace");
    }

    /// <summary>Leave the field the way a user does, by tabbing on.</summary>
    private Task LeaveAsync(ILocator field) => field.PressAsync("Tab");

    private async Task<double> TopOfAsync(ILocator locator)
    {
        var box = await locator.BoundingBoxAsync() ?? throw new InvalidOperationException("Not laid out.");
        return (double)box.Y;
    }

    /// <summary>The id of the element holding focus, and the caret's place in it.</summary>
    private async Task<FocusState> FocusAsync()
    {
        var id = await Page.EvaluateAsync<string>("() => document.activeElement?.id ?? ''");
        var caret = await Page.EvaluateAsync<int>("() => document.activeElement?.selectionStart ?? -1");
        return new FocusState(id, caret);
    }

    private sealed record FocusState(string Id, int Caret);

    private async Task NavigateToSettingsAndBackAsync()
    {
        await Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.SettingsNavLink, Exact = true }).ClickAsync();
        await ExpectPageRenderedAsync(AppRoute.Settings);
        await Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.HomeNavLink, Exact = true }).ClickAsync();
        await ExpectPageRenderedAsync(AppRoute.Home);
        await WaitForFilterRestorationAsync();
    }

    [Fact]
    public async Task BlankIsNoBound_UnfinishedTextIsInvalidAndKept_AndAMessageWaitsForTheUserToLeave()
    {
        await PickAndSettleAsync();
        await Expect(StartButton).ToBeEnabledAsync(); // the empty selection, in effect

        // Unfinished, key by key: invalid at once — the gates close — but
        // kept as typed, and its message waits until the user leaves.
        await ErrorMin.ClickAsync();
        await TypeAsync(ErrorMin, "1e-");
        await Expect(ErrorMin).ToHaveValueAsync("1e-");
        await Expect(ApplyButton).ToBeDisabledAsync();
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(ErrorRangeLine).ToHaveCountAsync(0);

        await LeaveAsync(ErrorMin);
        await Expect(ErrorRangeLine).ToContainTextAsync(ExpectedText.ErrorRangeFeedback);
        await Expect(ErrorMin).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(ErrorMin).ToHaveValueAsync("1e-");

        // Blank is no bound and no fault: the message goes, the box is empty,
        // and with nothing set the empty selection is in effect again.
        await EraseAsync(ErrorMin);
        await Expect(ErrorMin).ToHaveValueAsync("");
        await Expect(ErrorRangeLine).ToHaveCountAsync(0);
        await Expect(ErrorMin).Not.ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(StartButton).ToBeEnabledAsync();

        // A bare sign is unfinished too.
        await TypeAsync(ErrorMin, "-");
        await Expect(ErrorMin).ToHaveValueAsync("-");
        await Expect(StartButton).ToBeDisabledAsync();
        await LeaveAsync(ErrorMin);
        await Expect(ErrorRangeLine).ToContainTextAsync(ExpectedText.ErrorRangeFeedback);
    }

    [Fact]
    public async Task EitherDecimalMarkReadsAsTheSameBound()
    {
        // Applied with a comma, then retyped with a point: the same number,
        // so the retyped selection is the one in effect — no "apply the
        // filters" hint and no second Apply, Apply off with nothing new to
        // commit. (In effect is read off the hint, not off Start, which also
        // answers to how many decisions the bound happens to admit.)
        await PickAndSettleAsync();
        var applyHint = Page.GetByText(ExpectedText.ApplyFiltersHint);

        await ErrorMin.ClickAsync();
        await TypeAsync(ErrorMin, "0,05");
        await Expect(ApplyButton).ToBeEnabledAsync(); // a valid bound, not yet applied
        await Expect(applyHint).ToBeVisibleAsync();
        await ApplyFilterAsync();

        await EraseAsync(ErrorMin);
        await Expect(applyHint).ToHaveCountAsync(0); // blank: the empty selection, in effect
        await TypeAsync(ErrorMin, "0.05");
        await Expect(ErrorMin).ToHaveValueAsync("0.05");
        await Expect(applyHint).ToHaveCountAsync(0);  // the applied bound, in effect again
        await Expect(ApplyButton).ToBeDisabledAsync();
        await Expect(ErrorRangeLine).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task OneEToTheNineHundredNinetyNine_IsRefusedAsNotFinite()
    {
        await PickAndSettleAsync();

        await ErrorMax.ClickAsync();
        await TypeAsync(ErrorMax, "1e999");
        await Expect(ApplyButton).ToBeDisabledAsync();
        await Expect(StartButton).ToBeDisabledAsync();
        await LeaveAsync(ErrorMax);
        await Expect(ErrorRangeLine).ToContainTextAsync(ExpectedText.ErrorRangeFeedback);
        await Expect(ErrorMax).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(ErrorMax).ToHaveValueAsync("1e999");
    }

    [Fact]
    public async Task TypedTextSurvivesNavigatingAwayAndBack_AndAnInvalidOneShowsAtOnce()
    {
        // The draft lives in the app-scoped owner, as typed, so the boxes come
        // back holding the text — unfinished text included. Back on Home the
        // wrong value was not typed on this mount, so its message shows at once.
        await PickAndSettleAsync();

        await ErrorMin.ClickAsync();
        await TypeAsync(ErrorMin, "0,1");
        await ErrorMax.ClickAsync();
        await TypeAsync(ErrorMax, "1e-");

        await NavigateToSettingsAndBackAsync();

        await Expect(ErrorMin).ToHaveValueAsync("0,1");
        await Expect(ErrorMax).ToHaveValueAsync("1e-");
        await Expect(ErrorRangeLine).ToContainTextAsync(ExpectedText.ErrorRangeFeedback);
        await Expect(StartButton).ToBeDisabledAsync();
    }

    [Fact]
    public async Task The272Sequence_InvalidLeftReturnedCorrectedThenInvalidAgain()
    {
        await PickAndSettleAsync();

        // Invalid, then left: the message shows.
        await ErrorMin.ClickAsync();
        await TypeAsync(ErrorMin, "x");
        await LeaveAsync(ErrorMin);
        await Expect(ErrorRangeLine).ToContainTextAsync(ExpectedText.ErrorRangeFeedback);

        // Returned to and corrected: the message clears at once, while the
        // user is still in the field — its box stays, saying nothing.
        await ErrorMin.ClickAsync();
        await EraseAsync(ErrorMin);
        await TypeAsync(ErrorMin, "0.1");
        await Expect(ErrorRangeLine).ToHaveCountAsync(0);
        await Expect(ErrorRangeHeldBox).ToHaveCountAsync(1);
        await Expect(ErrorRangeHeldBox).ToBeHiddenAsync();
        await Expect(ApplyButton).ToBeEnabledAsync();

        // Made invalid again, still in the field: newly typed, so the message
        // waits for the user to leave.
        await TypeAsync(ErrorMin, "x");
        await Expect(ApplyButton).ToBeDisabledAsync();
        await Expect(ErrorRangeLine).ToHaveCountAsync(0);

        await LeaveAsync(ErrorMin);
        await Expect(ErrorRangeLine).ToContainTextAsync(ExpectedText.ErrorRangeFeedback);
    }

    [Fact]
    public async Task MatchScores_ANewlyTypedFaultKindWaitsForTheBlur_AndACorrectedKindClears()
    {
        await PickAndSettleAsync();
        await ExpandFacetRowAsync("MatchScores");

        // One kind — malformed — typed and left: its words show.
        await MatchScores.ClickAsync();
        await TypeAsync(MatchScores, "4a5x");
        await LeaveAsync(MatchScores);
        await Expect(MatchScoreLine).ToContainTextAsync(ExpectedText.MatchScoreMalformed);
        await Expect(MatchScoreLine.GetByText(ExpectedText.MatchScoreRetired)).ToHaveCountAsync(0);

        // A second kind — the retired token — typed while the first shows:
        // newly typed, so its words wait for the user to leave.
        await MatchScores.ClickAsync();
        await MatchScores.PressAsync("End");
        await TypeAsync(MatchScores, ", " + ExpectedText.RetiredMoneyToken);
        await Expect(MatchScores).ToHaveValueAsync("4a5x, " + ExpectedText.RetiredMoneyToken);
        await Expect(MatchScoreLine.GetByText(ExpectedText.MatchScoreRetired)).ToBeHiddenAsync();

        await LeaveAsync(MatchScores);
        await Expect(MatchScoreLine.GetByText(ExpectedText.MatchScoreRetired)).ToBeVisibleAsync();
        await Expect(MatchScoreLine.GetByText(ExpectedText.MatchScoreMalformed)).ToBeVisibleAsync();

        // The malformed token corrected in place — its one wrong character
        // replaced, in the field: its words clear at once, and the kind still
        // true stays. (Erasing the whole list would clear both kinds, and the
        // retired token typed back would then be newly typed, and wait.)
        await MatchScores.ClickAsync();
        await MatchScores.EvaluateAsync("el => el.setSelectionRange(3, 4)"); // the "x" of 4a5x
        await TypeAsync(MatchScores, "a");
        await Expect(MatchScores).ToHaveValueAsync("4a5a, " + ExpectedText.RetiredMoneyToken);
        await Expect(MatchScoreLine.GetByText(ExpectedText.MatchScoreMalformed)).ToBeHiddenAsync();
        await Expect(MatchScoreLine.GetByText(ExpectedText.MatchScoreRetired)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TypingKeepsFocusAndCaret_AndTheLayoutHoldsStillWhileAnErrorIsShownCorrectedOrRetyped()
    {
        await PickAndSettleAsync();

        // Focus and caret, key by key, including an edit in the middle of the
        // text: every keystroke re-renders the panel, and neither moves.
        await ErrorMin.ClickAsync();
        await TypeAsync(ErrorMin, "0.5");
        Assert.Equal(new FocusState("errorMin", 3), await FocusAsync());
        await ErrorMin.EvaluateAsync("el => el.setSelectionRange(1, 1)");
        await TypeAsync(ErrorMin, "0");
        await Expect(ErrorMin).ToHaveValueAsync("00.5");
        Assert.Equal(new FocusState("errorMin", 2), await FocusAsync());

        // Typing an error does not move what is below the range: no message
        // appears under the caret.
        await EraseAsync(ErrorMin);
        var beforeAnyMessage = await TopOfAsync(BelowTheRange);
        await TypeAsync(ErrorMin, "1e-");
        Assert.Equal(new FocusState("errorMin", 3), await FocusAsync());
        Assert.Equal(beforeAnyMessage, await TopOfAsync(BelowTheRange));

        // Shown on leaving — the one place the layout may change.
        await LeaveAsync(ErrorMin);
        await Expect(ErrorRangeLine).ToBeVisibleAsync();
        var whileShown = await TopOfAsync(BelowTheRange);

        // Corrected: the message goes and its box holds the place.
        await ErrorMin.ClickAsync();
        await EraseAsync(ErrorMin);
        await TypeAsync(ErrorMin, "0.1");
        await Expect(ErrorRangeLine).ToHaveCountAsync(0);
        Assert.Equal(whileShown, await TopOfAsync(BelowTheRange));

        // Retyped wrong: still nothing moves while the user types…
        await TypeAsync(ErrorMin, "x");
        Assert.Equal(new FocusState("errorMin", 4), await FocusAsync());
        Assert.Equal(whileShown, await TopOfAsync(BelowTheRange));

        // …and shown again, in the same box.
        await LeaveAsync(ErrorMin);
        await Expect(ErrorRangeLine).ToBeVisibleAsync();
        Assert.Equal(whileShown, await TopOfAsync(BelowTheRange));
    }
}
