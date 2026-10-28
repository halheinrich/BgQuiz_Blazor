using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="NotesStage"/>, the placement and size arithmetic of
/// <c>SPEC-quiz-view.md</c> §4, "The notes overlay's placement is a remembered
/// preference" (issue <c>halheinrich/backgammon#344</c>) and "The notes
/// overlay's size is a remembered preference" (issue
/// <c>halheinrich/backgammon#414</c>): where a placement is shown — 0, 1 and
/// between within the travel, centred where it is unset or where an axis has
/// no travel, and clamped so the title bar stays in view — and what a drag and
/// a step make of it, each axis on its own; how a size is shown through the
/// show bounds; and what a resize makes of both, with the top-left corner kept.
///
/// <para>
/// The expected numbers are worked out here from the ruling's words, not read
/// back from the type: the travel is the area less the clearance on each side,
/// less the overlay as shown, and a position <c>p</c> stands <c>clearance + p
/// × travel</c> from the start edge. The desktop stage below has a space of
/// 1280 − 32 = 1248 across and 800 − 32 = 768 down, so a travel of
/// 1248 − 576 = 672 across and 768 − 200 = 568 down for the measured overlay.
/// Its minimum height is the title bar's 56 px plus three 24 px lines, 128.
/// </para>
/// </summary>
public class NotesStageTests
{
    private const double Clearance = 16;

    private const double LineHeight = 24;

    /// <summary>A desktop window and a short note: travel on both axes.</summary>
    private static readonly NotesStage Desktop = Stage(1280, 800, 576, 200, titleBarBottom: 56);

    private const double SpaceAcross = 1280 - 2 * Clearance;

    private const double SpaceDown = 800 - 2 * Clearance;

    private const double TravelAcross = SpaceAcross - 576;

    private const double TravelDown = SpaceDown - 200;

    /// <summary>The desktop stage's minimum height: the title bar's bottom and three lines of text.</summary>
    private const double MinimumHeight = 56 + 3 * LineHeight;

    /// <summary>A phone: the overlay fills the width less the clearance, so there is no horizontal travel.</summary>
    private static readonly NotesStage Phone = Stage(375, 812, 375 - 2 * Clearance, 220, titleBarBottom: 56);

    private static NotesStage Stage(
        double areaWidth, double areaHeight, double overlayWidth, double overlayHeight, double titleBarBottom,
        double clearance = Clearance, double lineHeight = LineHeight)
    {
        Assert.True(NotesStage.TryCreate(
            areaWidth, areaHeight, clearance, overlayWidth, overlayHeight, titleBarBottom, lineHeight, out var stage));
        return stage;
    }

    private static NotesPlacement At(double? horizontal, double? vertical) => NotesPlacement.Create(horizontal, vertical);

    /// <summary>Preferences placed at (<paramref name="horizontal"/>, <paramref name="vertical"/>), sized <paramref name="width"/> × <paramref name="height"/>.</summary>
    private static NotesPreferences P(double? horizontal, double? vertical, double? width = null, double? height = null) =>
        new(At(horizontal, vertical), NotesSize.Create(width, height));

    /// <summary>A box at (<paramref name="left"/>, <paramref name="top"/>) rendering no length.</summary>
    private static NotesBox At(double left, double top) => new(left, top, null, null);

