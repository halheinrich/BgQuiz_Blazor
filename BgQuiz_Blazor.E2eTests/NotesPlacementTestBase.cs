using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Shared plumbing for the notes overlay's placement scenarios
/// (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's placement is a remembered
/// preference", ruled 2026-10-05; issue <c>halheinrich/backgammon#344</c>):
/// reaching a review whose decision has notes, opening them, reading where the
/// overlay is, and moving it the ways a reader can.
///
/// <para>
/// <b>What these scenarios prove, and what they leave to the unit suite.</b>
/// The placement arithmetic — travel, centring, the clamp, drag and step — is
/// <c>NotesStage</c>'s, pinned in <c>NotesStageTests</c>; the drag's state
/// transitions are <c>DecisionNotesPlacementTests</c>' in bUnit. What only a
/// browser can show is here: real pointers, touch and keys reaching the
/// title bar and the buttons, capture holding a drag over the backdrop, the
/// overlay really standing where the rule puts it in a real window, and the
/// preference really surviving in browser storage.
/// </para>
///
/// <para>
/// <b>Expected positions are computed here from the ruling, not read from
/// the app.</b> The stage a scenario measures is Playwright's own reading of
/// the page (the backdrop's box is the visible area, the overlay's box its
/// size), and the clearance is the stylesheet's stated <c>1rem</c> at the
/// browser's default 16 px. A scenario that says "the overlay is a quarter of
/// the way along its travel" therefore checks the rule against the screen,
/// independently of the code that applied it.
/// </para>
/// </summary>
public abstract class NotesPlacementTestBase : E2eTestBase
{
    protected NotesPlacementTestBase(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright)
    {
    }

    /// <summary>
    /// The localStorage key the preference is kept under — a literal, per this
    /// suite's posture: it is the name a reader finds in devtools, and Help
    /// names it.
    /// </summary>
    protected const string PlacementKey = "xg_notesPlacement";

    /// <summary>
    /// The edge clearance the overlay keeps from each side of the visible area:
    /// the stylesheet's <c>--notes-edge: 1rem</c>, at the browser's default
    /// root font size.
    /// </summary>
    protected const double Clearance = 16;

    /// <summary>A desktop window with travel on both axes for a short note.</summary>
    protected const int DesktopWidth = 1280;

    /// <inheritdoc cref="DesktopWidth"/>
    protected const int DesktopHeight = 800;

    /// <summary>How close two lengths must be to count as one position: sub-pixel rounding, and no more.</summary>
    protected const double Tolerance = 1;

