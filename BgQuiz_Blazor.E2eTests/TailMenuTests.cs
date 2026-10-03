using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The action row's tail folds behind one "⋯" control below the width where it
/// fits beside the row's other controls (SPEC-quiz-view.md §4,
/// halheinrich/backgammon#264's widened fourth, Hal, 2026-10-03: "I'm good
/// with the ellipsis"). The width is the page's own live measurement, read in
/// the browser by <see cref="ActionRowGeometry.TailFoldWidthAsync"/>, so
/// nothing here spells a pixel boundary.
/// </summary>
/// <remarks>
/// <para>
/// The list follows the menu-button pattern: the toggle carries
/// <c>aria-haspopup</c> and <c>aria-expanded</c>; Enter or Space opens it and
/// the arrow keys move between the items; Escape, a click elsewhere or a choice
/// closes it, and where focus goes depends on what closed it. Space and Enter
/// used in the list never also press Submit or move the quiz on.
/// </para>
/// <para>
/// Every scenario but the first starts on the synthesized <c>.xg</c> match,
/// whose first problem is a cube decision with game and move numbers — the
/// widest tail there is — at a window just below the measured switch.
/// </para>
/// </remarks>
public sealed class TailMenuTests : E2eTestBase
{
    public TailMenuTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>Copy XGID writes the clipboard, and the scenario reads it back.</summary>
    protected override BrowserNewContextOptions ContextOptions =>
        new() { Permissions = ["clipboard-read", "clipboard-write"] };

    /// <summary>Wider text, everywhere on the page: the changed font metrics (as <c>RowFitTests</c>).</summary>
    private const string WiderText = "body { letter-spacing: 0.2em; }";

    /// <summary>
    /// A click on the page outside every control: the board's title strip, at
    /// its right-hand end, which no hit region covers and the drawer never
    /// reaches.
    /// </summary>
    private async Task ClickTheBoardsTitleStripAsync()
    {
        var board = await LaidOutBoxAsync(Page.Locator(".board-container"), "board");
        await Page.Mouse.ClickAsync(board.X + board.Width - 40, board.Y + 8);
    }