    // -----------------------------------------------------------------------
    //  Measurements
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(double.NaN, 800, 16, 576, 200, 56, 24)]
    [InlineData(1280, double.PositiveInfinity, 16, 576, 200, 56, 24)]
    [InlineData(0, 800, 16, 576, 200, 56, 24)]
    [InlineData(1280, 800, -1, 576, 200, 56, 24)]
    [InlineData(1280, 800, 16, 0, 200, 56, 24)]
    [InlineData(1280, 800, 16, 576, -3, 56, 24)]
    [InlineData(1280, 800, 16, 576, 200, double.NaN, 24)]
    [InlineData(1280, 800, 16, 576, 200, 56, double.NaN)]   // a line height of "normal", unparsed
    [InlineData(1280, 800, 16, 576, 200, 56, -1)]
    public void TryCreate_RefusesMeasurementsThatAreNoStage(
        double areaWidth, double areaHeight, double clearance, double overlayWidth, double overlayHeight,
        double titleBarBottom, double lineHeight)
    {
        Assert.False(NotesStage.TryCreate(
            areaWidth, areaHeight, clearance, overlayWidth, overlayHeight, titleBarBottom, lineHeight, out var stage));
        Assert.Equal(default, stage);
    }

    [Fact]
    public void TryCreate_AcceptsNoClearanceAndNoTitleBar()
    {
        Assert.True(NotesStage.TryCreate(1280, 800, 0, 576, 200, 0, 0, out var stage));
        Assert.Equal(0, stage.Clearance);
        Assert.Equal(0, stage.TitleBarBottom);
    }

    // -----------------------------------------------------------------------
    //  Show: the placement
    // -----------------------------------------------------------------------

    [Fact]
    public void Show_Unset_IsCentred_RenderingNoLength()
    {
        Assert.Equal(At((1280 - 576) / 2.0, (800 - 200) / 2.0), Desktop.Show(NotesPreferences.Unset));
    }

    [Fact]
    public void Show_ZeroAndOne_AreTheTravelsEnds_ClearanceFromEachEdge()
    {
        Assert.Equal(At(Clearance, Clearance), Desktop.Show(P(0, 0)));
        Assert.Equal(At(1280 - Clearance - 576, 800 - Clearance - 200), Desktop.Show(P(1, 1)));
    }

    [Fact]
    public void Show_BetweenTheEnds_IsProportional_EachAxisOnItsOwn()
    {
        Assert.Equal(
            At(Clearance + 0.25 * TravelAcross, Clearance + 0.75 * TravelDown),
            Desktop.Show(P(0.25, 0.75)));

        // One axis unset is that axis centred, whatever the other holds.
        Assert.Equal(
            At(Clearance + 0.25 * TravelAcross, (800 - 200) / 2.0),
            Desktop.Show(P(0.25, null)));
    }

    [Fact]
    public void Show_OnePlacement_LandsProportionally_ForANoteOfAnyLength()
    {
        // A long note is taller, so its vertical travel is shorter; the same
        // position lands the same way along it.
        var longNote = Stage(1280, 800, 576, 512, titleBarBottom: 56);
        var preferences = P(0.5, 0.875);

        Assert.Equal(Clearance + 0.875 * TravelDown, Desktop.Show(preferences).Top);
        Assert.Equal(Clearance + 0.875 * (800 - 2 * Clearance - 512), longNote.Show(preferences).Top);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(null)]
    public void Show_AnAxisWithNoTravel_IsCentred_WhateverItHolds(double? held)
    {
        var shown = Phone.Show(P(held, 0.25));

        Assert.Equal(Clearance, shown.Left);
        Assert.Equal(Clearance + 0.25 * (812 - 2 * Clearance - 220), shown.Top);
    }

    [Fact]
    public void Show_TravelUnderHalfAPixel_IsNoTravel()
    {
        var almostFull = Stage(1000, 800, 1000 - 2 * Clearance - 0.4, 200, titleBarBottom: 56);
        var justMovable = Stage(1000, 800, 1000 - 2 * Clearance - NotesStage.MinimumTravel, 200, titleBarBottom: 56);

        Assert.Equal((1000 - almostFull.OverlayWidth) / 2, almostFull.Show(P(1, null)).Left);
        Assert.Equal(Clearance + NotesStage.MinimumTravel, justMovable.Show(P(1, null)).Left);
    }

    [Fact]
    public void Show_ClampsAtTheEndEdge_SoTheTitleBarStaysInView()
    {
        // A title bar that reaches further down than the overlay is tall — a
        // very short overlay — would leave the window at the travel's far end;
        // the clamp stops it where the title bar's bottom meets the window's.
        var shortOverlay = Stage(1280, 300, 576, 100, titleBarBottom: 280);

        Assert.Equal(300 - 280, shortOverlay.Show(P(null, 1)).Top);
        Assert.Equal(Clearance, shortOverlay.Show(P(null, 0)).Top);
    }

    [Fact]
    public void Show_ClampsAtTheStartEdge_WhereTheOverlayCannotFit()
    {
        // Centring an overlay larger than the area would put its start edge
        // outside; where it cannot fit at all, the start edge — the left, the
        // top — wins.
        var tooWide = Stage(300, 800, 310, 200, titleBarBottom: 56);
        var tooTall = Stage(1280, 90, 576, 100, titleBarBottom: 95);

        Assert.Equal(0, tooWide.Show(NotesPreferences.Unset).Left);
        Assert.Equal(0, tooTall.Show(NotesPreferences.Unset).Top);
    }

    [Fact]
    public void Show_TheClampChangesWhatIsShown_NeverWhatIsHeld()
    {
        // A very small window with no vertical travel left: the overlay is
        // centred, then pulled up so the title bar fits. Grown again, the same
        // placement shows where the user put it.
        var tiny = Stage(260, 100, 228, 68, titleBarBottom: 96);
        var preferences = P(1, 1);

        Assert.Equal(At(Clearance, 100 - 96), tiny.Show(preferences));
        Assert.Equal(At(1280 - Clearance - 576, 800 - Clearance - 200), Desktop.Show(preferences));
    }

    // -----------------------------------------------------------------------
    //  Show: the size
    // -----------------------------------------------------------------------

    [Fact]
    public void Show_ASetAxis_RendersTheHeldLength_AnUnsetOneRendersNothing_AndShowsTheMeasuredOne()
    {
        // A held width of 400 is rendered, and centres in a travel of
        // 1248 − 400; the unset height renders nothing and centres the
        // measured 200 as before.
        Assert.Equal(new NotesBox((1280 - 400) / 2.0, (800 - 200) / 2.0, 400, null), Desktop.Show(P(null, null, 400)));

        // And the other way about.
        Assert.Equal(new NotesBox((1280 - 576) / 2.0, (800 - 300) / 2.0, null, 300), Desktop.Show(P(null, null, null, 300)));
    }

    [Fact]
    public void Show_ThePositionFollowsThePlacementRule_WithTheShownSize()
    {
        // 16 + ¼ × (1248 − 400) across, 16 + ¾ × (768 − 300) down.
        Assert.Equal(
            new NotesBox(16 + 0.25 * 848, 16 + 0.75 * 468, 400, 300),
            Desktop.Show(P(0.25, 0.75, 400, 300)));
    }

    [Fact]
    public void Show_TheRenderedBox_NeverReadsTheMeasuredOverlay_OnASetAxis()
    {
        // Two reports of one window, the overlay measured at two sizes (as
        // between a render and the report of it): a set size shows the same
        // box on both, so the report cannot feed back into what it reports.
        var measuredSmall = Stage(1280, 800, 300, 150, titleBarBottom: 56);
        var measuredLarge = Stage(1280, 800, 900, 700, titleBarBottom: 56);
        var preferences = P(0.25, 0.75, 400, 300);

        Assert.Equal(measuredSmall.Show(preferences), measuredLarge.Show(preferences));
        Assert.Equal(Desktop.Show(preferences), measuredLarge.Show(preferences));
    }

    [Fact]
    public void Show_ClampsAHeldLengthToTheShowBounds_AndTheHeldValueIsUntouched()
    {
        // Above the space it shows the space, which leaves no travel: placed
        // at the clearance, centred.
        var tooLarge = P(1, 1, 5000, 5000);
        Assert.Equal(new NotesBox(Clearance, Clearance, SpaceAcross, SpaceDown), Desktop.Show(tooLarge));
        Assert.Equal(NotesSize.Create(5000, 5000), tooLarge.Size);

        // Below the minimum it shows the minimum.
        var tooSmall = P(0, 0, 10, 10);
        Assert.Equal(new NotesBox(Clearance, Clearance, NotesStage.MinimumWidth, MinimumHeight), Desktop.Show(tooSmall));
        Assert.Equal(NotesSize.Create(10, 10), tooSmall.Size);
    }

    [Fact]
    public void TheMinimumWidth_IsTheStatedConstant() => Assert.Equal(200, NotesStage.MinimumWidth);

    [Fact]
    public void TheMinimumHeight_IsTheTitleBarAndThreeLinesOfText()
    {
        Assert.Equal(3, NotesStage.MinimumTextLines);
        Assert.Equal(56 + 3 * 24, Desktop.MinimumHeight);
        Assert.Equal(80 + 3 * 30, Stage(1280, 800, 576, 200, titleBarBottom: 80, lineHeight: 30).MinimumHeight);
    }

    // -----------------------------------------------------------------------
    //  Drag
    // -----------------------------------------------------------------------

    [Fact]
    public void Drag_MovesEachAxisByTheDragOverItsTravel()
    {
        Assert.Equal(P(0.625, 0.375), Desktop.Drag(NotesPreferences.Unset, TravelAcross / 8, -TravelDown / 8));
        Assert.Equal(P(0.5, 0.5), Desktop.Drag(P(0.25, 0.75), TravelAcross / 4, -TravelDown / 4));
    }

    [Fact]
    public void Drag_StopsAtEachEndOfTheTravel()
    {
        Assert.Equal(P(0, 0), Desktop.Drag(P(0.5, 0.5), -5000, -5000));
        Assert.Equal(P(1, 1), Desktop.Drag(P(0.5, 0.5), 5000, 5000));
    }

    [Fact]
    public void Drag_ChangesOnlyTheAxesItMoved()
    {
        // Straight down from unset: the horizontal stays unset, not 0.5.
        var down = Desktop.Drag(NotesPreferences.Unset, 0, TravelDown / 8);
        Assert.Null(down.Placement.Horizontal);
        Assert.Equal(0.625, down.Placement.Vertical);

        // Straight across: the vertical keeps what it held.
        Assert.Equal(P(0.75, 0.3), Desktop.Drag(P(0.5, 0.3), TravelAcross / 4, 0));
    }

    [Fact]
    public void Drag_ThatEndsWhereItBegan_ChangesNothing()
    {
        Assert.Equal(NotesPreferences.Unset, Desktop.Drag(NotesPreferences.Unset, 0, 0));

        // Pushed further against an edge it already stands at.
        Assert.Equal(P(1, null), Desktop.Drag(P(1, null), 300, 0));
    }

    [Fact]
    public void Drag_OnAnAxisWithNoTravel_KeepsWhatItHolds_AndMovesTheOther()
    {
        var moved = Phone.Drag(P(0.875, 0.5), 120, (812 - 2 * Clearance - 220) / 8);

        Assert.Equal(0.875, moved.Placement.Horizontal);
        Assert.Equal(0.625, moved.Placement.Vertical);
        Assert.Equal(NotesPreferences.Unset, Phone.Drag(NotesPreferences.Unset, 120, 0));
    }

    [Fact]
    public void Drag_StartsFromWhereTheClampShowsTheOverlay()
    {
        // Held at the bottom, shown clamped 20 px down a travel of 168; a drag
        // of 10 px down moves it from there, not from the far end it holds.
        var shortOverlay = Stage(1280, 300, 576, 100, titleBarBottom: 280);
        const double travel = 300 - 2 * Clearance - 100;

        var moved = shortOverlay.Drag(P(null, 1), 0, 10);

        Assert.Equal((300 - 280 - Clearance + 10) / travel, moved.Placement.Vertical!.Value, 12);
    }

    [Fact]
    public void Drag_TravelsWithinTheShownSize_AndKeepsTheSize()
    {
        // A held width of 400 leaves a travel of 848, not the measured
        // overlay's 672: an eighth of it is 106.
        Assert.Equal(P(0.625, null, 400), Desktop.Drag(P(0.5, null, 400), 106, 0));
    }

    // -----------------------------------------------------------------------
    //  Step
    // -----------------------------------------------------------------------

    [Fact]
    public void TheStep_IsAnEighthOfTheTravel() => Assert.Equal(1.0 / 8, NotesStage.StepFraction);

    [Theory]
    [InlineData(nameof(NotesStep.Left), 0.375, null)]
    [InlineData(nameof(NotesStep.Right), 0.625, null)]
    [InlineData(nameof(NotesStep.Up), null, 0.375)]
    [InlineData(nameof(NotesStep.Down), null, 0.625)]
    public void Step_FromUnset_MovesOneStepOnItsOwnAxisOnly(string step, double? horizontal, double? vertical)
    {
        Assert.Equal(P(horizontal, vertical), Desktop.Step(NotesPreferences.Unset, Enum.Parse<NotesStep>(step)));
    }

    [Fact]
    public void Step_FourFromTheCentre_ReachAnEdge_AndTheNextCannotMove()
    {
        var preferences = NotesPreferences.Unset;
        for (int i = 0; i < 4; i++) preferences = Desktop.Step(preferences, NotesStep.Up);

        Assert.Equal(P(null, 0), preferences);
        Assert.Equal(preferences, Desktop.Step(preferences, NotesStep.Up));
    }

    [Fact]
    public void Step_ShortOfAWholeStepFromTheEdge_StopsAtTheEdge()
    {
        Assert.Equal(P(1, null), Desktop.Step(P(0.95, null), NotesStep.Right));
        Assert.Equal(P(0, 0.3), Desktop.Step(P(0.05, 0.3), NotesStep.Left));
    }

    [Theory]
    [InlineData(nameof(NotesStep.Left))]
    [InlineData(nameof(NotesStep.Right))]
    public void Step_AcrossAnAxisWithNoTravel_CannotMove_AndUnsetStaysUnset(string step)
    {
        var across = Enum.Parse<NotesStep>(step);
        Assert.Equal(NotesPreferences.Unset, Phone.Step(NotesPreferences.Unset, across));
        Assert.Equal(P(0.875, 0.5), Phone.Step(P(0.875, 0.5), across));
    }

    [Fact]
    public void Step_AlongTheOtherAxis_KeepsWhatTheAxisWithNoTravelHolds()
    {
        Assert.Equal(P(0.875, 0.375), Phone.Step(P(0.875, 0.5), NotesStep.Up));
    }

    [Fact]
    public void Step_AcrossAShownSizeThatFillsTheAxis_CannotMove()
    {
        // A held width larger than the window shows the space, leaving no travel.
        Assert.Equal(P(0.25, null, 5000), Desktop.Step(P(0.25, null, 5000), NotesStep.Right));
    }

    [Fact]
    public void Step_RefusesAValueThatIsNoStep() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Desktop.Step(NotesPreferences.Unset, default));

    // -----------------------------------------------------------------------
    //  Resize: the top-left corner stays
    // -----------------------------------------------------------------------

    [Fact]
    public void Resize_Wider_KeepsTheLeftEdge_AndMovesTheRightEdgeByTheDrag()
    {
        // Centred, the overlay's left edge is at 352 and its right at 928.
        // Widened by 100 it is 676 wide in a travel of 1248 − 676 = 572, and
        // the corner stays at 352 only at the position 336 / 572. The unset
        // height is held at the 200 it shows, its top at 300: 284 / 568 = ½.
        var resized = Desktop.Resize(NotesPreferences.Unset, 100, 0);

        Assert.Equal(P(336.0 / 572, 0.5, 676, 200), resized);
        var box = Desktop.Show(resized);
        Assert.Equal(352, box.Left, 9);
        Assert.Equal(928 + 100, box.Left + box.Width!.Value, 9);
        Assert.Equal((800 - 200) / 2.0, box.Top);
        Assert.Equal(200, box.Height);
    }

    [Fact]
    public void Resize_Taller_KeepsTheTopEdge_AndMovesTheBottomEdgeByTheDrag()
    {
        // Centred, the top edge is at 300; 50 taller is 250 in a travel of
        // 768 − 250 = 518, at the position 284 / 518. The unset width is held
        // at the 576 it shows, its left at 352: 336 / 672 = ½.
        var resized = Desktop.Resize(NotesPreferences.Unset, 0, 50);

        Assert.Equal(P(0.5, 284.0 / 518, 576, 250), resized);
        var box = Desktop.Show(resized);
        Assert.Equal(300, box.Top, 9);
        Assert.Equal(500 + 50, box.Top + box.Height!.Value, 9);
        Assert.Equal((1280 - 576) / 2.0, box.Left);
        Assert.Equal(576, box.Width);
    }

    [Fact]
    public void Resize_OnBothAxes_ChangesEachOnItsOwn()
    {
        Assert.Equal(P(336.0 / 572, 284.0 / 518, 676, 250), Desktop.Resize(NotesPreferences.Unset, 100, 50));
    }

    [Fact]
    public void Resize_StartsFromTheShownCorner_OfAPlacedSizedOverlay()
    {
        // At (¼, ¾) and 400 × 300: the corner is at (228, 367). 100 narrower
        // and 100 shorter, the travels are 948 and 568, and the corner's
        // positions within them 212 / 948 and 351 / 568.
        var resized = Desktop.Resize(P(0.25, 0.75, 400, 300), -100, -100);

        Assert.Equal(P(212.0 / 948, 351.0 / 568, 300, 200), resized);
        var box = Desktop.Show(resized);
        Assert.Equal(228, box.Left, 9);
        Assert.Equal(367, box.Top, 9);
    }

    [Fact]
    public void Resize_AtTheEndOfItsTravel_ThereIsNoRoomToGrow_AndNothingIsWritten()
    {
        // At the right of its travel the right edge is at the clearance: the
        // room from the corner is the width it has.
        var atTheRight = P(1, null);
        Assert.Equal(atTheRight, Desktop.Resize(atTheRight, 100, 0));
        Assert.Equal(atTheRight, Desktop.Resize(atTheRight, NotesSizeStep.Wider));
    }

    [Fact]
    public void Resize_PastTheRoom_StopsAtTheRoom_FromTheCorner()
    {
        // Centred, the room from the corner at 352 to the right clearance is
        // 1280 − 16 − 352 = 912 — not the whole space of 1248. At 912 the
        // travel is 336 and the corner at 352 is its end.
        Assert.Equal(P(1, 0.5, 912, 200), Desktop.Resize(NotesPreferences.Unset, 5000, 0));

        // Down: 800 − 16 − 300 = 484 of room; a travel of 284, its end.
        Assert.Equal(P(0.5, 1, 576, 484), Desktop.Resize(NotesPreferences.Unset, 0, 5000));
    }

    [Fact]
    public void Resize_BelowTheMinimum_StopsAtTheMinimum()
    {
        double width = NotesStage.MinimumWidth;
        Assert.Equal(P(336 / (SpaceAcross - width), 0.5, width, 200), Desktop.Resize(NotesPreferences.Unset, -5000, 0));
        Assert.Equal(P(0.5, 284 / (SpaceDown - MinimumHeight), 576, MinimumHeight), Desktop.Resize(NotesPreferences.Unset, 0, -5000));
    }

    [Fact]
    public void Resize_ThatMovesNothing_OrPushesPastTheBoundItStandsAt_ChangesNothing()
    {
        Assert.Equal(NotesPreferences.Unset, Desktop.Resize(NotesPreferences.Unset, 0, 0));

        var atTheMinimum = P(0.5, 0.5, NotesStage.MinimumWidth, MinimumHeight);
        Assert.Equal(atTheMinimum, Desktop.Resize(atTheMinimum, -50, -50));
    }

    [Fact]
    public void Resize_AnUnsetLengthBelowTheMinimum_IsNeverGrownByAShorterGesture()
    {
        // A one-line note's overlay is shorter than the minimum height; making
        // it shorter cannot make it taller.
        var oneLine = Stage(1280, 800, 576, 100, titleBarBottom: 56);
        Assert.Equal(NotesPreferences.Unset, oneLine.Resize(NotesPreferences.Unset, 0, -10));
        Assert.Equal(NotesPreferences.Unset, oneLine.Resize(NotesPreferences.Unset, NotesSizeStep.Shorter));
    }

    [Fact]
    public void Resize_WhereTheNewTravelIsNone_KeepsTheHeldPlacement()
    {
        // From the left of the travel, widened to all the room: the overlay
        // fills the space, so there is no travel to hold a position in.
        var widened = Desktop.Resize(P(0, 0.3), 5000, 0);
        Assert.Equal(0, widened.Placement.Horizontal);
        Assert.Equal(SpaceAcross, widened.Size.Width);
        Assert.Equal(P(0.5, 0, 576, SpaceDown), Desktop.Resize(P(null, 0), 0, 5000));
    }

    [Fact]
    public void Resize_AnAxisItDidNotChange_KeepsBothHeldValues_EvenWhereTheClampShowsThemOtherwise()
    {
        // A held width of 5000 shows the space; a resize of the height alone
        // leaves the width, and the horizontal position, as held.
        var resized = Desktop.Resize(P(0.25, null, 5000), 0, 40);

        Assert.Equal(5000, resized.Size.Width);
        Assert.Equal(0.25, resized.Placement.Horizontal);
        Assert.Equal(240, resized.Size.Height);
    }

    // -----------------------------------------------------------------------
    //  Resize: the first resize chooses the box (§4, amended 2026-10-27)
    // -----------------------------------------------------------------------

    [Fact]
    public void AWidthOnlyResize_FromAnUnsetHeight_HoldsTheShownHeight_AndTheTopStays()
    {
        // Placed at ¾ down a travel of 568 for the measured 200: the top at
        // 16 + 426 = 442. Narrowed, the height is held at 200 and the top is
        // kept by the same position, 426 / 568.
        var from = P(0.25, 0.75);
        var resized = Desktop.Resize(from, -100, 0);

        Assert.Equal(200, resized.Size.Height);
        Assert.Equal(0.75, resized.Placement.Vertical);
        var box = Desktop.Show(resized);
        Assert.Equal(442, box.Top, 9);
        Assert.Equal(200, box.Height);

        // A wider step likewise.
        Assert.Equal(200, Desktop.Resize(from, NotesSizeStep.Wider).Size.Height);
    }

    [Fact]
    public void AHeightOnlyResize_FromAnUnsetWidth_HoldsTheShownWidth_AndTheLeftEdgeStays()
    {
        // At ¼ across a travel of 672: the left edge at 16 + 168 = 184.
        var resized = Desktop.Resize(P(0.25, 0.75), 0, -50);

        Assert.Equal(576, resized.Size.Width);
        Assert.Equal(0.25, resized.Placement.Horizontal);
        var box = Desktop.Show(resized);
        Assert.Equal(184, box.Left, 9);
        Assert.Equal(576, box.Width);
    }

    [Fact]
    public void AShownHeightUnderTheMinimum_IsHeldAtTheMinimum_AndTheTopStays()
    {
        // A one-line note's overlay, 100 tall, centred: its top at 350. Held
        // at the minimum of 128, the top stays at 350 by the position
        // (350 − 16) / (768 − 128).
        var oneLine = Stage(1280, 800, 576, 100, titleBarBottom: 56);
        var resized = oneLine.Resize(NotesPreferences.Unset, 100, 0);

        Assert.Equal(MinimumHeight, resized.Size.Height);
        Assert.Equal(334.0 / 640, resized.Placement.Vertical);
        Assert.Equal(350, oneLine.Show(resized).Top, 9);
    }

    [Fact]
    public void AChosenLengthOnTheOtherAxis_IsKeptAsHeld_ThroughTheClamp()
    {
        // A held height of 5000 shows the space; a width-only resize keeps it
        // at 5000, and its position as held — the clamp writes nothing.
        var resized = Desktop.Resize(P(0.5, 0.3, null, 5000), 100, 0);

        Assert.Equal(5000, resized.Size.Height);
        Assert.Equal(0.3, resized.Placement.Vertical);
        Assert.Equal(676, resized.Size.Width);
    }

    [Fact]
    public void AnUnsetOtherAxisWithLessRoomThanTheMinimum_IsLeftAsItIs()
    {
        // 100 tall: a space of 68, under the minimum height. A width-only
        // resize changes the width and leaves the height unset.
        var low = Stage(1280, 100, 576, 68, titleBarBottom: 56);
        var resized = low.Resize(NotesPreferences.Unset, 100, 0);

        Assert.Equal(676, resized.Size.Width);
        Assert.Null(resized.Size.Height);
        Assert.Null(resized.Placement.Vertical);
    }

    [Fact]
    public void AResizeThatChangesNothing_ReturnsWhatItWasGiven_HoldingNothing()
    {
        Assert.Equal(NotesPreferences.Unset, Desktop.Resize(NotesPreferences.Unset, 0, 0));
        var atTheRight = P(1, null);
        Assert.Equal(atTheRight, Desktop.Resize(atTheRight, NotesSizeStep.Wider));
        Assert.Null(Desktop.Resize(atTheRight, 100, 0).Size.Height);
    }

    // -----------------------------------------------------------------------
    //  Resize: the size steps
    // -----------------------------------------------------------------------

    [Fact]
    public void TheSizeStep_IsAnEighthOfTheRange() => Assert.Equal(1.0 / 8, NotesStage.SizeStepFraction);

    [Fact]
    public void ASizeStep_IsAnEighthOfTheRangeFromTheMinimumToTheSpace_FromTheCorner()
    {
        double across = (SpaceAcross - NotesStage.MinimumWidth) / 8;
        const double down = (SpaceDown - MinimumHeight) / 8;   // 80

        Assert.Equal(576 + across, Desktop.Resize(NotesPreferences.Unset, NotesSizeStep.Wider).Size.Width);
        Assert.Equal(576 - across, Desktop.Resize(NotesPreferences.Unset, NotesSizeStep.Narrower).Size.Width);
        Assert.Equal(P(0.5, 284 / (SpaceDown - 280), 576, 280), Desktop.Resize(NotesPreferences.Unset, NotesSizeStep.Taller));
        // 200 less a step of 80 is under the minimum of 128, which it stops at.
        Assert.Equal(80, down);
        Assert.Equal(MinimumHeight, Desktop.Resize(NotesPreferences.Unset, NotesSizeStep.Shorter).Size.Height);
        Assert.Equal(280 + down, Desktop.Resize(P(null, 0, null, 280), NotesSizeStep.Taller).Size.Height);

        // Each from the same top-left corner.
        foreach (var step in Enum.GetValues<NotesSizeStep>())
        {
            var box = Desktop.Show(Desktop.Resize(NotesPreferences.Unset, step));
            Assert.Equal(352, box.Left, 9);
            Assert.Equal(300, box.Top, 9);
        }
    }

    [Fact]
    public void ASizeStep_ThatCannotChangeTheSize_ReturnsWhatItWasGiven()
    {
        var atTheMinimum = P(0.5, 0.5, NotesStage.MinimumWidth, MinimumHeight);
        Assert.Equal(atTheMinimum, Desktop.Resize(atTheMinimum, NotesSizeStep.Narrower));
        Assert.Equal(atTheMinimum, Desktop.Resize(atTheMinimum, NotesSizeStep.Shorter));

        var atTheBottomRight = P(1, 1);
        Assert.Equal(atTheBottomRight, Desktop.Resize(atTheBottomRight, NotesSizeStep.Wider));
        Assert.Equal(atTheBottomRight, Desktop.Resize(atTheBottomRight, NotesSizeStep.Taller));
    }

    [Fact]
    public void ASizeStep_ShortOfAWholeStepFromTheRoom_StopsAtTheRoom()
    {
        // At the left clearance and 30 px short of the space: one wider step
        // (more than 30 px) reaches the space and no further.
        var nearlyFull = P(0, null, SpaceAcross - 30);
        Assert.Equal(P(0, 0.5, SpaceAcross, 200), Desktop.Resize(nearlyFull, NotesSizeStep.Wider));
    }

    [Fact]
    public void Resize_RefusesAValueThatIsNoSizeStep() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Desktop.Resize(NotesPreferences.Unset, (NotesSizeStep)0));

    // -----------------------------------------------------------------------
    //  Small windows and a phone: the same bounds everywhere
    // -----------------------------------------------------------------------

    [Fact]
    public void AWindowSmallerThanTheMinimum_ShowsTheSpace_AndNeverThrows()
    {
        // 200 wide: a space of 168, under the minimum width. 100 tall: a space
        // of 68, under the minimum height of 128. The space wins on each.
        var narrow = Stage(200, 800, 168, 200, titleBarBottom: 56);
        var low = Stage(1280, 100, 576, 68, titleBarBottom: 56);

        Assert.Equal(168, narrow.Show(P(null, null, 400, 300)).Width);
        Assert.Equal(300, narrow.Show(P(null, null, 400, 300)).Height);
        Assert.Equal(68, low.Show(P(null, null, 400, 300)).Height);
        Assert.Equal(168, narrow.Show(P(null, null, 100, null)).Width);   // under the minimum, and still the space
    }

    [Fact]
    public void AWindowSmallerThanTheMinimum_RefusesAGestureOnThatAxis_AndResizesTheOther()
    {
        var narrow = Stage(200, 800, 168, 200, titleBarBottom: 56);
        var held = P(0.25, 0.5, 400, 300);

        // Across: no room for the minimum, so neither held value moves.
        Assert.Equal(held, narrow.Resize(held, 50, 0));
        Assert.Equal(held, narrow.Resize(held, -50, 0));
        Assert.Equal(held, narrow.Resize(held, NotesSizeStep.Wider));
        Assert.Equal(held, narrow.Resize(held, NotesSizeStep.Narrower));

        // Down: the height still resizes, and the width and its position stay as held.
        var taller = narrow.Resize(held, 0, 40);
        Assert.Equal(P(0.25, 234.0 / 428, 400, 340), taller);
    }

    [Fact]
    public void APhonesWidth_NarrowerThenWider_WorksWithinTheSameBounds()
    {
        // The default width fills a phone's space of 343: no travel, the
        // corner at the clearance, the room 343. Nothing about a phone is
        // special: one step is an eighth of the range from the minimum.
        const double space = 375 - 2 * Clearance;
        double step = (space - NotesStage.MinimumWidth) / 8;

        var narrower = Phone.Resize(NotesPreferences.Unset, NotesSizeStep.Narrower);
        Assert.Equal(P(0, 0.5, space - step, 220), narrower);
        Assert.Equal(Clearance, Phone.Show(narrower).Left, 9);

        var wider = Phone.Resize(narrower, NotesSizeStep.Wider);
        Assert.Equal(P(0, 0.5, space, 220), wider);
        Assert.Equal(wider, Phone.Resize(wider, NotesSizeStep.Wider));   // at the room

        // Narrowed to the minimum by drag, then no further.
        var narrowest = Phone.Resize(NotesPreferences.Unset, -5000, 0);
        Assert.Equal(NotesStage.MinimumWidth, narrowest.Size.Width);
        Assert.Equal(narrowest, Phone.Resize(narrowest, NotesSizeStep.Narrower));
    }
}
