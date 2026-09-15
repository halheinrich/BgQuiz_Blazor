using Microsoft.Playwright;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The decision's notes in a real browser (issue
/// <c>halheinrich/backgammon#31</c>, conforming to <c>SPEC-quiz-view.md</c>
/// §4's 2026-09-15 amendment): review offers a <b>Notes</b> control after
/// Redo when the decision carries a comment, and it opens the text as an
/// overlay above the page — nothing reflows, the board does not move, Esc or a
/// click outside closes it, and focus comes back to the control.
///
/// <para>
/// <b>What only a browser can judge, and so what this suite is for.</b> The
/// component's state machine is bUnit-pinned (<c>DecisionNotesTests</c> in the
/// unit project). What bUnit cannot see is everything CSS and the real event
/// loop decide: that a CRLF crossing the real parse renders as one line break
/// with the author's spacing intact, that the fixed overlay leaves the board
/// exactly where it was, that the backdrop really covers the page — the
/// navigation panel included — that focus really lands and returns, that the
/// page's Space shortcut stays out of the open notes, and that the review row
/// stays one line with the control in it.
/// </para>
///
/// <para>
/// <b>Literals, not constants</b>, per this suite's posture: the control's and
/// the dialog's accessible names are spelled here. The comment texts are the
/// exception, and a principled one — they are <see cref="SyntheticXgMatch"/>'s
/// construction parameters, what the file was <i>told</i> to carry, never an
/// observation of the app.
/// </para>
/// </summary>
public sealed class DecisionNotesTests : E2eTestBase
{
    /// <summary>xUnit's per-test output sink, for the review row's measured geometry.</summary>
    private readonly ITestOutputHelper _output;

