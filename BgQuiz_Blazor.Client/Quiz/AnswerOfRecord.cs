namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;

/// <summary>
/// A problem's <i>answer of record</i> — the submission that counts
/// (SPEC-scoring.md §1 and §2): a scored checker play or a scored cube pair.
/// It is what an <see cref="ProblemDispositionKind.Answered"/> disposition
/// carries, and with it everything the session score and the lifetime fold
/// need from the problem.
///
/// <para>
/// <b>Two kinds, read through one door.</b> The two scored records are distinct
/// shapes — a <see cref="SubmittedPlay"/> and a <see cref="SubmittedCubeAction"/>
/// — and every consumer needs the one it holds: the score folds each by its own
/// <see cref="QuizScore"/> overload, the stats sink by its own. <see cref="Match{TResult}"/>
/// hands the record to the branch for its kind, so a consumer names both kinds
/// and no type test or default arm stands in for one — the shape the producers
/// use for their own closed unions (<see cref="PlaySubmission.Match{TResult}"/>).
/// </para>
///
/// <para>
/// <b>A skip is not one of these.</b> A skip is of record too, but it is a
/// disposition of its own (<see cref="ProblemDisposition.Skipped"/>) and
/// carries no submission (SPEC-quiz-history.md §1), so there is nothing here
/// for it to hold.
/// </para>
///
/// <para>
/// <b>Reference-equal, deliberately.</b> Nothing compares answers of record:
/// the run identifies a problem by its place in the presented sequence and
/// keeps what that place holds (SPEC-quiz-history.md §7). So this type defines
/// no equality, and none of the submissions' own reaches application code
/// through it.
/// </para>
/// </summary>
internal abstract class AnswerOfRecord
{
    // Closed: only the two nested kinds below can derive.
    private AnswerOfRecord() { }

    /// <summary>The answer of record that is a scored checker play.</summary>
    /// <param name="submission">The scored play, whole, as the producer scored it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="submission"/> is null.</exception>
    public static AnswerOfRecord Of(SubmittedPlay submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return new PlayAnswer(submission);
    }

    /// <summary>The answer of record that is a scored cube pair.</summary>
    /// <param name="submission">The scored cube submission, whole, as the producer scored it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="submission"/> is null.</exception>
    public static AnswerOfRecord Of(SubmittedCubeAction submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return new CubeAnswer(submission);
    }

    /// <summary>
    /// Hands the scored record to the one branch for its kind: the very
    /// instance this answer was made from.
    /// </summary>
    /// <typeparam name="TResult">What each branch returns.</typeparam>
    /// <param name="play">The branch for a scored checker play.</param>
    /// <param name="cube">The branch for a scored cube pair.</param>
    /// <returns>The chosen branch's result.</returns>
    /// <exception cref="ArgumentNullException">A branch is null.</exception>
    public TResult Match<TResult>(
        Func<SubmittedPlay, TResult> play, Func<SubmittedCubeAction, TResult> cube)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentNullException.ThrowIfNull(cube);
        return MatchCore(play, cube);
    }

    private protected abstract TResult MatchCore<TResult>(
        Func<SubmittedPlay, TResult> play, Func<SubmittedCubeAction, TResult> cube);

    private sealed class PlayAnswer(SubmittedPlay submission) : AnswerOfRecord
    {
        private protected override TResult MatchCore<TResult>(
            Func<SubmittedPlay, TResult> play, Func<SubmittedCubeAction, TResult> cube) =>
            play(submission);
    }

    private sealed class CubeAnswer(SubmittedCubeAction submission) : AnswerOfRecord
    {
        private protected override TResult MatchCore<TResult>(
            Func<SubmittedPlay, TResult> play, Func<SubmittedCubeAction, TResult> cube) =>
            cube(submission);
    }
}