    protected ILocator NotesButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.NotesButton, Exact = true });

    protected ILocator NotesDialog => Page.GetByRole(AriaRole.Dialog, new() { Name = ExpectedText.NotesButton });

    /// <summary>The overlay's title bar — its drag handle.</summary>
    protected ILocator TitleBar => NotesDialog.Locator(".decision-notes-header");

    /// <summary>The title bar's heading: the spot on the handle a drag grabs, which is no button.</summary>
    protected ILocator NotesHeading => NotesDialog.GetByRole(AriaRole.Heading, new() { Name = ExpectedText.NotesButton });

    protected ILocator NotesText => NotesDialog.Locator(".decision-notes-text");

    protected ILocator CloseNotesButton =>
        NotesDialog.GetByRole(AriaRole.Button, new() { Name = ExpectedText.CloseNotesButton, Exact = true });

    protected ILocator MoveButton =>
        NotesDialog.GetByRole(AriaRole.Button, new() { Name = ExpectedText.MoveNotesButton, Exact = true });

    protected ILocator StepButton(string name) =>
        NotesDialog.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    protected ILocator ResetButton =>
        NotesDialog.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ResetNotesButton, Exact = true });

    protected ILocator ContinueButton => Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ContinueButton });

    /// <summary>The backdrop: fixed at inset 0, its box is the visible area the overlay is placed in.</summary>
    protected ILocator Backdrop => Page.Locator(".decision-notes-backdrop");

    /// <summary>
    /// Start the synthesized match — both of whose problems carry notes — at a
    /// window of <paramref name="width"/> × <paramref name="height"/>, and
    /// answer its first problem, the cube, landing on its review.
    /// </summary>
    protected async Task StartAtTheCubeReviewAsync(
        int width = DesktopWidth, int height = DesktopHeight,
        string stagedFileName = SyntheticXgMatch.StagedFileName, byte[]? bytes = null)
    {
        await Page.SetViewportSizeAsync(width, height);
        await BootHomeAsync();
        await PickSynthesizedFileAsync(stagedFileName, bytes ?? SyntheticXgMatch.Bytes());
        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeAsync(ExpectedText.DoubleTakePill);
        await Expect(NotesButton).ToBeVisibleAsync();
    }

    /// <summary>
    /// Open the notes and wait until the overlay is <b>placed</b> — its stage
    /// measured and its position drawn from the preference — which is when a
    /// reading of where it stands means anything. The overlay shows centred
    /// before that; a chosen placement is drawn only once measured.
    /// </summary>
    protected async Task OpenNotesAsync()
    {
        await NotesButton.ClickAsync();
        await ExpectPlacedAsync();
    }

    /// <summary>The overlay is open and placed (see <see cref="OpenNotesAsync"/>).</summary>
    protected async Task ExpectPlacedAsync()
    {
        await Expect(NotesDialog).ToBeVisibleAsync();
        await Expect(NotesDialog).ToHaveAttributeAsync("data-placed", string.Empty);
    }

    /// <summary>Open the Move control's step buttons.</summary>
    protected async Task OpenMoveControlAsync()
    {
        await MoveButton.ClickAsync();
        await Expect(MoveButton).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(StepButton(ExpectedText.MoveUpButton)).ToBeVisibleAsync();
    }

    /// <summary>What the browser holds under <see cref="PlacementKey"/>; null when nothing is stored.</summary>
    protected Task<string?> StoredPlacementAsync() =>
        Page.EvaluateAsync<string?>("key => localStorage.getItem(key)", PlacementKey);

    /// <summary>The overlay's box as laid out now, refusing a degenerate one.</summary>
    protected Task<LocatorBoundingBoxResult> OverlayBoxAsync() => LaidOutBoxAsync(NotesDialog, "the notes overlay");

    /// <summary>The visible area, as the backdrop covers it.</summary>
    protected Task<LocatorBoundingBoxResult> AreaAsync() => LaidOutBoxAsync(Backdrop, "the backdrop");

    /// <summary>
    /// Where the ruling puts an overlay of <paramref name="size"/> at position
    /// <paramref name="position"/> (0 to 1) on an axis of
    /// <paramref name="area"/>: <c>clearance + position × travel</c>, with the
    /// travel the area less the clearance on each side, less the overlay.
    /// </summary>
    protected static double ExpectedOffset(double position, double area, double size) =>
        Clearance + position * (area - 2 * Clearance - size);

    /// <summary>The position (0 to 1) the shown offset stands for on an axis — the inverse of <see cref="ExpectedOffset"/>.</summary>
    protected static double PositionOf(double offset, double area, double size) =>
        (offset - Clearance) / (area - 2 * Clearance - size);

    /// <summary>
    /// Wait until the overlay's top-left corner stands at
    /// (<paramref name="left"/>, <paramref name="top"/>), within
    /// <see cref="Tolerance"/>.
    /// </summary>
    protected async Task ExpectOverlayAtAsync(double left, double top, string because)
    {
        await ExpectToPassAsync(async () =>
        {
            var box = await OverlayBoxAsync();
            Assert.True(
                Math.Abs(box.X - left) <= Tolerance && Math.Abs(box.Y - top) <= Tolerance,
                $"{because}: the overlay should stand at ({left:0.#}, {top:0.#}); it stands at ({box.X:0.#}, {box.Y:0.#}).");
        });
    }

    /// <summary>
    /// Wait until the overlay stands at <paramref name="horizontal"/> and
    /// <paramref name="vertical"/> (each 0 to 1) within its travel on each
    /// axis, as the ruling computes it from the area and the overlay's size
    /// measured at that moment.
    /// </summary>
    protected async Task ExpectAtPositionAsync(double horizontal, double vertical, string because)
    {
        await ExpectToPassAsync(async () =>
        {
            var area = await AreaAsync();
            var box = await OverlayBoxAsync();
            double left = ExpectedOffset(horizontal, area.Width, box.Width);
            double top = ExpectedOffset(vertical, area.Height, box.Height);
            Assert.True(
                Math.Abs(box.X - left) <= Tolerance && Math.Abs(box.Y - top) <= Tolerance,
                $"{because}: at ({horizontal}, {vertical}) of its travel the overlay should stand at ({left:0.#}, {top:0.#}); "
                + $"it stands at ({box.X:0.#}, {box.Y:0.#}), {box.Width:0.#} x {box.Height:0.#} in {area.Width:0.#} x {area.Height:0.#}.");
        });
    }

    /// <summary>Wait until the overlay stands centred in the visible area — where an unset preference shows it.</summary>
    protected async Task ExpectOverlayCentredAsync(string because)
    {
        await ExpectToPassAsync(async () =>
        {
            var area = await AreaAsync();
            var box = await OverlayBoxAsync();
            double left = (area.Width - box.Width) / 2;
            double top = (area.Height - box.Height) / 2;
            Assert.True(
                Math.Abs(box.X - left) <= Tolerance && Math.Abs(box.Y - top) <= Tolerance,
                $"{because}: the overlay should be centred at ({left:0.#}, {top:0.#}); it stands at ({box.X:0.#}, {box.Y:0.#}).");
        });
    }

    /// <summary>The point a drag grabs the title bar by: its heading's centre, a spot on the handle that is no button.</summary>
    protected async Task<(double X, double Y)> GrabPointAsync()
    {
        var heading = await LaidOutBoxAsync(NotesHeading, "the notes' heading");
        return ((double)(heading.X + heading.Width / 2), (double)(heading.Y + heading.Height / 2));
    }

    /// <summary>
    /// Press the mouse at (<paramref name="x"/>, <paramref name="y"/>) and move
    /// it by (<paramref name="dx"/>, <paramref name="dy"/>) in steps, leaving
    /// the button down.
    /// </summary>
    protected async Task PressAndMoveAsync(double x, double y, double dx, double dy)
    {
        await Page.Mouse.MoveAsync((float)x, (float)y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)(x + dx), (float)(y + dy), new() { Steps = 8 });
    }

    /// <summary>Drag the overlay by its title bar by (<paramref name="dx"/>, <paramref name="dy"/>) and release.</summary>
    protected async Task DragTitleBarAsync(double dx, double dy)
    {
        var (x, y) = await GrabPointAsync();
        await PressAndMoveAsync(x, y, dx, dy);
        await Page.Mouse.UpAsync();
    }

    /// <summary>
    /// Count every write the page makes to the placement's key — a set or a
    /// removal — from the first navigation on, so a scenario can tell a press
    /// that wrote from one that did not.
    /// </summary>
    protected Task CountPlacementWritesAsync() => Page.AddInitScriptAsync($$"""
        (() => {
          window.__placementWrites = 0;
          const set = Storage.prototype.setItem, remove = Storage.prototype.removeItem;
          Storage.prototype.setItem = function (key, value) {
            if (key === '{{PlacementKey}}') window.__placementWrites++;
            return set.call(this, key, value);
          };
          Storage.prototype.removeItem = function (key) {
            if (key === '{{PlacementKey}}') window.__placementWrites++;
            return remove.call(this, key);
          };
        })();
        """);

    /// <summary>How many writes <see cref="CountPlacementWritesAsync"/> has counted so far.</summary>
    protected Task<int> PlacementWritesAsync() => Page.EvaluateAsync<int>("() => window.__placementWrites");
}