    public DecisionNotesTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright)
    {
        _output = output;
    }

    /// <summary>
    /// The floor SPEC-quiz-view §4's amendment asks the row to be measured at:
    /// 1280×800 with the navigation panel showing — the tightest desktop width
    /// the row's rulings are measured against, and the one the locator pins use.
    /// </summary>
    private const int DesktopWidth = 1280;

    private const int DesktopHeight = 800;

    /// <summary>
    /// The Notes control, by its accessible name — exact, because Playwright
    /// matches names by substring and the dialog's close button is "Close notes".
    /// </summary>
    private ILocator NotesButton => Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.NotesButton, Exact = true });

    /// <summary>The overlay, as assistive technology is told it is: a dialog named Notes.</summary>
    private ILocator NotesDialog => Page.GetByRole(AriaRole.Dialog, new() { Name = ExpectedText.NotesButton });

    private ILocator NotesText => NotesDialog.Locator(".decision-notes-text");

    private ILocator ContinueButton => Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton });

    private ILocator CollapseRail =>
        Page.GetByRole(AriaRole.Checkbox, new() { Name = ExpectedText.HideNavigationPanelCheckbox });

    private async Task StartTheSynthesizedMatchAsync()
    {
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await BootHomeAsync();
        await PickSynthesizedFileAsync(SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
        await ApplyFilterAsync();
        await StartQuizAsync();
    }

    [Fact]
    public async Task CubeNotes_OpenOverThePage_AsStored_AndCloseBackToTheControl()
    {
        await StartTheSynthesizedMatchAsync();

        // Answering: the comment is a spoiler, so there is no control — beside
        // the positive precondition that this IS the cube's answering row.
        await Expect(Page.Locator(".bg-cube-actions")).ToHaveCountAsync(1);
        await Expect(NotesButton).ToHaveCountAsync(0);

        await AnswerCubeAsync(ExpectedText.DoubleTakePill);

        // Review: the control is offered, closed, and the row is still one line
        // at the floor with the control in it.
        await Expect(NotesButton).ToBeVisibleAsync();
        await Expect(NotesButton).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(CollapseRail).Not.ToBeCheckedAsync();
        await AssertReviewRowIsOneLineAsync();

        var boardBefore = await LaidOutBoxAsync(Page.Locator(".board-container"), "the board region");

        // Open: a dialog named Notes, holding the text as the file stored it,
        // with focus inside it.
        await NotesButton.ClickAsync();
        await Expect(NotesDialog).ToBeVisibleAsync();
        await Expect(NotesButton).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(NotesDialog).ToBeFocusedAsync();
        await AssertNotesShowAsStoredAsync(SyntheticXgMatch.CubeComment);

        // Nothing reflowed: the board is exactly where it was, to the pixel.
        var boardOpen = await LaidOutBoxAsync(Page.Locator(".board-container"), "the board region");
        Assert.Equal((boardBefore.X, boardBefore.Y, boardBefore.Width, boardBefore.Height),
            (boardOpen.X, boardOpen.Y, boardOpen.Width, boardOpen.Height));

        // And the overlay is above it: the topmost element at the board's
        // centre belongs to the overlay, not to the board.
        Assert.True(
            await Page.EvaluateAsync<bool>(
                "p => !!document.elementFromPoint(p.x, p.y)?.closest('.decision-notes, .decision-notes-backdrop')",
                // double, not the box's float: Playwright .NET does not
                // serialize System.Single, and a float arrives as NaN.
                new { x = (double)(boardOpen.X + boardOpen.Width / 2), y = (double)(boardOpen.Y + boardOpen.Height / 2) }),
            "the notes overlay is the topmost thing over the board's centre");

        // Space inside the open notes is theirs, not the page's: it must not
        // press Continue behind the overlay (quizKeys.js's focus filter). The
        // absence is made to mean something by what follows — had the press
        // continued, the review this control belongs to would be gone before
        // the next line could find it.
        await Page.Keyboard.PressAsync(" ");
        await Expect(NotesDialog).ToBeVisibleAsync();

        // Esc closes it and hands focus back to the control, on the same review.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(NotesDialog).ToHaveCountAsync(0);
        await Expect(NotesButton).ToBeFocusedAsync();
        await Expect(NotesButton).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(ContinueButton).ToBeVisibleAsync();

        // A click outside closes it too — taken on the navigation panel's Home
        // link, the far edge of "outside": a backdrop that failed to cover the
        // panel would let this click navigate away, and the URL would say so.
        await NotesButton.ClickAsync();
        await Expect(NotesDialog).ToBeVisibleAsync();
        var homeLink = await LaidOutBoxAsync(
            Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.HomeNavLink }), "the navigation panel's Home link");
        await Page.Mouse.ClickAsync(homeLink.X + homeLink.Width / 2, homeLink.Y + homeLink.Height / 2);
        await Expect(NotesDialog).ToHaveCountAsync(0);
        await Expect(NotesButton).ToBeFocusedAsync();
        await ExpectUrlAsync("/quiz");

        // And the visible close button, the one route a reader can see.
        await NotesButton.ClickAsync();
        await NotesDialog.GetByRole(AriaRole.Button, new() { Name = "Close notes" }).ClickAsync();
        await Expect(NotesDialog).ToHaveCountAsync(0);
        await Expect(NotesButton).ToBeFocusedAsync();

        // The positive half of the Space pin: with focus off the notes and off
        // every control, the very same key continues — so the listener was
        // attached all along, and the earlier press did nothing because it was
        // inside the notes.
        await Page.EvaluateAsync("() => document.activeElement?.blur()");
        await Page.Keyboard.PressAsync(" ");
        await Expect(SubmitButton).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PlayNotes_AreOfferedAtReviewOnly_AndShowTheirLineBreak()
    {
        await StartTheSynthesizedMatchAsync();

        // Past the cube (the file's first problem) to the checker play.
        await AnswerCubeAsync(ExpectedText.DoubleTakePill);
        await ContinueButton.ClickAsync();

        // Answering the play: the play-entry board, and no notes.
        await Expect(Page.Locator(".board-container .bg-play-entry")).ToBeVisibleAsync();
        await Expect(NotesButton).ToHaveCountAsync(0);

        // The fixture's play is 8/5 6/5 on a 3-1, its one analysed candidate:
        // the entry consumes the leftmost rendered die first, so the 8-point
        // takes the 3 and the 6-point the 1.
        await ClickBoardPointAsync(8);
        await ClickBoardPointAsync(6);
        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(ContinueButton).ToBeVisibleAsync();

        await NotesButton.ClickAsync();
        await Expect(NotesDialog).ToBeVisibleAsync();
        await AssertNotesShowAsStoredAsync(SyntheticXgMatch.PlayComment);
    }

    [Fact]
    public async Task ADecisionWithoutAComment_OffersNoNotes()
    {
        // Every committed fixture's comment is empty (measured through the
        // converter), so the primary-path cube fixture is the no-notes case.
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();

        // Positive precondition: this is review, where notes would be offered.
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.RedoButton })).ToBeVisibleAsync();
        await Expect(NotesButton).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The notes read exactly as the fixture stored them, in two ways a DOM
    /// check alone cannot make. <c>textContent</c> is the stored string,
    /// CRLF and all — nothing between the parse and the page normalized it.
    /// <c>innerText</c>, which applies the element's <i>rendered</i> white-space
    /// handling, is the same string too — so the run of spaces and the line
    /// break are both on screen, not merely in the DOM (under the default
    /// <c>white-space</c> the run would collapse and the break read as a
    /// space). And the text's line boxes number one more than its breaks: the
    /// CRLF renders as exactly one break, not none and not two.
    /// </summary>
    private async Task AssertNotesShowAsStoredAsync(string stored)
    {
        Assert.Equal(stored, await NotesText.EvaluateAsync<string>("e => e.textContent"));
        Assert.Equal(stored, await NotesText.EvaluateAsync<string>("e => e.innerText"));

        int breaks = stored.Split("\r\n").Length - 1;
        int lines = await NotesText.EvaluateAsync<int>("""
            e => {
              const range = document.createRange();
              range.selectNodeContents(e);
              return new Set([...range.getClientRects()]
                .filter(r => r.width > 0)
                .map(r => Math.round(r.top))).size;
            }
            """);
        Assert.Equal(breaks + 1, lines);
    }

    /// <summary>
    /// SPEC-quiz-view.md §4's build-time measurement for the notes: the review
    /// row stays <b>one line</b> at the 1280×800 floor with the navigation panel
    /// showing and the control present. One line means the row is no taller
    /// than its tallest control — the primary button, which since
    /// halheinrich/backgammon#148 is the row's height by construction — so that
    /// is the assertion, beside the control's own levelness with it. Every
    /// child's box goes to the test output first, so a failure on another
    /// machine's fonts arrives with the evidence rather than just the verdict.
    /// </summary>
    private async Task AssertReviewRowIsOneLineAsync()
    {
        string report = await Page.Locator(".action-row").EvaluateAsync<string>("""
            row => {
              const r1 = n => Math.round(n * 10) / 10;
              const geom = e => { const b = e.getBoundingClientRect();
                return 'x=' + r1(b.x) + ' y=' + r1(b.y) + ' w=' + r1(b.width) + ' h=' + r1(b.height); };
              return ['row ' + geom(row)].concat([...row.children].map((k, i) =>
                '  [' + i + '] <' + k.tagName.toLowerCase() + ' class="' + k.className + '"> '
                  + geom(k) + ' text="' + (k.textContent || '').trim().slice(0, 24) + '"'))
                .join(String.fromCharCode(10));
            }
            """);
        _output.WriteLine("[review row geometry] synthesized .xg, cube review, notes present, panel showing");
        _output.WriteLine(report);

        await ExpectToPassAsync(async () =>
        {
            var row = await LaidOutBoxAsync(Page.Locator(".action-row"), "the action row");
            var primary = await LaidOutBoxAsync(ContinueButton, "Continue");
            var notes = await LaidOutBoxAsync(NotesButton, "the Notes control");
            var tail = await LaidOutBoxAsync(Page.Locator(".action-row-tail"), "the trailing cluster");

            Assert.True(
                row.Height <= primary.Height + 1,
                $"The review row should be one line — no taller than Continue; row.Height={row.Height}, "
                + $"Continue.Height={primary.Height}.\n{report}");
            Assert.True(
                Math.Abs(notes.Y + notes.Height / 2 - (primary.Y + primary.Height / 2)) < primary.Height / 2,
                $"The Notes control should sit on Continue's line; notes.Y={notes.Y}, Continue.Y={primary.Y}.\n{report}");
            Assert.True(
                tail.Height <= primary.Height + 1,
                $"The trailing cluster should be one line; tail.Height={tail.Height}.\n{report}");
        });
    }
}
