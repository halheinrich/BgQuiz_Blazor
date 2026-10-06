using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="NotesStage"/>, the placement arithmetic of
/// <c>SPEC-quiz-view.md</c> §4, "The notes overlay's placement is a remembered
/// preference" (issue <c>halheinrich/backgammon#344</c>): where a placement is
/// shown — 0, 1 and between within the travel, centred where it is unset or
/// where an axis has no travel, and clamped so the title bar stays in view —
/// and what a drag and a step make of it, each axis on its own.
///
/// <para>
/// The expected numbers are worked out here from the ruling's words, not read
/// back from the type: the travel is the area less the clearance on each side,
/// less the overlay, and a position <c>p</c> stands <c>clearance + p ×
/// travel</c> from the start edge. The desktop stage below has a travel of
/// 1280 − 32 − 576 = 672 across and 800 − 32 − 200 = 568 down.
/// </para>
/// </summary>
public class NotesStageTests
{
    private const double Clearance = 16;

    /// <summary>A desktop window and a short note: travel on both axes.</summary>
    private static readonly NotesStage Desktop = Stage(1280, 800, 576, 200, titleBarBottom: 56);

    private const double TravelAcross = 1280 - 2 * Clearance - 576;

    private const double TravelDown = 800 - 2 * Clearance - 200;

    /// <summary>A phone: the overlay fills the width less the clearance, so there is no horizontal travel.</summary>
    private static readonly NotesStage Phone = Stage(375, 812, 375 - 2 * Clearance, 220, titleBarBottom: 56);

    private static NotesStage Stage(
        double areaWidth, double areaHeight, double overlayWidth, double overlayHeight, double titleBarBottom,
        double clearance = Clearance)
    {
        Assert.True(NotesStage.TryCreate(
            areaWidth, areaHeight, clearance, overlayWidth, overlayHeight, titleBarBottom, out var stage));
        return stage;
    }

    private static NotesPlacement At(double? horizontal, double? vertical) => NotesPlacement.Create(horizontal, vertical);

