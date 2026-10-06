namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// Where the decision's notes appear when they are open: the <b>placement
/// preference</b> of <c>SPEC-quiz-view.md</c> §4, "The notes overlay's
/// placement is a remembered preference" (ruled 2026-10-05, issue
/// <c>halheinrich/backgammon#344</c>). One per browser, held by
/// <see cref="NotesPlacementStore"/>; shown, moved and stepped by
/// <see cref="NotesStage"/>.
///
/// <para>
/// <b>Each axis is held on its own</b>, as a position within the overlay's
/// travel on that axis: 0 at the start (left, top), 1 at the end (right,
/// bottom), or <c>null</c> for an axis no move has set, which shows the
/// overlay centred on it. The travel is measured when the overlay is shown, so
/// one placement lands proportionally in any window and for any note; nothing
/// here knows a pixel.
/// </para>
///
/// <para>
/// <b>Unset is the default</b> — <see cref="Unset"/>, which is also
/// <c>default</c>: both axes <c>null</c>, the overlay centred in the window as
/// it opened before the preference existed. A placement is unset exactly when
/// both axes are.
/// </para>
///
/// <para>
/// <b>Valid by construction.</b> The only ways in are <see cref="Unset"/> and
/// <see cref="Create"/>, which refuses a position that is not a finite number
/// from 0 to 1, so nothing downstream ever meets one — the store's tolerant
/// read turns stored garbage into <see cref="Unset"/> through
/// <see cref="TryCreate"/> rather than repairing it.
/// </para>
/// </summary>
internal readonly record struct NotesPlacement
{
    private NotesPlacement(double? horizontal, double? vertical)
    {
        Horizontal = horizontal;
        Vertical = vertical;
    }

    /// <summary>The unset preference: both axes centred. The same value as <c>default</c>.</summary>
    public static NotesPlacement Unset => default;

    /// <summary>The position within the horizontal travel, 0 (left) to 1 (right); <c>null</c> when unset.</summary>
    public double? Horizontal { get; }

    /// <summary>The position within the vertical travel, 0 (top) to 1 (bottom); <c>null</c> when unset.</summary>
    public double? Vertical { get; }

    /// <summary>True when neither axis has been set — the default, and what Reset returns to.</summary>
    public bool IsUnset => Horizontal is null && Vertical is null;

    /// <summary>
    /// A placement holding <paramref name="horizontal"/> and
    /// <paramref name="vertical"/>, each a position from 0 to 1 within its
    /// axis's travel or <c>null</c> for an unset axis.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A position is not a finite number from 0 to 1.</exception>
    public static NotesPlacement Create(double? horizontal, double? vertical)
    {
        if (!IsPosition(horizontal))
            throw new ArgumentOutOfRangeException(nameof(horizontal), horizontal, PositionRule);
        if (!IsPosition(vertical))
            throw new ArgumentOutOfRangeException(nameof(vertical), vertical, PositionRule);
        return new NotesPlacement(horizontal, vertical);
    }

    /// <summary>
    /// <see cref="Create"/> for input this app does not control: false, with
    /// <see cref="Unset"/>, where a position is not a finite number from 0 to 1.
    /// </summary>
    public static bool TryCreate(double? horizontal, double? vertical, out NotesPlacement placement)
    {
        if (IsPosition(horizontal) && IsPosition(vertical))
        {
            placement = new NotesPlacement(horizontal, vertical);
            return true;
        }
        placement = Unset;
        return false;
    }

    /// <summary>This placement with the horizontal position replaced; the vertical one is kept.</summary>
    /// <exception cref="ArgumentOutOfRangeException">See <see cref="Create"/>.</exception>
    public NotesPlacement WithHorizontal(double? horizontal) => Create(horizontal, Vertical);

    /// <summary>This placement with the vertical position replaced; the horizontal one is kept.</summary>
    /// <exception cref="ArgumentOutOfRangeException">See <see cref="Create"/>.</exception>
    public NotesPlacement WithVertical(double? vertical) => Create(Horizontal, vertical);

    private const string PositionRule = "A position within the travel is a finite number from 0 to 1, or null for an unset axis.";

    private static bool IsPosition(double? value) =>
        value is not double position || (double.IsFinite(position) && position is >= 0 and <= 1);
}
