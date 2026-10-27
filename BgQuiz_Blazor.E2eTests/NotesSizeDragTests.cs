using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Resizing the notes overlay by its corner handle, with a real mouse
/// (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's size is a remembered
/// preference", "How it is set" and "Bounds and the clamp"; issue
/// <c>halheinrich/backgammon#414</c>): the dragged corner follows the pointer,
/// the top-left corner stays where it is shown, the release writes the size
/// and the placement once, a drag past the room stops at the edge clearance,
/// the released overlay stands where the placement rule puts it, and nothing
/// on the page reflows. The state transitions — cancelling, one drag at a
/// time — are <c>DecisionNotesResizeTests</c>' in bUnit.
/// </summary>
public sealed class NotesSizeDragTests : NotesPlacementTestBase
{
    public NotesSizeDragTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output)
    {
    }

    [Fact]
    public async Task TheHandle_ResizesFromTheCorner_KeepingTheTopLeft_AndWritesOnceOnRelease()
    {
        await CountPlacementWritesAsync();
        await StartAtTheCubeReviewAsync();
        var board = await LaidOutBoxAsync(Page.Locator(".board-container"), "the board region");

        await OpenNotesAsync();
        await ExpectOverlayCentredAsync("unset preferences");
        var start = await OverlayBoxAsync();
        AssertNear(DefaultWidth, start.Width, "the default width");

        // The handle is a 24 x 24 target at the bottom-right corner.
        var handle = await LaidOutBoxAsync(ResizeHandle, "the resize handle");
        AssertNear(24, handle.Width, "the handle's width");
        AssertNear(24, handle.Height, "the handle's height");
        AssertNear(start.X + start.Width, handle.X + handle.Width, "the handle's right edge");
        AssertNear(start.Y + start.Height, handle.Y + handle.Height, "the handle's bottom edge");

        // Mid-drag the corner follows the pointer and the top-left stays;
        // nothing is written until the release, and nothing else moves.
        var (x, y) = await CornerGrabPointAsync();
        await PressAndMoveAsync(x, y, 120, 80);
        await ExpectBoxAsync(start.X, start.Y, start.Width + 120, start.Height + 80, "mid-drag");
        Assert.Equal(0, await PlacementWritesAsync());
        await AssertBoardUnmovedAsync(board, "mid-resize");

        await Page.Mouse.UpAsync();
        await ExpectBoxAsync(start.X, start.Y, start.Width + 120, start.Height + 80, "released");
        Assert.Equal(1, await PlacementWritesAsync());
        await AssertBoardUnmovedAsync(board, "after the resize");

        // What was written is the size and the placement together: the size
        // dragged to, and the position within the new travel that keeps the
        // corner where it was shown.
        var area = await AreaAsync();
        var stored = await StoredPreferencesAsync();
        Assert.NotNull(stored);
        AssertNear(start.Width + 120, stored!.Width!.Value, "the stored width");
        AssertNear(start.Height + 80, stored.Height!.Value, "the stored height");
        AssertNear(start.X, ExpectedOffset(stored.Horizontal!.Value, area.Width, stored.Width.Value), "where the stored placement puts the left edge");
        AssertNear(start.Y, ExpectedOffset(stored.Vertical!.Value, area.Height, stored.Height.Value), "where the stored placement puts the top edge");

        // Closed and opened again: it stands where the rule puts that placement.
        await CloseNotesButton.ClickAsync();
        await OpenNotesAsync();
        await ExpectAtPositionAsync(stored.Horizontal.Value, stored.Vertical.Value, "reopened");
        await ExpectBoxAsync(start.X, start.Y, start.Width + 120, start.Height + 80, "reopened");
    }

    [Fact]
    public async Task ADragPastTheRoom_StopsAtTheEdgeClearance_AndAnotherAtTheRoomWritesNothing()
    {
        await CountPlacementWritesAsync();
        await StartAtTheCubeReviewAsync();
        await OpenNotesAsync();
        var start = await OverlayBoxAsync();
        var area = await AreaAsync();

        // Far past the window's corner: the room from the top-left corner to
        // the clearance on the right and at the bottom is as large as it gets.
        await DragCornerAsync(2000, 2000);
        await ExpectBoxAsync(
            start.X, start.Y, area.Width - Clearance - start.X, area.Height - Clearance - start.Y,
            "dragged past the bottom-right corner");
        Assert.Equal(1, await PlacementWritesAsync());

        // At the room, a further drag outwards cannot grow it, and writes nothing.
        await DragCornerAsync(200, 200);
        Assert.Equal(1, await PlacementWritesAsync());

        // Inwards, far past the minimum: the minimum width, the corner still there.
        await DragCornerAsync(-2000, 0);
        await ExpectToPassAsync(async () =>
        {
            var box = await OverlayBoxAsync();
            AssertNear(MinimumWidth, box.Width, "the width");
            AssertNear(start.X, box.X, "the left edge");
        });
        Assert.Equal(2, await PlacementWritesAsync());
        await Expect(NotesDialog).ToBeVisibleAsync();
    }

    private async Task AssertBoardUnmovedAsync(Microsoft.Playwright.LocatorBoundingBoxResult board, string when)
    {
        var now = await LaidOutBoxAsync(Page.Locator(".board-container"), "the board region");
        Assert.True(
            (board.X, board.Y, board.Width, board.Height) == (now.X, now.Y, now.Width, now.Height),
            $"{when}, the board should not have moved or resized: it was {board.Width} x {board.Height} at ({board.X}, {board.Y}), "
            + $"it is {now.Width} x {now.Height} at ({now.X}, {now.Y}).");
    }
}
