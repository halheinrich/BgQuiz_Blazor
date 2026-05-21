namespace BgQuiz_Blazor.Quiz;

/// <summary>
/// Bound from the <c>Quiz</c> section of <c>appsettings.json</c> (and overridable
/// via environment variables / user-secrets per the standard ASP.NET Core
/// configuration chain).
/// </summary>
public sealed class QuizOptions
{
    /// <summary>
    /// Default problem-set directory used to seed a fresh circuit's
    /// <see cref="ProblemSetSelection"/>. Not the runtime authority — the user
    /// edits the directory on the landing page, and that choice (persisted to
    /// localStorage) overrides this seed. Empty string means "no default": the
    /// landing page then starts with an unset directory and gates the Start
    /// Quiz button until the user supplies one.
    /// </summary>
    public string ProblemSetDirectory { get; set; } = string.Empty;
}
