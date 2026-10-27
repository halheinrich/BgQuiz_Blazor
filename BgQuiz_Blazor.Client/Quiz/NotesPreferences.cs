namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The decision's notes' two remembered preferences, as one value: where the
/// overlay opens (<see cref="NotesPlacement"/>, <c>SPEC-quiz-view.md</c> §4,
/// issue <c>halheinrich/backgammon#344</c>) and how large
/// (<see cref="NotesSize"/>, §4, issue <c>halheinrich/backgammon#414</c>).
/// They are one value because one gesture can change both — a resize keeps
/// the overlay's top-left corner where it was shown, which is a new placement
/// in the shorter or longer travel — and one Reset clears both. What
/// <see cref="NotesPreferencesStore"/> holds, and what every gesture of
/// <see cref="NotesStage"/> takes and returns.
/// </summary>
/// <param name="Placement">Where the overlay opens.</param>
/// <param name="Size">How large it opens.</param>
internal readonly record struct NotesPreferences(NotesPlacement Placement, NotesSize Size)
{
    /// <summary>Both preferences unset: the overlay centred at the stylesheet's size. The same value as <c>default</c>.</summary>
    public static NotesPreferences Unset => default;

    /// <summary>True when neither preference has been set — the default, and what Reset returns to.</summary>
    public bool IsUnset => Placement.IsUnset && Size.IsUnset;
}
