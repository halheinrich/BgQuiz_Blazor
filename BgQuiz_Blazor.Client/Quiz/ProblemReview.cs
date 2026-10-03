namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;
using BgGame_Lib;

/// <summary>
/// The scored outcome of a just-submitted problem — the <i>displayed
/// review</i>, held by the run (<see cref="QuizRun.Review"/>, surfaced as
/// <see cref="QuizController.Review"/>) from Submit until the cursor leaves
/// the problem. It carries exactly what the review
/// surfaces need to mark and name the user's answer: for a checker play, the
/// producer's scored outcome (<see cref="PlaySubmission"/>) and the play the
/// user submitted; for a cube decision, the producer's scored answer and the
/// decision it was scored at.
///
/// <para>
/// <b>Displayed, not of record.</b> Every submission produces one of these,
/// including practice submissions on a problem returned to (SPEC-scoring.md §2:
/// practice still reviews — "discarded" governs the record, not the pixels).
/// What <i>counts</i> is the problem's disposition, which the run holds apart
/// from this (<see cref="ProblemDisposition"/>); <see cref="IsPractice"/> is
/// the one fact of that split this type carries, so a review and its practice
/// status can never be assigned separately and drift.
/// </para>
///
/// <para>
/// <b>Both variants wrap the producer's scored outcome whole.</b> Copying
/// fields out of it — the matched candidate, the error, the verdict — would put
/// a second spelling of what the producer derives together beside the
/// producer's own (halheinrich/backgammon#86 for the cube,
/// halheinrich/backgammon#273 for the play). The play review used to restate
/// them, because its off-list case had no scored record at all; the producer's
/// <see cref="PlaySubmission"/> names all three cases now, so it no longer
/// has to.
/// </para>
///
/// <para>
/// <b>A closed class hierarchy, deliberately not records.</b> The private
/// constructor permits only the two nested variants (<see cref="Play"/>,
/// <see cref="Cube"/>), mirroring the play / cube split of the producer's
/// <see cref="PlaySubmission"/> / <see cref="SubmittedCubeAnswer"/>. They are
/// classes because nothing compares a review and a record's generated
/// equality would have to: it would reach the submitted
/// <see cref="BgDataTypes_Lib.Play"/>, which has no equality (its
/// <c>Equals</c> throws), and <see cref="PlaySubmission"/>'s equality, which
/// for a skipped play is not settled (halheinrich/backgammon#287) and which this
/// app does not use as application semantics. A review is reference-equal.
/// This review type is BgQuiz_Blazor's own per-problem UI state and does not
/// cross the submodule boundary into <c>BgGame_Lib</c>.
/// </para>
/// </summary>
internal abstract class ProblemReview
{
    private ProblemReview() { }

    /// <summary>
    /// True when this review shows a <i>practice</i> submission — one made
    /// against a problem that was already completed, answered or skipped
    /// (SPEC-scoring.md §2; SPEC-quiz-history.md §3), returned to by
    /// navigation. Such a
    /// submission is discarded as if it never happened: no session score,
    /// nothing of record, no lifetime fold. It is still scored and shown,
    /// because seeing how the retry scored is the point of the gesture; this
    /// flag is what lets the page say so.
    ///
    /// <para>
    /// <b>A fact about this submission, not a second record of the
    /// problem.</b> Whether the <i>next</i> submission would be live is never
    /// stored — the run reads it off the problem's disposition
    /// (<see cref="QuizRun.IsLive"/>). What this review keeps is what that
    /// reading was when its own submission was made, which the disposition
    /// cannot say afterwards: a skip of record carries no submission, so a
    /// review of the play that caused it and a review of a later practice play
    /// look the same from the record.
    /// </para>
    ///
    /// <para>
    /// <c>init</c>-only and defaulted false: the fact is known exactly where a
    /// review is constructed (the run's submit transitions), and nothing may
    /// re-badge a review afterwards.
    /// </para>
    /// </summary>
    public bool IsPractice { get; init; }

    /// <summary>
    /// A submitted checker play, scored under the quiz's ranking by the
    /// producer's one scoring operation (<see cref="PlaySubmission.Score"/>).
    /// </summary>
    internal sealed class Play : ProblemReview
    {
        /// <summary>Wrap a scored play and the play it was scored from.</summary>
        /// <param name="submission">The producer's scored outcome of <paramref name="userPlay"/>.</param>
        /// <param name="userPlay">The play the user submitted, as entered.</param>
        /// <exception cref="ArgumentNullException"><paramref name="submission"/> is null.</exception>
        internal Play(PlaySubmission submission, BgDataTypes_Lib.Play userPlay)
        {
            ArgumentNullException.ThrowIfNull(submission);
            Submission = submission;
            UserPlay = userPlay;
        }