    private ILocator More =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.MoreButton, Exact = true });

    private ILocator Menu => Page.GetByRole(AriaRole.Menu);

    private ILocator Items => Page.GetByRole(AriaRole.Menuitem);

    private ILocator Item(string name) =>
        Page.GetByRole(AriaRole.Menuitem, new() { Name = name, Exact = true });

    private ILocator ContinueButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton });

    /// <summary>The match's cube decision's locator, in the full wording the list shows.</summary>
    private static string MatchLocatorLine =>
        $"{SyntheticXgMatch.StagedFileName} · Game {SyntheticXgMatch.CubeGameNumber} · Move {SyntheticXgMatch.CubeMoveNumber}";

    /// <summary>Two animation frames: the page's fit after a resize or a font change has run.</summary>
    private Task SettleAsync() =>
        Page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");

    private async Task ResizeAsync(int width, int height = 768)
    {
        await Page.SetViewportSizeAsync(width, height);
        await SettleAsync();
    }

    /// <summary>The focused element: a button by its accessible name, a radio by its state, otherwise its tag.</summary>
    private Task<string> FocusedAsync() => Page.EvaluateAsync<string>("""
        () => {
          const el = document.activeElement;
          if (!el) return '(none)';
          const tag = el.tagName.toLowerCase();
          if (el instanceof HTMLInputElement && el.type === 'radio')
            return 'radio ' + (el.checked ? 'checked' : 'unchecked');
          return tag === 'button' ? 'button ' + (el.getAttribute('aria-label') || el.textContent || '').trim() : tag;
        }
        """);

    /// <summary>The live tail's children, a button by its accessible name and anything else by its class.</summary>
    private Task<string[]> TailContentsAsync() => Page.EvaluateAsync<string[]>("""
        () => [...document.querySelector('.action-row > .action-row-tail').children]
          .map(c => c.tagName === 'BUTTON' ? c.getAttribute('aria-label') : c.className)
        """);

    private async Task StartOnTheMatchAsync()
    {
        await Page.SetViewportSizeAsync(1000, 768);
        await BootHomeAsync();
        await PickSynthesizedFileAsync(SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
        await ApplyFilterAsync();
        await StartQuizAsync();
        await Expect(Page.Locator(".action-row .bg-cube-actions")).ToBeVisibleAsync();
        await ExpectKeyboardShortcutReadyAsync();
    }

    /// <summary>
    /// The measured switch, read below the panel's own fold (800 px), then a
    /// window one pixel under it, where the tail is folded.
    /// </summary>
    private async Task<double> NarrowBelowTheSwitchAsync()
    {
        await ResizeAsync(800);
        var tailSwitch = await ActionRowGeometry.TailFoldWidthAsync(Page);
        await ResizeAsync((int)Math.Floor(tailSwitch) - 1);
        await Expect(More).ToBeVisibleAsync();
        return tailSwitch;
    }

    [Fact]
    public async Task BelowTheMeasuredWidth_TheTailFoldsBehindTheMenu_AndAboveIt_ShowsAsBefore()
    {
        await StartOnTheMatchAsync();
        await ResizeAsync(800);
        var foldWidth = await ActionRowGeometry.FoldWidthAsync(Page);
        var tailSwitch = await ActionRowGeometry.TailFoldWidthAsync(Page);
        Assert.InRange(tailSwitch, 641, foldWidth);   // under the panel's own fold, inside its layout band

        await ResizeAsync((int)Math.Floor(tailSwitch) - 1);
        Assert.Equal(["tail-menu"], await TailContentsAsync());
        Assert.True((await ActionRowGeometry.FitAsync(Page)).TailFolded);
        await Expect(More).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.Empty(await ActionRowGeometry.CoveredControlsAsync(Page));
        Assert.Equal(1, (await ActionRowGeometry.FitAsync(Page)).RowLines);

        await ResizeAsync((int)Math.Ceiling(tailSwitch) + 1);
        Assert.Equal(
            ["xgid-label", "problem-locator", ExpectedText.ShowStatsButton, ExpectedText.EndQuizButton],
            await TailContentsAsync());
        await Expect(More).ToHaveCountAsync(0);
        Assert.Empty(await ActionRowGeometry.CoveredControlsAsync(Page));
    }

    [Fact]
    public async Task WiderText_MovesTheSwitch_AndTheTailFoldsAtTheSameViewport()
    {
        // The changed-font case: the switch is the page's measurement under
        // the fonts rendering, so wider text raises it, and a window that held
        // the tail now folds it.
        await StartOnTheMatchAsync();
        await ResizeAsync(800);
        var before = await ActionRowGeometry.TailFoldWidthAsync(Page);
        await ResizeAsync((int)Math.Ceiling(before) + 20);
        await Expect(More).ToHaveCountAsync(0);

        await Page.AddStyleTagAsync(new() { Content = WiderText });

        await Expect(More).ToBeVisibleAsync();
        var after = await ActionRowGeometry.TailFoldWidthAsync(Page);
        Assert.True(after > before + 20, $"switch {before} -> {after}");
        Assert.Empty(await ActionRowGeometry.CoveredControlsAsync(Page));
    }

    [Fact]
    public async Task AtTheSwitchWidth_RepeatedResizesDoNotFlipBetweenTheTailAndTheMenu()
    {
        // The decision rests on the tail at full size (the ruler), never on
        // what the row is showing, so showing the "⋯" cannot make the row fit
        // and switch it back. Count every change of presentation while resize
        // events repeat on either side of the switch, and while the width
        // steps across it.
        await StartOnTheMatchAsync();
        await ResizeAsync(800);
        var tailSwitch = await ActionRowGeometry.TailFoldWidthAsync(Page);
        var below = (int)Math.Floor(tailSwitch) - 1;
        var above = (int)Math.Ceiling(tailSwitch) + 1;
        await Page.EvaluateAsync(@"() => {
            const tail = document.querySelector('.action-row > .action-row-tail');
            let folded = tail.querySelector('.tail-menu') !== null;
            window.__tailChanges = 0;
            new MutationObserver(() => {
              const now = tail.querySelector('.tail-menu') !== null;
              if (now !== folded) { folded = now; window.__tailChanges++; }
            }).observe(tail, { childList: true, subtree: true });
          }");

        foreach (var width in new[] { below, above })
        {
            await ResizeAsync(width);
            var changes = await Page.EvaluateAsync<int>("() => window.__tailChanges");
            await Page.EvaluateAsync(@"async () => {
                for (let i = 0; i < 25; i++) {
                  window.dispatchEvent(new Event('resize'));
                  await new Promise(r => requestAnimationFrame(r));
                }
              }");
            Assert.Equal(changes, await Page.EvaluateAsync<int>("() => window.__tailChanges"));
        }

        await Page.EvaluateAsync("() => { window.__tailChanges = 0; }");
        foreach (var width in new[] { below, above, below, above })
        {
            await ResizeAsync(width);
        }
        Assert.Equal(4, await Page.EvaluateAsync<int>("() => window.__tailChanges"));
    }

    [Fact]
    public async Task TheList_OffersEveryMemberOfTheTail_UnderItsOwnName_EndQuizLast()
    {
        await StartOnTheMatchAsync();
        await NarrowBelowTheSwitchAsync();
        await Expect(More).ToHaveAttributeAsync("aria-haspopup", "menu");
        var board = await LaidOutBoxAsync(Page.Locator(".board-container"), "board");

        await More.ClickAsync();

        await Expect(More).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Menu).ToHaveAccessibleNameAsync(ExpectedText.MoreButton);
        await Expect(Items).ToHaveCountAsync(3);
        await Expect(Items.Nth(0)).ToHaveAccessibleNameAsync(ExpectedText.CopyXgidButton);
        await Expect(Items.Nth(1)).ToHaveAccessibleNameAsync(ExpectedText.ShowStatsButton);
        await Expect(Items.Nth(2)).ToHaveAccessibleNameAsync(ExpectedText.EndQuizButton);
        Assert.Equal(
            [ExpectedText.CopyXgidButton, ExpectedText.ShowStatsButton, ExpectedText.EndQuizButton],
            (await Items.AllInnerTextsAsync()).Select(t => t.Trim()));

        // The locator: a line to read, in full, naming the group the copy
        // item sits in — not an item.
        await Expect(Menu.Locator(".tail-menu-line")).ToHaveTextAsync(MatchLocatorLine);
        var group = Page.GetByRole(AriaRole.Group, new() { Name = MatchLocatorLine, Exact = true });
        await Expect(group.GetByRole(AriaRole.Menuitem)).ToHaveAccessibleNameAsync(ExpectedText.CopyXgidButton);

        // Lying over the page: the board did not move and the row did not grow.
        var boardOpen = await LaidOutBoxAsync(Page.Locator(".board-container"), "board");
        Assert.Equal(board.Height, boardOpen.Height, 0.5);
        Assert.Equal(board.Y, boardOpen.Y, 0.5);
        Assert.Equal(1, (await ActionRowGeometry.FitAsync(Page)).RowLines);
    }

    [Fact]
    public async Task CopyXgid_CopiesTheXgid_ClosesTheList_AndFocusReturnsToTheToggle()
    {
        await StartOnTheMatchAsync();
        var xgid = await Page.Locator(".action-row-tail .xgid-label-text").GetAttributeAsync("title");
        Assert.StartsWith("XGID=", xgid);
        await NarrowBelowTheSwitchAsync();

        await More.ClickAsync();
        await Item(ExpectedText.CopyXgidButton).ClickAsync();

        Assert.Equal(xgid, await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()"));
        await Expect(Menu).ToHaveCountAsync(0);
        await Expect(More).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.Equal("button " + ExpectedText.MoreButton, await FocusedAsync());
        await ExpectUrlAsync("/quiz");
    }

    [Fact]
    public async Task ShowStats_GoesWhereItsButtonGoes_AndFocusIsWhereThatNavigationPutsIt()
    {
        // The button first, at a width that shows it, and where its navigation
        // leaves focus; then the item, from the list, compared with that.
        await StartOnTheMatchAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ShowStatsButton, Exact = true }).ClickAsync();
        await ExpectUrlAsync("/stats");
        var backToQuiz = Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToQuizButton });
        await Expect(backToQuiz).ToBeVisibleAsync();
        var focusAfterTheButton = await FocusedAsync();
        await backToQuiz.ClickAsync();
        await ExpectUrlAsync("/quiz");
        await ExpectKeyboardShortcutReadyAsync();

        await NarrowBelowTheSwitchAsync();
        await More.ClickAsync();
        await Item(ExpectedText.ShowStatsButton).ClickAsync();

        await ExpectUrlAsync("/stats");
        await Expect(backToQuiz).ToBeVisibleAsync();
        Assert.Equal(focusAfterTheButton, await FocusedAsync());
    }

    [Fact]
    public async Task TheKeyboard_OpensWithEnterOrSpace_MovesWithTheArrows_AndEscapeReturnsFocusToTheToggle()
    {
        await StartOnTheMatchAsync();
        await NarrowBelowTheSwitchAsync();
        await More.FocusAsync();

        await Page.Keyboard.PressAsync("Enter");
        await Expect(Menu).ToBeVisibleAsync();
        await Expect(More).ToHaveAttributeAsync("aria-expanded", "true");
        await ExpectToPassAsync(async () =>
            Assert.Equal("button " + ExpectedText.CopyXgidButton, await FocusedAsync()));

        foreach (var (key, expected) in new[]
        {
            ("ArrowDown", ExpectedText.ShowStatsButton),
            ("ArrowDown", ExpectedText.EndQuizButton),
            ("ArrowDown", ExpectedText.CopyXgidButton),   // wraps
            ("ArrowUp", ExpectedText.EndQuizButton),      // and back
            ("Home", ExpectedText.CopyXgidButton),
            ("End", ExpectedText.EndQuizButton),
        })
        {
            await Page.Keyboard.PressAsync(key);
            Assert.Equal("button " + expected, await FocusedAsync());
        }

        await Page.Keyboard.PressAsync("Escape");
        await Expect(Menu).ToHaveCountAsync(0);
        await ExpectToPassAsync(async () =>
            Assert.Equal("button " + ExpectedText.MoreButton, await FocusedAsync()));

        await Page.Keyboard.PressAsync("Space");
        await Expect(Menu).ToBeVisibleAsync();
        await ExpectToPassAsync(async () =>
            Assert.Equal("button " + ExpectedText.CopyXgidButton, await FocusedAsync()));
    }

    [Fact]
    public async Task ChoosingByKeyboard_DoesWhatTheItemDoes_AndFocusReturnsToTheToggle()
    {
        await StartOnTheMatchAsync();
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing yet')");
        await NarrowBelowTheSwitchAsync();
        await More.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Menu).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Enter");   // on Copy XGID, the first item

        await Expect(Menu).ToHaveCountAsync(0);
        await ExpectToPassAsync(async () =>
            Assert.StartsWith("XGID=", await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()")));
        await ExpectToPassAsync(async () =>
            Assert.Equal("button " + ExpectedText.MoreButton, await FocusedAsync()));
    }

    [Fact]
    public async Task AClickElsewhere_ClosesTheList_AndFocusStaysWhereTheClickPutIt()
    {
        await StartOnTheMatchAsync();
        await NarrowBelowTheSwitchAsync();

        // A click on a pill: the list closes, and the click does what it does
        // there — the pill is chosen and holds focus.
        await More.ClickAsync();
        await Expect(Menu).ToBeVisibleAsync();
        await Page.Locator(".action-row .bg-cube-action")
            .Filter(new() { Has = CubePill(ExpectedText.NoDoublePill) }).ClickAsync();
        await Expect(Menu).ToHaveCountAsync(0);
        await Expect(CubePill(ExpectedText.NoDoublePill)).ToBeCheckedAsync();
        Assert.Equal("radio checked", await FocusedAsync());

        // A click on the board, which takes no focus: the list closes, and
        // focus is on nothing in particular, where the click left it.
        await More.ClickAsync();
        await Expect(Menu).ToBeVisibleAsync();
        await ClickTheBoardsTitleStripAsync();
        await Expect(Menu).ToHaveCountAsync(0);
        Assert.Equal("body", await FocusedAsync());
    }

    [Fact]
    public async Task SpaceAndEnter_UsedInTheList_NeitherSubmitNorMoveTheQuizOn()
    {
        // An answer chosen, so Submit is lit and the page's Space shortcut
        // would press it from the body. Every key used to open the list and to
        // choose in it must stay the list's.
        await StartOnTheMatchAsync();
        await NarrowBelowTheSwitchAsync();
        await CubePill(ExpectedText.NoDoublePill).CheckAsync();
        await Expect(SubmitButton).ToBeEnabledAsync();
        var xgid = await Page.Locator(".action-row-ruler .xgid-label-text").GetAttributeAsync("title");

        await More.FocusAsync();
        await Page.Keyboard.PressAsync("Space");                  // opens
        await Expect(Menu).ToBeVisibleAsync();
        await ExpectToPassAsync(async () =>
            Assert.Equal("button " + ExpectedText.CopyXgidButton, await FocusedAsync()));
        await Page.Keyboard.PressAsync("Space");                  // chooses Copy XGID
        await Expect(Menu).ToHaveCountAsync(0);
        await ExpectToPassAsync(async () =>
            Assert.Equal("button " + ExpectedText.MoreButton, await FocusedAsync()));
        await Page.Keyboard.PressAsync("Enter");                  // opens
        await Expect(Menu).ToBeVisibleAsync();
        await ExpectToPassAsync(async () =>
            Assert.Equal("button " + ExpectedText.CopyXgidButton, await FocusedAsync()));
        await Page.Keyboard.PressAsync("Enter");                  // chooses Copy XGID
        await Expect(Menu).ToHaveCountAsync(0);

        // Still answering the same problem, the answer still chosen.
        await Expect(ContinueButton).ToHaveCountAsync(0);
        await Expect(SubmitButton).ToBeEnabledAsync();
        await Expect(CubePill(ExpectedText.NoDoublePill)).ToBeCheckedAsync();
        Assert.Equal(xgid, await Page.Locator(".action-row-ruler .xgid-label-text").GetAttributeAsync("title"));
    }

    [Fact]
    public async Task Escape_ClosesTheActiveSurface_TheListFirst_ThenTheDrawer()
    {
        // The drawer open from the rail, and the list opened by keyboard with
        // it still open: Escape closes the list and leaves the drawer; the
        // next Escape closes the drawer.
        await StartOnTheMatchAsync();
        await NarrowBelowTheSwitchAsync();
        var rail = Page.GetByRole(AriaRole.Checkbox, new() { Name = ExpectedText.HideNavigationPanelCheckbox });
        await rail.ClickAsync();
        await Expect(rail).Not.ToBeCheckedAsync();
        await More.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Menu).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(Menu).ToHaveCountAsync(0);
        await Expect(rail).Not.ToBeCheckedAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(rail).ToBeCheckedAsync();
        Assert.Empty(await ActionRowGeometry.CoveredControlsAsync(Page));
    }
}
