namespace BgQuiz_Blazor.Quiz;

using BgGame_Lib;
using Microsoft.Extensions.Logging;
using XgFilter_Lib.Filtering;

/// <summary>
/// Phase 1 <see cref="ProblemSetSourceFactory"/> implementation: builds a
/// <see cref="ServerDiskProblemSetSource"/> over the directory currently held
/// in the per-circuit <see cref="ProblemSetSelection"/>.
///
/// <para>
/// Lifetime: <b>Scoped</b>. <c>Program.cs</c> binds <see cref="Create"/> as the
/// <see cref="ProblemSetSourceFactory"/> delegate the <see cref="QuizController"/>
/// invokes at quiz-start. The directory is read at invocation time, not at
/// registration — so a user's in-session change to the selection takes effect
/// on the next <see cref="QuizController.StartAsync"/>.
/// </para>
///
/// <para>
/// Phase 2+ source kinds (uploaded files, deployed bundles, curated libraries)
/// plug in by registering a different factory; the controller is unchanged.
/// </para>
/// </summary>
public sealed class ServerDiskProblemSetSourceFactory
{
    private readonly ProblemSetSelection _selection;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Construct the factory over the per-circuit <paramref name="selection"/>.
    /// Per-file read failures inside the source's underlying iterator are
    /// logged through <paramref name="loggerFactory"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="selection"/> or <paramref name="loggerFactory"/> is null.
    /// </exception>
    public ServerDiskProblemSetSourceFactory(
        ProblemSetSelection selection,
        ILoggerFactory loggerFactory)
    {
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    /// Build a source over the currently-selected directory, applying
    /// <paramref name="filters"/>. Matches the <see cref="ProblemSetSourceFactory"/>
    /// delegate shape.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No problem-set directory has been selected —
    /// <see cref="ProblemSetSelection.Directory"/> is empty or whitespace.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">
    /// The selected directory does not exist on disk — propagated from
    /// <see cref="ServerDiskProblemSetSource"/>'s constructor.
    /// </exception>
    public IProblemSetSource Create(DecisionFilterSet filters)
    {
        var directory = _selection.Directory;
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException(
                "No problem-set directory has been selected.");
        return new ServerDiskProblemSetSource(directory, filters, _loggerFactory);
    }
}
