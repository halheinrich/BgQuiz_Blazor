namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// Where the decision's notes overlay is shown: its top-left corner, in CSS
/// pixels from the visible area's top-left corner. Computed by
/// <see cref="NotesStage.Show"/> and never stored — what is stored is the
/// <see cref="NotesPlacement"/>, which lands at a different position in a
/// different window.
/// </summary>
/// <param name="Left">The overlay's left edge.</param>
/// <param name="Top">The overlay's top edge.</param>
internal readonly record struct NotesPosition(double Left, double Top);
