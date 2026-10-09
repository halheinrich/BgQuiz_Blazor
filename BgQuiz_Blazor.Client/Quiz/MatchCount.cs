namespace BgQuiz_Blazor.Client.Quiz;

using Microsoft.Extensions.Logging;

/// <summary>
/// The per-app (Scoped, one-per-tab in WASM) holder of <c>Home</c>'s match
/// count: what the filter in effect matches in the picked selection, under the
/// user's ranking, keyed by exactly those inputs (<see cref="MatchCountInputs"/>;
/// XgFilter_Razor's host contract, rule 4; halheinrich/backgammon#374).
///
/// <para>
/// <b>Why it outlives the page.</b> The count used to be <c>Home</c>'s own
/// field, started by the filter panel's commit event, so it died with the page:
/// leaving <c>Home</c> and coming back showed no count, and with it went the
/// known-zero Start gate the count feeds (reproduced on the v1.12.1 production
/// build, 2026-10-09). The filter setup it describes outlives every page in
/// <c>FilterSetup</c>, so the count does too, here. <c>Home</c> asks for the
/// count of its current inputs whenever they may have moved — on every mount
/// and every filter snapshot — and this holder decides: inputs equal to the
/// ones it holds are <b>reused</b>, settled or still counting; anything else is
/// <b>recounted</b>.
/// </para>
///
/// <para>
/// <b>One request is current.</b> Each recount is a new request, and only the
/// newest may publish: a completion for superseded inputs — success or failure,
/// whether the newer request is still running or has already published —
/// replaces no result and ends no busy state. The identity is a fresh object
/// per request, compared by reference, so no counter can wrap or be reset into
/// matching an old one.
/// </para>
///
/// <para>
/// <b>A failed count is unknown, never zero.</b> The count is advisory: a
/// failure is logged and leaves <see cref="Summary"/> null for its inputs, so
/// the known-zero gate cannot close on it and Start's own validation stays the
/// word on the config. It is settled for those inputs — a remount over them
/// does not retry it, any change to them does — so nothing re-runs a failing
/// parse on every snapshot.
/// </para>
///
/// <para>
/// Not thread-safe: every member is called on the renderer's synchronization
/// context, as every component event and lifecycle method already is.
/// </para>
/// </summary>
internal sealed class MatchCount(QuizController controller, ILogger<MatchCount> logger)
{
    /// <summary>
    /// The request counting <see cref="Inputs"/>, or <see langword="null"/>
    /// when their count has settled. Compared by reference only.
    /// </summary>
    private object? _pending;

    /// <summary>
    /// Raised whenever what this holds changes — a count started, or the
    /// current one settled — so a page on screen re-renders. Subscribers
    /// unsubscribe on dispose.
    /// </summary>
    public event Action? Changed;

    /// <summary>The inputs of the current count, or <see langword="null"/> before the first.</summary>
    public MatchCountInputs? Inputs { get; private set; }

    /// <summary>
    /// What <see cref="Inputs"/> matched, or <see langword="null"/> while it is
    /// counting or when the count failed.
    /// </summary>
    public MatchSummary? Summary { get; private set; }

    /// <summary>Whether the count of <see cref="Inputs"/> is still running.</summary>
    public bool IsCounting => _pending is not null;

    /// <summary>
    /// The settled count of <paramref name="inputs"/>, or <see langword="null"/>
    /// when they are not the current inputs, are still counting, or failed —
    /// the one reading a page renders and gates from, so a count of anything
    /// but what is on screen is never shown.
    /// </summary>
    /// <param name="inputs">The page's current inputs, or <see langword="null"/> when nothing is in effect.</param>
    /// <returns>The summary, or <see langword="null"/>.</returns>
    public MatchSummary? SummaryFor(MatchCountInputs? inputs) =>
        inputs is not null && inputs.Equals(Inputs) ? Summary : null;

    /// <summary>
    /// Make <paramref name="inputs"/> the current count: reuse it when it
    /// already is — its result, or its request still running — and otherwise
    /// count it, superseding any request for other inputs.
    /// </summary>
    /// <param name="inputs">What to count.</param>
    /// <returns>
    /// A task that completes when this call's own request has settled or been
    /// superseded — at once, for a reuse. It never faults for the count: a
    /// failure is logged and leaves the count unknown.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is <see langword="null"/>.</exception>
    public async Task EnsureAsync(MatchCountInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Equals(Inputs)) return;

        var request = new object();
        Inputs = inputs;
        Summary = null;
        _pending = request;
        Changed?.Invoke();

        // Let the busy state paint before the count begins: WebAssembly runs
        // the renderer on this one thread, and the first count after a pick
        // parses the corpus.
        await Task.Yield();

        MatchSummary? result;
        try
        {
            result = await controller.SummarizeMatchesAsync(inputs.NewConfig(), inputs.Ranking);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "The match count for {Inputs} failed; it is shown as unknown.", inputs);
            result = null;
        }

        if (!ReferenceEquals(_pending, request)) return; // superseded — a newer request owns the state

        _pending = null;
        Summary = result;
        Changed?.Invoke();
    }
}