        /// <summary>
        /// The producer's scored outcome, whole: <see cref="PlaySubmissionKind.Scored"/>
        /// with its <see cref="SubmittedPlay"/> (the error and the verdict,
        /// derived together), <see cref="PlaySubmissionKind.NotScored"/> — a
        /// candidate the ranking does not score, which SPEC-scoring.md §2a makes
        /// a skip — or <see cref="PlaySubmissionKind.OffList"/>, a play that is
        /// no candidate. Read by its case (<see cref="PlaySubmission.Kind"/>,
        /// <see cref="PlaySubmission.TryGetScored"/>, <see cref="PlaySubmission.Match{TResult}"/>),
        /// never compared.
        /// </summary>
        public PlaySubmission Submission { get; }

        /// <summary>
        /// The play the user submitted, as entered — what the off-list verdict
        /// names (halheinrich/backgammon#274), spelled by the producer's one
        /// notation (<see cref="BgDataTypes_Lib.Play.ToNotation"/>). For a
        /// scored play it is also <see cref="SubmittedPlay.UserPlay"/>; the
        /// other two cases carry no play of their own, which is why the review
        /// holds it.
        /// </summary>
        public BgDataTypes_Lib.Play UserPlay { get; }

        /// <summary>
        /// The candidate the user's play is, in the record's stored order — the
        /// solution diagram's † mark: the scored candidate, or the candidate
        /// the ranking does not score; <see langword="null"/> for an off-list
        /// play, which is no row of the list and so gets no mark.
        /// </summary>
        public int? CandidateIndex => Submission.Match<int?>(
            scored: submitted => submitted.MatchedCandidateIndex,
            notScored: index => index,
            offList: () => null);
    }

    /// <summary>
    /// A submitted cube answer — one of the four (<see cref="CubeAnswer"/>,
    /// SPEC-scoring.md §3 as amended on halheinrich/backgammon#326) — scored at
    /// the decision it answers, together with that decision.
    ///
    /// <para>
    /// <b>It scores, so the decision it keeps is the one scored at.</b> The
    /// constructor is the one place in the app a cube answer is scored: it
    /// hands the answer and the decision to the producer
    /// (<see cref="SubmittedCubeAnswer.Score"/>) and keeps the result and the
    /// decision side by side. The review names the answer, and lists the
    /// best answers, from <see cref="Decision"/> — a label is the decision's
    /// reading of an answer (<see cref="BackgammonDiagram_Lib.CubeLabels"/>),
    /// and the best answers are the decision's
    /// (<see cref="CubeDecision.ZeroCostAnswers"/>) — so no caller can pair a
    /// scored answer with another record's labels or Best list.
    /// </para>
    /// </summary>
    internal sealed class Cube : ProblemReview
    {
        /// <summary>
        /// Score <paramref name="answer"/> at <paramref name="decision"/> and
        /// keep both.
        /// </summary>
        /// <param name="answer">The answer the user submitted.</param>
        /// <param name="decision">The cube decision on screen, which the answer answers.</param>
        /// <exception cref="ArgumentNullException"><paramref name="decision"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="answer"/> is not one of the four answers (the producer's refusal).
        /// </exception>
        internal Cube(CubeAnswer answer, CubeDecision decision)
        {
            Submission = SubmittedCubeAnswer.Score(answer, decision);
            Decision = decision;
        }

        /// <summary>
        /// The producer's scored answer, whole: the answer, its cost in its two
        /// parts, and whether the whole answer and each part is correct — all
        /// derived together from <see cref="Decision"/>, so a review can never
        /// state a result that disagrees with the answer it describes. For a
        /// practice submission it exists to be shown and is recorded nowhere;
        /// for the answer of record it is the same instance the run keeps.
        /// </summary>
        public SubmittedCubeAnswer Submission { get; }

        /// <summary>
        /// The decision <see cref="Submission"/> was scored at: what the
        /// verdict reads the answer's label and the Best list from.
        /// </summary>
        public CubeDecision Decision { get; }
    }
}
