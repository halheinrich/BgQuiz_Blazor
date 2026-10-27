namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The four size step buttons beside the notes' four move steps
/// (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's size is a remembered
/// preference": the single-pointer alternative to the resize handle that WCAG
/// 2.2 SC 2.5.7 asks for). Each press changes the overlay's size one step —
/// see <see cref="NotesStage.SizeStepFraction"/> — from the top-left corner
/// where it is shown.
/// </summary>
internal enum NotesSizeStep
{
    /// <summary>The right edge outwards.</summary>
    Wider = 1,

    /// <summary>The right edge inwards.</summary>
    Narrower,

    /// <summary>The bottom edge downwards.</summary>
    Taller,

    /// <summary>The bottom edge upwards.</summary>
    Shorter,
}
