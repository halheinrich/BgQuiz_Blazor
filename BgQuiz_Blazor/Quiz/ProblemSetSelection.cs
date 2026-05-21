namespace BgQuiz_Blazor.Quiz;

/// <summary>
/// Per-circuit holder for the user's chosen problem-set source directory.
///
/// <para>
/// Lifetime: <b>Scoped</b> — one instance per Blazor Server circuit, the same
/// lifetime as <see cref="QuizController"/>. Seeded at construction from the
/// configured <see cref="QuizOptions.ProblemSetDirectory"/> default;
/// <c>Home.razor</c> overrides it from the user's localStorage-persisted
/// choice and writes back on every edit.
/// </para>
///
/// <para>
/// Deliberately a bare mutable holder with no behaviour: it shuttles UI state
/// between <c>Home.razor</c> (the writer) and
/// <see cref="ServerDiskProblemSetSourceFactory"/> (the reader). Directory
/// validity is enforced where it is acted on — the Start-button gate in
/// <c>Home.razor</c> and the hard guards in
/// <see cref="ServerDiskProblemSetSourceFactory.Create"/> and
/// <see cref="ServerDiskProblemSetSource"/> — so there is nothing for this
/// type itself to encapsulate.
/// </para>
/// </summary>
public sealed class ProblemSetSelection
{
    /// <summary>
    /// Absolute path to the directory of <c>.xg</c>/<c>.xgp</c> files the next
    /// quiz will draw from. Empty string means "not yet chosen".
    /// </summary>
    public string Directory { get; set; } = string.Empty;
}
