namespace BgQuiz_Blazor.Quiz;

/// <summary>
/// Bound from the <c>Quiz</c> section of <c>appsettings.json</c> (and overridable
/// via environment variables / user-secrets per the standard ASP.NET Core
/// configuration chain).
/// </summary>
public sealed class QuizOptions
{
    /// <summary>
    /// Absolute path to a directory of <c>.xg</c> files used as the problem-set
    /// source. Empty string means "not configured" — the landing page surfaces
    /// this as a blocking message and disables the Start Quiz button.
    /// </summary>
    public string ProblemSetDirectory { get; set; } = string.Empty;
}
