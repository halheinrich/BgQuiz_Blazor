using System.Globalization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Moving the notes overlay by dragging its title bar, with a real mouse
/// (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's placement is a remembered
/// preference", "How it moves"; issue <c>halheinrich/backgammon#344</c>): the
/// title bar only, a commit on release wherever it happens, a release over
/// the backdrop that never closes the notes, and the three ways a drag ends
/// without writing — lost capture, Esc, and leaving the review.
/// </summary>
public sealed class NotesPlacementDragTests : NotesPlacementTestBase
{
    public NotesPlacementDragTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output)
    {
    }

    [Fact]
    public async Task ADragByTheTitleBar_FollowsThePointer_CommitsOnRelease_AndStopsAtTheEdge()
    {
        await StartAtTheCubeReviewAsync();
        var board = await LaidOutBoxAsync(Page.Locator(".board-container"), "the board region");

        // Unset, the notes open where they always have: centred.
        await OpenNotesAsync();
        await ExpectOverlayCentredAsync("an unset preference");
        Assert.Null(await StoredPlacementAsync());
        var start = await OverlayBoxAsync();

        // Mid-drag the overlay follows the pointer, and nothing is written yet:
        // the drag commits when the pointer is released.
        var (x, y) = await GrabPointAsync();
        await PressAndMoveAsync(x, y, -200, -90);
        await ExpectOverlayAtAsync(start.X - 200, start.Y - 90, "mid-drag, the overlay follows the pointer");
        Assert.Null(await StoredPlacementAsync());

        await Page.Mouse.UpAsync();
        await ExpectOverlayAtAsync(start.X - 200, start.Y - 90, "released, the overlay stays where it was dropped");
        Assert.NotNull(await StoredPlacementAsync());

        // Moving the overlay, like opening it, reflows nothing.
        var boardAfter = await LaidOutBoxAsync(Page.Locator(".board-container"), "the board region");
        Assert.Equal((board.X, board.Y, board.Width, board.Height), (boardAfter.X, boardAfter.Y, boardAfter.Width, boardAfter.Height));

        // Closed and opened again, the notes open where they were put.
        await CloseNotesButton.ClickAsync();
        await Expect(NotesDialog).ToHaveCountAsync(0);
        await OpenNotesAsync();
        await ExpectOverlayAtAsync(start.X - 200, start.Y - 90, "reopened, the placement holds");

        // A drag pushed past the window's edges stops where the travel ends:
        // the clearance from the left and the top.
        await DragTitleBarAsync(-900, -700);
        await ExpectOverlayAtAsync(Clearance, Clearance, "pushed past the top-left corner");

        // ...and from the right and the bottom.
        await DragTitleBarAsync(2000, 1500);
        var area = await AreaAsync();
        var box = await OverlayBoxAsync();
        await ExpectOverlayAtAsync(
            area.Width - Clearance - box.Width, area.Height - Clearance - box.Height, "pushed past the bottom-right corner");
    }

    [Fact]
    public async Task ADragStartedOnTheText_OrOnATitleBarButton_MovesNothing()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        await WriteOverlayEvidenceAsync("before opening Move");
        await OpenMoveControlAsync();
        var start = await OverlayBoxAsync();
        await WriteOverlayEvidenceAsync(
            "when the Move control is open and the baseline is captured",
            ("baseline captured (Playwright's box) [x, y, width, height]", Invariant(start)));

        // On the text: the drag selects text, as it always has, and the
        // overlay stays put — the text is not a handle. Pressed in the middle
        // of the first rendered line: a point on the boundary between two
        // line boxes is no line, and a press there selects nothing anywhere.
        double[]? line = null;
        try
        {
            line = await Page.EvaluateAsync<double[]>("""
                () => {
                  const range = document.createRange();
                  range.selectNodeContents(document.querySelector('.decision-notes-text'));
                  const first = [...range.getClientRects()].find(r => r.width > 0);
                  return [first.left, first.top + first.height / 2];
                }
                """);
            await PressAndMoveAsync(line[0] + 2, line[1], 150, 0);
            await Page.Mouse.UpAsync();
            Assert.True(
                await Page.EvaluateAsync<int>("() => window.getSelection().toString().length") > 0,
                "a drag across the text selects it");
            await AssertStillAtAsync(start, "after a drag that began on the text");
        }
        finally
        {
            await WriteOverlayEvidenceAsync(
                "after the text drag",
                ("the press [x, y]", line is null ? "(not reached)" : Invariant([line[0] + 2, line[1]])));
        }

        // On each of the title bar's own buttons: they stay buttons, and a press
        // that wanders off and is released elsewhere is no drag (and no click).
        foreach (var button in new[]
                 {
                     MoveButton, CloseNotesButton, StepButton(ExpectedText.MoveUpButton),
                     StepButton(ExpectedText.MoveRightButton), ResetButton,
                 })
        {
            var b = await LaidOutBoxAsync(button, "a title-bar button");
            await PressAndMoveAsync(b.X + b.Width / 2, b.Y + b.Height / 2, 150, 80);
            await Page.Mouse.UpAsync();
            await AssertStillAtAsync(start, "after a drag that began on a title-bar button");
            await Expect(NotesDialog).ToBeVisibleAsync();
        }

        Assert.Null(await StoredPlacementAsync());
    }

    [Fact]
    public async Task AReleaseOverTheBackdrop_EndsTheDrag_AndLeavesTheNotesOpen()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();

        // Drag left past the travel's end: the overlay stops at the clearance,
        // the pointer goes on into it, so the release lands on the backdrop.
        var (x, y) = await GrabPointAsync();
        const double releaseX = 5;
        await PressAndMoveAsync(x, y, releaseX - x, 0);
        await ExpectOverlayAtAsync(Clearance, (await OverlayBoxAsync()).Y, "dragged against the left edge");
        Assert.True(
            await Page.EvaluateAsync<bool>(
                "p => document.elementFromPoint(p.x, p.y)?.classList.contains('decision-notes-backdrop') === true",
                new { x = releaseX, y }),
            "the pointer is over the backdrop when it is released");

        await Page.Mouse.UpAsync();

        // The release ended the drag and committed it; it was never the click
        // outside that closes the notes.
        await Expect(NotesDialog).ToBeVisibleAsync();
        Assert.NotNull(await StoredPlacementAsync());
        await ExpectOverlayAtAsync(Clearance, (await OverlayBoxAsync()).Y, "after the release over the backdrop");

        // A click outside still closes them, outside a drag.
        await Page.Mouse.ClickAsync((float)releaseX, (float)y);
        await Expect(NotesDialog).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task LosingPointerCapture_CancelsTheDrag_RestoringWhereItStarted()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        var start = await OverlayBoxAsync();

        var (x, y) = await GrabPointAsync();
        await PressAndMoveAsync(x, y, -180, 70);
        await ExpectOverlayAtAsync(start.X - 180, start.Y + 70, "mid-drag");

        // The title bar holds the mouse's capture — taking it away is the lost
        // capture the ruling names, and proves the capture was held.
        var released = await Page.EvaluateAsync<int>("""
            () => {
              const bar = document.querySelector('.decision-notes-header');
              for (const id of [1, 0, 2]) {
                if (bar.hasPointerCapture(id)) { bar.releasePointerCapture(id); return id; }
              }
              return -1;
            }
            """);
        Assert.NotEqual(-1, released);

        // The browser delivers lostpointercapture with the pointer's next event
        // (Pointer Events: pending capture is processed before a pointer event
        // fires), so the pointer moves on, a little, as a held pointer does.
        await Page.Mouse.MoveAsync((float)(x - 175), (float)(y + 72));

        // Cancelled: back where the drag started, still open, nothing written —
        // and the release that follows commits nothing either.
        await ExpectOverlayAtAsync(start.X, start.Y, "after the capture was lost");
        await Expect(NotesDialog).ToBeVisibleAsync();
        await Page.Mouse.UpAsync();
        await AssertStillAtAsync(start, "after the release that followed");
        Assert.Null(await StoredPlacementAsync());
    }

    [Fact]
    public async Task EscapeDuringADrag_CancelsOnlyTheDrag_AndTheNextEscapeCloses()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        var start = await OverlayBoxAsync();

        var (x, y) = await GrabPointAsync();
        await PressAndMoveAsync(x, y, 160, -60);
        await ExpectOverlayAtAsync(start.X + 160, start.Y - 60, "mid-drag");

        await Page.Keyboard.PressAsync("Escape");
        await ExpectOverlayAtAsync(start.X, start.Y, "Esc during the drag puts the overlay back");
        await Expect(NotesDialog).ToBeVisibleAsync();

        await Page.Mouse.UpAsync();
        await AssertStillAtAsync(start, "after the release that followed");
        Assert.Null(await StoredPlacementAsync());

        // Outside a drag, Esc closes the notes, as it always has.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(NotesDialog).ToHaveCountAsync(0);
        await Expect(NotesButton).ToBeFocusedAsync();
    }

    [Fact]
    public async Task LeavingTheReviewMidDrag_PersistsNothing()
    {
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();

        var (x, y) = await GrabPointAsync();
        await PressAndMoveAsync(x, y, -150, -80);
        var moved = await OverlayBoxAsync();

        // The browser's Back, with the button still down: the quiz page goes,
        // and the overlay and its drag with it.
        await Page.EvaluateAsync("() => history.back()");
        await ExpectUrlAsync(AppRoute.Home);
        await Expect(PickFolderButton).ToBeVisibleAsync();
        await ReleaseOverNothingAsync();
        Assert.Null(await StoredPlacementAsync());

        // Back on the review, the notes open where they were before the drag:
        // centred, the preference still unset.
        await Page.EvaluateAsync("() => history.forward()");
        await ExpectUrlAsync(AppRoute.Quiz);
        await Expect(NotesButton).ToBeVisibleAsync();
        await OpenNotesAsync();
        await ExpectOverlayCentredAsync("the abandoned drag wrote nothing");
        var reopened = await OverlayBoxAsync();
        Assert.NotEqual((moved.X, moved.Y), (reopened.X, reopened.Y));
    }

    /// <summary>The overlay's corner has not moved from <paramref name="start"/>'s.</summary>
    private async Task AssertStillAtAsync(LocatorBoundingBoxResult start, string when)
    {
        var box = await OverlayBoxAsync();
        Assert.True(
            Math.Abs(box.X - start.X) <= Tolerance && Math.Abs(box.Y - start.Y) <= Tolerance,
            $"{when}, the overlay should still stand at ({start.X:0.#}, {start.Y:0.#}); it stands at ({box.X:0.#}, {box.Y:0.#}).");
    }

    /// <summary>
    /// Write what the browser holds about the overlay at one moment to the
    /// test's output, under <paramref name="when"/> — evidence for an
    /// intermittent failure of the text drag's position check whose cause is
    /// not yet known (halheinrich/backgammon#402), written whether the
    /// scenario passes or fails.
    ///
    /// <para>
    /// <b>One read, at one instant.</b> Everything the page is asked comes from
    /// a single script, so the lines describe one moment rather than several
    /// round trips apart: the overlay's box, its rendered placement
    /// (<c>data-placed</c>, the style's <c>--notes-left</c> and
    /// <c>--notes-top</c>, the computed corner), its title bar's height and
    /// whether the Move control's steps are shown; the backdrop's box; the
    /// page's scroll position, the viewport's size and the device pixel
    /// ratio; the selection's length; and the trace below. Items the test
    /// already holds — <paramref name="inHand"/> — are written with it.
    /// </para>
    ///
    /// <para>
    /// <b>The trace.</b> The first read starts a passive record on the
    /// overlay, before it reads: timestamped changes to its style,
    /// <c>data-placed</c> and the Move control's <c>aria-expanded</c>, the
    /// elements added to it and removed from it, its size as the browser
    /// reports it, and every pointer press, release and click on the page.
    /// Observers and passive listeners only — the app's own handlers see the
    /// same events at the same moments — so every later read can say what
    /// changed, and when, against its own timestamp.
    /// </para>
    ///
    /// <para>
    /// Best-effort, through <see cref="EvidenceSheet"/>: a read that fails or
    /// does not answer is written as not collected, and nothing here throws,
    /// so the evidence never decides or masks the scenario's outcome.
    /// </para>
    /// </summary>
    private async Task WriteOverlayEvidenceAsync(string when, params (string What, string Value)[] inHand)
    {
        var sheet = new EvidenceSheet();
        foreach (var (what, value) in inHand) sheet.Add(what, value);
        await sheet.ReadAsync("browser", () => Page.EvaluateAsync<string>("""
            () => {
              const at = () => Math.round(performance.now() * 10) / 10;
              const d = document.querySelector('dialog.decision-notes');
              const classOf = n => n?.getAttribute?.('class') ?? n?.tagName ?? null;

              if (d && !window.__notesTrace) {
                const trace = window.__notesTrace = [];
                new MutationObserver(records => {
                  for (const m of records) {
                    if (m.type === 'attributes') {
                      trace.push([at(), 'attribute', m.target === d ? 'dialog' : classOf(m.target),
                        m.attributeName, m.target.getAttribute(m.attributeName)]);
                    } else {
                      const named = nodes => [...nodes].filter(n => n.nodeType === 1).map(classOf);
                      const added = named(m.addedNodes), removed = named(m.removedNodes);
                      if (added.length || removed.length)
                        trace.push([at(), 'children', classOf(m.target), { added, removed }]);
                    }
                  }
                }).observe(d, {
                  attributes: true, attributeFilter: ['style', 'data-placed', 'aria-expanded'],
                  childList: true, subtree: true,
                });
                new ResizeObserver(entries => {
                  for (const e of entries)
                    trace.push([at(), 'size', 'dialog', e.borderBoxSize[0].inlineSize, e.borderBoxSize[0].blockSize]);
                }).observe(d);
                for (const type of ['pointerdown', 'pointerup', 'click'])
                  window.addEventListener(type, e => trace.push([at(), type, classOf(e.target), e.clientX, e.clientY]),
                    { capture: true, passive: true });
              }

              const lines = [];
              const add = (k, v) => lines.push(k + ': ' + JSON.stringify(v));
              add('t (performance.now)', at());
              if (d) {
                const r = d.getBoundingClientRect(), cs = getComputedStyle(d);
                const bar = d.querySelector('.decision-notes-header');
                const move = d.querySelector('.decision-notes-tools button[aria-expanded]');
                add('overlay box [x, y, width, height]', [r.x, r.y, r.width, r.height]);
                add('data-placed', d.hasAttribute('data-placed'));
                add('style', d.getAttribute('style'));
                add('--notes-left, --notes-top', [cs.getPropertyValue('--notes-left').trim(), cs.getPropertyValue('--notes-top').trim()]);
                add('computed left, top', [cs.left, cs.top]);
                add('title bar height', bar ? bar.getBoundingClientRect().height : null);
                add('Move aria-expanded', move ? move.getAttribute('aria-expanded') : null);
                add('step buttons shown', d.querySelector('.decision-notes-steps') !== null);
              } else {
                add('overlay', 'not in the DOM');
              }
              const b = document.querySelector('.decision-notes-backdrop')?.getBoundingClientRect();
              const se = document.scrollingElement, vv = window.visualViewport;
              add('backdrop box [x, y, width, height]', b ? [b.x, b.y, b.width, b.height] : null);
              add('window.scrollX, scrollY', [window.scrollX, window.scrollY]);
              add('scrolling element, scrollLeft, scrollTop', se ? [se.tagName, se.scrollLeft, se.scrollTop] : null);
              add('innerWidth, innerHeight', [window.innerWidth, window.innerHeight]);
              add('clientWidth, clientHeight', [document.documentElement.clientWidth, document.documentElement.clientHeight]);
              add('visualViewport [width, height, offsetTop, pageTop, scale]',
                vv ? [vv.width, vv.height, vv.offsetTop, vv.pageTop, vv.scale] : null);
              add('devicePixelRatio', window.devicePixelRatio);
              add('selection length', window.getSelection()?.toString().length ?? null);
              const trace = window.__notesTrace;
              if (trace) {
                lines.push('trace (' + trace.length + ' entries):');
                for (const e of trace) lines.push('  ' + JSON.stringify(e));
              } else {
                add('trace', 'not started');
              }
              return lines.join('\n');
            }
            """));

        try
        {
            Output.WriteLine($"[halheinrich/backgammon#402] {when}:");
            foreach (var line in sheet.Lines) Output.WriteLine("  " + line.ReplaceLineEndings(Environment.NewLine + "  "));
        }
        catch (InvalidOperationException)
        {
            // xUnit refuses output outside a running test; the scenario's own
            // outcome is what matters.
        }
    }

    /// <summary>A box's corner and size, in invariant culture.</summary>
    private static string Invariant(LocatorBoundingBoxResult box) => Invariant([box.X, box.Y, box.Width, box.Height]);

    /// <summary>A list of lengths, in invariant culture.</summary>
    private static string Invariant(IEnumerable<double> values) =>
        "[" + string.Join(", ", values.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))) + "]";

    /// <summary>
    /// Release the mouse over a spot where a click reaches no control, so the
    /// release a scenario owes the browser cannot act on the page it lands on.
    /// </summary>
    private async Task ReleaseOverNothingAsync()
    {
        var spot = await Page.EvaluateAsync<double[]?>("""
            () => {
              const w = document.documentElement.clientWidth, h = document.documentElement.clientHeight;
              for (const [x, y] of [[w - 4, h - 4], [w / 2, h - 4], [w - 4, h / 2], [w / 2, h / 2]]) {
                const at = document.elementFromPoint(x, y);
                if (at && !at.closest('a, button, input, label, select, summary, textarea, [role="button"]')) return [x, y];
              }
              return null;
            }
            """);
        Assert.NotNull(spot);
        await Page.Mouse.MoveAsync((float)spot![0], (float)spot[1]);
        await Page.Mouse.UpAsync();
    }
}
