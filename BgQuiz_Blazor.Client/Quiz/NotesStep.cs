namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The four step buttons of the notes' Move control (<c>SPEC-quiz-view.md</c>
/// §4: the single-pointer alternative to dragging that WCAG 2.2 SC 2.5.7
/// asks for). Each press moves the overlay one step — see
/// <see cref="NotesStage.StepFraction"/> — in its direction.
/// </summary>
internal enum NotesStep
{
    /// <summary>Towards the top of the window.</summary>
    Up = 1,

    /// <summary>Towards the bottom of the window.</summary>
    Down,

    /// <summary>Towards the left of the window.</summary>
    Left,

    /// <summary>Towards the right of the window.</summary>
    Right,
}
