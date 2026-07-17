namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The scored outcome of a just-submitted problem, held by
/// <see cref="QuizController.Review"/> between Submit and Continue. It carries
/// exactly the values the solution diagram needs to mark the user's answer:
/// for a checker play, the matched candidate index that drives the
/// <c>UserPlayIndex</c> marker; for a cube decision, the two per-half equity
/// losses that drive the renderer's "Actual" banner.
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
    /// <param name="DoublerEquityLoss">Equity loss of the user's doubler action vs. the best (0 if best).</param>
    /// <param name="TakerEquityLoss">Equity loss of the user's taker action vs. the best (0 if best).</param>
    /// <param name="DoublerCorrect">True iff the user's doubler action was best.</param>
    /// <param name="TakerCorrect">True iff the user's taker action was best.</param>
    public sealed record Cube(
        double DoublerEquityLoss,
        double TakerEquityLoss,
        bool DoublerCorrect,
        bool TakerCorrect) : ProblemReview;
}