    // -----------------------------------------------------------------------
    //  Measurements
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(double.NaN, 800, 16, 576, 200, 56)]
    [InlineData(1280, double.PositiveInfinity, 16, 576, 200, 56)]
    [InlineData(0, 800, 16, 576, 200, 56)]
    [InlineData(1280, 800, -1, 576, 200, 56)]
    [InlineData(1280, 800, 16, 0, 200, 56)]
    [InlineData(1280, 800, 16, 576, -3, 56)]
    [InlineData(1280, 800, 16, 576, 200, double.NaN)]
    public void TryCreate_RefusesMeasurementsThatAreNoStage(
        double areaWidth, double areaHeight, double clearance, double overlayWidth, double overlayHeight, double titleBarBottom)
    {
        Assert.False(NotesStage.TryCreate(
            areaWidth, areaHeight, clearance, overlayWidth, overlayHeight, titleBarBottom, out var stage));
        Assert.Equal(default, stage);
    }

    [Fact]
    public void TryCreate_AcceptsNoClearanceAndNoTitleBar()
    {
        Assert.True(NotesStage.TryCreate(1280, 800, 0, 576, 200, 0, out var stage));
        Assert.Equal(0, stage.Clearance);
        Assert.Equal(0, stage.TitleBarBottom);
    }

    // -----------------------------------------------------------------------
    //  Show
    // -----------------------------------------------------------------------

    [Fact]
    public void Show_Unset_IsCentred()
    {
        Assert.Equal(new NotesPosition((1280 - 576) / 2.0, (800 - 200) / 2.0), Desktop.Show(NotesPlacement.Unset));
    }

    [Fact]
    public void Show_ZeroAndOne_AreTheTravelsEnds_ClearanceFromEachEdge()
    {
        Assert.Equal(new NotesPosition(Clearance, Clearance), Desktop.Show(At(0, 0)));
        Assert.Equal(new NotesPosition(1280 - Clearance - 576, 800 - Clearance - 200), Desktop.Show(At(1, 1)));
    }

    [Fact]
    public void Show_BetweenTheEnds_IsProportional_EachAxisOnItsOwn()
    {
        Assert.Equal(
            new NotesPosition(Clearance + 0.25 * TravelAcross, Clearance + 0.75 * TravelDown),
            Desktop.Show(At(0.25, 0.75)));

        // One axis unset is that axis centred, whatever the other holds.
        Assert.Equal(
            new NotesPosition(Clearance + 0.25 * TravelAcross, (800 - 200) / 2.0),
            Desktop.Show(At(0.25, null)));
    }

    [Fact]
    public void Show_OnePlacement_LandsProportionally_ForANoteOfAnyLength()
    {
        // A long note is taller, so its vertical travel is shorter; the same
        // position lands the same way along it.
        var longNote = Stage(1280, 800, 576, 512, titleBarBottom: 56);
        var placement = At(0.5, 0.875);

        Assert.Equal(Clearance + 0.875 * TravelDown, Desktop.Show(placement).Top);
        Assert.Equal(Clearance + 0.875 * (800 - 2 * Clearance - 512), longNote.Show(placement).Top);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(null)]
    public void Show_AnAxisWithNoTravel_IsCentred_WhateverItHolds(double? held)
    {
        var shown = Phone.Show(At(held, 0.25));

        Assert.Equal(Clearance, shown.Left);
        Assert.Equal(Clearance + 0.25 * (812 - 2 * Clearance - 220), shown.Top);
    }

    [Fact]
    public void Show_TravelUnderHalfAPixel_IsNoTravel()
    {
        var almostFull = Stage(1000, 800, 1000 - 2 * Clearance - 0.4, 200, titleBarBottom: 56);
        var justMovable = Stage(1000, 800, 1000 - 2 * Clearance - NotesStage.MinimumTravel, 200, titleBarBottom: 56);

        Assert.Equal((1000 - almostFull.OverlayWidth) / 2, almostFull.Show(At(1, null)).Left);
        Assert.Equal(Clearance + NotesStage.MinimumTravel, justMovable.Show(At(1, null)).Left);
    }

    [Fact]
    public void Show_ClampsAtTheEndEdge_SoTheTitleBarStaysInView()
    {
        // A title bar that reaches further down than the overlay is tall — a
        // very short overlay — would leave the window at the travel's far end;
        // the clamp stops it where the title bar's bottom meets the window's.
        var shortOverlay = Stage(1280, 300, 576, 100, titleBarBottom: 280);

        Assert.Equal(300 - 280, shortOverlay.Show(At(null, 1)).Top);
        Assert.Equal(Clearance, shortOverlay.Show(At(null, 0)).Top);
    }

    [Fact]
    public void Show_ClampsAtTheStartEdge_WhereTheOverlayCannotFit()
    {
        // Centring an overlay larger than the area would put its start edge
        // outside; where it cannot fit at all, the start edge — the left, the
        // top — wins.
        var tooWide = Stage(300, 800, 310, 200, titleBarBottom: 56);
        var tooTall = Stage(1280, 90, 576, 100, titleBarBottom: 95);

        Assert.Equal(0, tooWide.Show(NotesPlacement.Unset).Left);
        Assert.Equal(0, tooTall.Show(NotesPlacement.Unset).Top);
    }

    [Fact]
    public void Show_TheClampChangesWhatIsShown_NeverWhatIsHeld()
    {
        // A very small window with no vertical travel left: the overlay is
        // centred, then pulled up so the title bar fits. Grown again, the same
        // placement shows where the user put it.
        var tiny = Stage(260, 100, 228, 68, titleBarBottom: 96);
        var placement = At(1, 1);

        Assert.Equal(new NotesPosition(Clearance, 100 - 96), tiny.Show(placement));
        Assert.Equal(new NotesPosition(1280 - Clearance - 576, 800 - Clearance - 200), Desktop.Show(placement));
    }

    // -----------------------------------------------------------------------
    //  Drag
    // -----------------------------------------------------------------------

    [Fact]
    public void Drag_MovesEachAxisByTheDragOverItsTravel()
    {
        Assert.Equal(At(0.625, 0.375), Desktop.Drag(NotesPlacement.Unset, TravelAcross / 8, -TravelDown / 8));
        Assert.Equal(At(0.5, 0.5), Desktop.Drag(At(0.25, 0.75), TravelAcross / 4, -TravelDown / 4));
    }

    [Fact]
    public void Drag_StopsAtEachEndOfTheTravel()
    {
        Assert.Equal(At(0, 0), Desktop.Drag(At(0.5, 0.5), -5000, -5000));
        Assert.Equal(At(1, 1), Desktop.Drag(At(0.5, 0.5), 5000, 5000));
    }

    [Fact]
    public void Drag_ChangesOnlyTheAxesItMoved()
    {
        // Straight down from unset: the horizontal stays unset, not 0.5.
        var down = Desktop.Drag(NotesPlacement.Unset, 0, TravelDown / 8);
        Assert.Null(down.Horizontal);
        Assert.Equal(0.625, down.Vertical);

        // Straight across: the vertical keeps what it held.
        Assert.Equal(At(0.75, 0.3), Desktop.Drag(At(0.5, 0.3), TravelAcross / 4, 0));
    }

    [Fact]
    public void Drag_ThatEndsWhereItBegan_ChangesNothing()
    {
        Assert.Equal(NotesPlacement.Unset, Desktop.Drag(NotesPlacement.Unset, 0, 0));

        // Pushed further against an edge it already stands at.
        Assert.Equal(At(1, null), Desktop.Drag(At(1, null), 300, 0));
    }

    [Fact]
    public void Drag_OnAnAxisWithNoTravel_KeepsWhatItHolds_AndMovesTheOther()
    {
        var moved = Phone.Drag(At(0.875, 0.5), 120, (812 - 2 * Clearance - 220) / 8);

        Assert.Equal(0.875, moved.Horizontal);
        Assert.Equal(0.625, moved.Vertical);
        Assert.Equal(NotesPlacement.Unset, Phone.Drag(NotesPlacement.Unset, 120, 0));
    }

    [Fact]
    public void Drag_StartsFromWhereTheClampShowsTheOverlay()
    {
        // Held at the bottom, shown clamped 20 px down a travel of 168; a drag
        // of 10 px down moves it from there, not from the far end it holds.
        var shortOverlay = Stage(1280, 300, 576, 100, titleBarBottom: 280);
        const double travel = 300 - 2 * Clearance - 100;

        var moved = shortOverlay.Drag(At(null, 1), 0, 10);

        Assert.Equal((300 - 280 - Clearance + 10) / travel, moved.Vertical!.Value, 12);
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
        Assert.Equal(At(horizontal, vertical), Desktop.Step(NotesPlacement.Unset, Enum.Parse<NotesStep>(step)));
    }

    [Fact]
    public void Step_FourFromTheCentre_ReachAnEdge_AndTheNextCannotMove()
    {
        var placement = NotesPlacement.Unset;
        for (int i = 0; i < 4; i++) placement = Desktop.Step(placement, NotesStep.Up);

        Assert.Equal(At(null, 0), placement);
        Assert.Equal(placement, Desktop.Step(placement, NotesStep.Up));
    }

    [Fact]
    public void Step_ShortOfAWholeStepFromTheEdge_StopsAtTheEdge()
    {
        Assert.Equal(At(1, null), Desktop.Step(At(0.95, null), NotesStep.Right));
        Assert.Equal(At(0, 0.3), Desktop.Step(At(0.05, 0.3), NotesStep.Left));
    }

    [Theory]
    [InlineData(nameof(NotesStep.Left))]
    [InlineData(nameof(NotesStep.Right))]
    public void Step_AcrossAnAxisWithNoTravel_CannotMove_AndUnsetStaysUnset(string step)
    {
        var across = Enum.Parse<NotesStep>(step);
        Assert.Equal(NotesPlacement.Unset, Phone.Step(NotesPlacement.Unset, across));
        Assert.Equal(At(0.875, 0.5), Phone.Step(At(0.875, 0.5), across));
    }

    [Fact]
    public void Step_AlongTheOtherAxis_KeepsWhatTheAxisWithNoTravelHolds()
    {
        Assert.Equal(At(0.875, 0.375), Phone.Step(At(0.875, 0.5), NotesStep.Up));
    }

    [Fact]
    public void Step_RefusesAValueThatIsNoStep() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Desktop.Step(NotesPlacement.Unset, default));
}
