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
    public NotesPlacementDragTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright)
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
        await OpenMoveControlAsync();
        var start = await OverlayBoxAsync();

        // On the text: the drag selects text, as it always has, and the
        // overlay stays put — the text is not a handle. Pressed in the middle
        // of the first rendered line: a point on the boundary between two
        // line boxes is no line, and a press there selects nothing anywhere.
        var line = await Page.EvaluateAsync<double[]>("""
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
        await ExpectUrlAsync("/");
        await Expect(PickFolderButton).ToBeVisibleAsync();
        await ReleaseOverNothingAsync();
        Assert.Null(await StoredPlacementAsync());

        // Back on the review, the notes open where they were before the drag:
        // centred, the preference still unset.
        await Page.EvaluateAsync("() => history.forward()");
        await ExpectUrlAsync("/quiz");
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
