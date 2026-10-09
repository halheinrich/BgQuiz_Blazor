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
/// <b>Read only for the inputs on screen.</b> Activity and result are one
/// reading, and the only reading is for given inputs
/// (<see cref="ReadingFor"/>): a page asks about the inputs it shows, and
/// learns nothing of a request for any others. So a request left running
/// after its inputs went — the pick cleared or replaced, the filter edited
/// out of effect — shows no result and makes no page busy, while it still
/// runs on, so a return to its inputs reuses it. Nothing here answers "is
/// anything counting", because no page decision may depend on that
/// (halheinrich/backgammon#374).
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
/// failure is logged and reads as <see cref="MatchCountReading.Unknown"/> for its inputs, so
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
    /// when their count has settled. Compared by reference only. Non-null
    /// exactly while <see cref="_reading"/> is <see cref="MatchCountReading.Counting"/>.
    /// </summary>
    private object? _pending;

    /// <summary>What is known of <see cref="Inputs"/>: counting, counted, or unknown.</summary>
    private MatchCountReading _reading = MatchCountReading.Unknown;

    /// <summary>
    /// Raised whenever what this holds changes — a count started, or the
    /// current one settled — so a page on screen re-renders. Subscribers
    /// unsubscribe on dispose.
    /// </summary>
    public event Action? Changed;

    /// <summary>The inputs of the current count, or <see langword="null"/> before the first.</summary>
    public MatchCountInputs? Inputs { get; private set; }

    /// <summary>
    /// What is known of <paramref name="inputs"/>: that they are being
    /// counted, what they matched, or — when they are not the inputs being
    /// counted, or their count failed — nothing
    /// (<see cref="MatchCountReading.Unknown"/>). The one reading a page
    /// renders, gates and decides busy from, so neither a result nor activity
    /// for anything but what is on screen ever reaches it.
    /// </summary>
    /// <param name="inputs">The page's current inputs, or <see langword="null"/> when nothing is in effect.</param>
    /// <returns>The reading.</returns>
    public MatchCountReading ReadingFor(MatchCountInputs? inputs) =>
        inputs is not null && inputs.Equals(Inputs) ? _reading : MatchCountReading.Unknown;

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
        _pending = request;
        _reading = MatchCountReading.Counting;
        Changed?.Invoke();

        // Let the page paint the count's state before the count begins — the
        // counting line, and for a pick's first count the busy state over a
        // parse of the corpus: WebAssembly runs the renderer on this one
        // thread, and the work below may not yield to it soon.
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

        if (!ReferenceEquals(_pending, request))
        {
            // Superseded — a newer request owns the state. Said at Debug, as
            // the one trace that this completion happened and was dropped.
            logger.LogDebug("The match count for {Inputs} finished after newer inputs superseded it; it changes nothing.", inputs);
            return;
        }

        _pending = null;
        _reading = result is null ? MatchCountReading.Unknown : MatchCountReading.Counted(result);
        Changed?.Invoke();
    }
}
