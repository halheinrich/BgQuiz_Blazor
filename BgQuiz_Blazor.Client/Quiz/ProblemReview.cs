namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;

/// <summary>
/// The scored outcome of a just-submitted problem — the <i>displayed
/// review</i>, held by <see cref="QuizController.Review"/> between Submit and
/// Continue. It carries exactly what the review surfaces need to mark and
/// name the user's answer: for a checker play, the producer's scored outcome
/// (<see cref="PlaySubmission"/>) and the play the user submitted; for a cube
/// decision, the scored submission itself — the user's claim pair, the derived
/// truth pair, and the two per-half equity losses.
///
/// <para>
/// <b>Displayed, not of record.</b> Every submission produces one of these,
/// including the practice submissions of a redo cycle (SPEC-scoring.md §2:
/// practice still reviews — "discarded" governs the record, not the pixels).
/// What <i>counts</i> is the answer of record, which the controller holds
/// privately and apart from this; <see cref="IsPractice"/> is the one bit of
/// that split this type carries, so a review and its practice status can never
/// be assigned separately and drift.
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
/// <see cref="PlaySubmission"/> / <see cref="SubmittedCubeAction"/>. They are
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
    /// after <see cref="QuizController.RedoAsync"/> re-opened a problem that
    /// already holds an answer of record (SPEC-scoring.md §2). Such a
    /// submission is discarded as if it never happened: no session score, no
    /// history entry, no lifetime fold. It is still scored and shown, because
    /// seeing how the retry scored is the point of the gesture; this flag is
    /// what lets the page say so.
    ///
    /// <para>
    /// <c>init</c>-only and defaulted false: the fact is known exactly where a
    /// review is constructed (the controller's submit paths), and nothing may
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
    /// A submitted cube decision, scored as two independent halves — the
    /// doubler's <i>claim</i> and the taker's response if doubled
    /// (SPEC-scoring.md §3; halheinrich/backgammon#86).
    /// </summary>
    internal sealed class Cube : ProblemReview
    {
        /// <summary>Wrap a scored cube submission.</summary>
        /// <param name="submission">The scored submission, whole.</param>
        /// <exception cref="ArgumentNullException"><paramref name="submission"/> is null.</exception>
        internal Cube(SubmittedCubeAction submission)
        {
            ArgumentNullException.ThrowIfNull(submission);
            Submission = submission;
        }

        /// <summary>
        /// The scored submission, whole: the claim pair the user answered
        /// (<see cref="SubmittedCubeAction.UserDecision"/> — drives the per-half
        /// verdict-line labels, each half named for what was submitted rather
        /// than a generic half-name), the position's derived truth
        /// (<see cref="SubmittedCubeAction.BestDecision"/> — what the verdict
        /// names when a claim is wrong, and what makes the incoherent cell
        /// nameable), the two per-half equity losses the verdict line quotes,
        /// and the per-half correctness the outcome colouring reads — derived
        /// on the record from the two pairs, so a review can never state a
        /// result that disagrees with the answer it describes. For a practice
        /// submission this record exists to be shown and is recorded nowhere;
        /// for the answer of record it is the same instance the controller
        /// keeps.
        /// </summary>
        public SubmittedCubeAction Submission { get; }
    }
}
