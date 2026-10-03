namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// How a <see cref="ProblemLocator"/> presents the facts it locates a problem
/// by. The facts and their wording are the component's either way; only the
/// presentation differs.
/// </summary>
public enum ProblemLocatorForm
{
    /// <summary>
    /// The chip in the action row's tail: the file name shortened, the
    /// coordinates in their short form (<c>G3 · M12</c>), the full wording the
    /// accessible text and the tooltip (<c>SPEC-quiz-view.md</c> §4).
    /// </summary>
    Chip,

    /// <summary>
    /// One line of text in the full wording — the file name whole, then
    /// <c>Game 3 · Move 12</c> — for the action row's "⋯" list, where the tail
    /// folds behind one control and the locator is a line to read
    /// (<c>SPEC-quiz-view.md</c> §4, halheinrich/backgammon#264's widened
    /// fourth, Hal, 2026-10-03). What shows there is what a screen reader
    /// reads, so there is no hidden twin.
    /// </summary>
    Line,
}
