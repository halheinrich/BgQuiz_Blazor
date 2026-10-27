namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The decision's notes overlay as it is to be drawn: its top-left corner, in
/// CSS pixels from the layout viewport's top-left corner, and the length to
/// render on each axis whose size is set. Computed by
/// <see cref="NotesStage.Show"/> and never stored — what is stored is the
/// <see cref="NotesPreferences"/>, which lands as a different box in a
/// different window.
/// </summary>
/// <param name="Left">The overlay's left edge.</param>
/// <param name="Top">The overlay's top edge.</param>
/// <param name="Width">The width to render, the show clamp applied; <c>null</c> where the size is unset, leaving the stylesheet's default.</param>
/// <param name="Height">The height to render, likewise.</param>
internal readonly record struct NotesBox(double Left, double Top, double? Width, double? Height);
