namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;

/// <summary>
/// The scored outcome of a just-submitted problem — the <i>displayed
/// review</i>, held by <see cref="QuizController.Review"/> between Submit and
/// Continue. It carries exactly the values the solution diagram needs to mark
/// the user's answer: for a checker play, the matched candidate index that
/// drives the <c>UserPlayIndex</c> marker; for a cube decision, the two
/// per-half equity losses that drive the renderer's "Actual" banner.
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
/// Closed hierarchy — the private constructor permits only the two nested
/// variants (<see cref="Play"/>, <see cref="Cube"/>), mirroring the play /
/// cube split already present in <c>SubmittedPlay</c> / <c>SubmittedCubeAction</c>.
/// Those scored-result types live in <c>BgGame_Lib</c> and already carry these
/// values, but this review type is BgQuiz_Blazor's own per-problem UI state —
/// it does not cross the submodule boundary into <c>BgGame_Lib</c>.
/// </para>
/// </summary>
internal abstract record ProblemReview
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
    /// re-badge a review afterwards. It participates in record equality, so a
    /// practice review never compares equal to the answer of record's.
    /// </para>
    /// </summary>
    public bool IsPractice { get; init; }

    /// <summary>
    /// A submitted checker play, scored against the position's candidate list.
    /// </summary>
    /// <param name="UserPlayIndex">
    /// Index into the decision's <c>Plays</c> of the candidate the user's play
    /// matched, used as the solution diagram's <c>UserPlayIndex</c> marker.
    /// <c>-1</c> for an off-list submission (no marker is drawn).
    /// </param>
    /// <param name="EquityLoss">Equity loss vs. the best candidate (0 if best; 0 when off-list).</param>
    /// <param name="IsCorrect">True iff the user matched a zero-loss best candidate.</param>
    /// <param name="OffList">
    /// True when the user assembled a structurally-legal play that does not
    /// appear in the analyzer's candidate list — counted as a skip, not a
    /// scoring miss (see <see cref="QuizController.SubmitPlay"/>).
    /// </param>
    public sealed record Play(
        int UserPlayIndex,
        double EquityLoss,
        bool IsCorrect,
        bool OffList) : ProblemReview;

    /// <summary>
    /// A submitted cube decision, scored as two independent halves — the
    /// doubler's offer and the taker's response.
    /// </summary>
    /// <param name="Submitted">
    /// The pair the user actually answered. Drives the per-half verdict-line
    /// labels (each half named for the action submitted, not a generic
    /// half-name) — see <c>CubeActionDisplay</c>. The losses / correctness
    /// below remain the diagram-marking and outcome-colouring values.
    /// </param>
    /// <param name="DoublerEquityLoss">Equity loss of the user's doubler action vs. the best (0 if best).</param>
    /// <param name="TakerEquityLoss">Equity loss of the user's taker action vs. the best (0 if best).</param>
    /// <param name="DoublerCorrect">True iff the user's doubler action was best.</param>
    /// <param name="TakerCorrect">True iff the user's taker action was best.</param>
    public sealed record Cube(
        CubeDecisionPair Submitted,
        double DoublerEquityLoss,
        double TakerEquityLoss,
        bool DoublerCorrect,
        bool TakerCorrect) : ProblemReview;
}
