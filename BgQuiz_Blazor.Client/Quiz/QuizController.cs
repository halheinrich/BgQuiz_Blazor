namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;
using BgGame_Lib;
using BgMoveGen;
using XgFilter_Lib.Filtering;

/// <summary>
/// Per-app quiz state machine. Owns the active <see cref="IProblemSetSource"/>
/// enumerator, the running <see cref="QuizScore"/>, and the per-problem
/// <see cref="SubmittedPlay"/> / <see cref="SubmittedCubeAction"/> histories.
/// Pages observe state via <see cref="StateChanged"/> and drive transitions via
/// <see cref="StartAsync"/> / <see cref="SubmitPlay"/> /
/// <see cref="SubmitCubeAction"/> / <see cref="RedoAsync"/> /
/// <see cref="ContinueAsync"/> / <see cref="SkipCurrentAsync"/> /
/// <see cref="EndQuizAsync"/> / <see cref="RestartAsync"/>.
///
/// <para>
/// <b>Three-state per-problem flow.</b> Each problem moves through
/// <i>answering</i> → <i>review</i> → <i>advance</i>. Submit
/// (<see cref="SubmitPlay"/> / <see cref="SubmitCubeAction"/>) scores the answer
/// and sets <see cref="Review"/> without advancing — the page flips to a static
/// solution view. <see cref="ContinueAsync"/> then clears <see cref="Review"/>
/// and pulls the next problem. Skip (<see cref="SkipCurrentAsync"/>) bypasses
/// review and advances immediately. The split lets the page show the filled
/// analysis panel (the same view the PPTX exporter renders in
/// <c>DiagramMode.Solution</c>) before moving on. <see cref="RedoAsync"/> is the
/// one path that moves <i>backward</i> — from review back to answering on the
/// same problem — reversing the just-submitted answer instead of advancing past
/// it. <see cref="EndQuizAsync"/> is the one path that leaves the flow
/// altogether: it abandons whatever problem is showing and finishes the run
/// where it stands, at the user's request.
/// </para>
///
/// <para>
/// Lifetime: <b>Scoped</b> — but in the WebAssembly client "scoped" resolves to
/// a single instance per app lifetime (one browser tab / one loaded app), not
/// per Blazor Server circuit. The practical effect: quiz state <i>survives
/// in-app navigation</i> between <c>/</c>, <c>/quiz</c>, and <c>/done</c>, and is
/// reset only by a full page reload (which tears down and re-boots the WASM
/// runtime, constructing a fresh instance). Reload-survival (persistence) is a
/// later phase and out of scope here.
/// </para>
///
/// <para>
/// The <see cref="IProblemSetSource"/> is constructed via an injected
/// <see cref="ProblemSetSourceFactory"/> delegate, registered in the client's
/// <c>Program.cs</c>. The delegate seam keeps the controller agnostic to where
/// problems come from: any source implementation (the in-browser file picker,
/// bundled samples, future curated libraries) plugs in by registering a
/// different factory without controller changes. Tests substitute a fake source
/// the same way.
/// </para>
///
/// <para>
/// Filter ownership: <see cref="StartAsync"/> takes a <see cref="FilterConfig"/>
/// (the wire DTO emitted by <c>FilterPanel</c>) rather than a materialized
/// <see cref="DecisionFilterSet"/>. The controller materializes via
/// <see cref="FilterConfig.Build"/> and owns the resulting set end-to-end —
/// no shared mutable state between page and controller.
/// </para>
///
/// <para>
/// Decision-type policy: the user's <see cref="FilterConfig.DecisionType"/>
/// choice governs which decisions the quiz admits — checker plays, cube
/// decisions, or both. The controller adds no decision-type filter of its
/// own; both checker plays (scored via <see cref="SubmitPlay"/>) and
/// cube decisions (scored via <see cref="SubmitCubeAction"/>) flow when
/// the user's filter admits them.
/// </para>
///
/// <para>
/// Lifetime stats: the controller drives the injected
/// <see cref="IProblemStatsSink"/> at exactly two points — the context bind
/// in <see cref="ResetAndAdvanceAsync"/> (every Start/Restart) and the
/// per-answer fold on the forward exits from review
/// (<see cref="ContinueAsync"/> and <see cref="EndQuizAsync"/>, through one
/// shared <see cref="RecordReviewedSubmissionAsync"/>). The
/// sink never throws for stats trouble, so quiz flow is independent of
/// whether stats are recording.
/// </para>
///
/// <para>
/// <b>Transition gate.</b> The five <i>async</i> transitions —
/// <see cref="StartAsync"/> / <see cref="RestartAsync"/> /
/// <see cref="ContinueAsync"/> / <see cref="SkipCurrentAsync"/> /
/// <see cref="EndQuizAsync"/> — share one busy gate: a second gesture arriving
/// while a transition is in flight <b>no-ops</b> (it does not queue). The controller owns exactly one live
/// enumerator, and an overlapping <c>MoveNextAsync</c> — or a dispose during
/// one — throws on a thread-pool continuation where no page can catch it,
/// terminating the WASM runtime; the per-method state guards
/// (<see cref="Current"/> / <see cref="Review"/> / <see cref="IsFinished"/>)
/// cannot close that window because they read <i>stale</i> state while the
/// first call is suspended mid-await. The gate lives here, not in the pages,
/// so no caller needs to know the enumerator contract to be safe. The
/// synchronous mutators (<see cref="SubmitPlay"/> /
/// <see cref="SubmitCubeAction"/> / <see cref="RedoAsync"/>) cannot overlap
/// an await themselves but <i>can</i> land inside one, so they no-op while
/// <see cref="IsBusy"/> too. See <see cref="IsBusy"/> for observability and
/// the <see cref="StateChanged"/> contract.
/// </para>
/// </summary>
internal sealed class QuizController : IAsyncDisposable
{
    private readonly ProblemSetSourceFactory _sourceFactory;
    private readonly IProblemStatsSink _statsSink;
    private readonly TimeProvider _clock;
    private readonly List<SubmittedPlay> _history = [];
    private readonly List<SubmittedCubeAction> _cubeHistory = [];

    private DecisionFilterSet? _filterPipeline;
    private QuizMix _mix = QuizMix.Empty;
    private IProblemSetSource? _source;
    private MixedProblemSetSource? _mixedSource;
    private IAsyncEnumerator<BgDecisionData>? _enumerator;

    public QuizController(ProblemSetSourceFactory sourceFactory, IProblemStatsSink statsSink, TimeProvider clock)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _statsSink = statsSink ?? throw new ArgumentNullException(nameof(statsSink));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Source name once started; null otherwise.</summary>
    public string? Name => _source?.Name;

    /// <summary>The decision currently being shown to the user; null before start or after finish.</summary>
    public BgDecisionData? Current { get; private set; }

    /// <summary>
    /// The scored outcome of the just-submitted problem, set by Submit and
    /// cleared by <see cref="ContinueAsync"/> (and on start / restart). Non-null
    /// marks the <i>review</i> state: <see cref="Current"/> still points at the
    /// answered problem, and the page shows the solution view rather than the
    /// entry form. Null in the <i>answering</i> state and after finish.
    /// </summary>
    public ProblemReview? Review { get; private set; }

    /// <summary>
    /// A fresh coin flip taken for each problem the user is actually shown,
    /// offered to the presentation layer as the per-problem home-board side.
    ///
    /// <para>
    /// <b>Rolled unconditionally</b>, at the one place <see cref="Current"/> is
    /// assigned — so it reads as per-shown-problem (auto-skipped pass positions
    /// take no roll, exactly as they take no board) and so the controller needs
    /// no knowledge of whether the user asked for randomization at all. Whether
    /// this value is used is settings policy, composed in exactly one place
    /// outside the controller (<c>QuizSettings.EffectiveHomeBoardOnRight</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Held steady for the whole encounter.</b> Nothing but an advance rolls
    /// it, so a problem shows the same side while being answered and in its
    /// solution review, and <see cref="RedoAsync"/> — which clears
    /// <see cref="Review"/> on the same <see cref="Current"/> — cannot flip the
    /// board under the user.
    /// </para>
    ///
    /// <para>
    /// Presentation state, never persisted: "the same position looks different
    /// next time" is the whole point. The roll is unseeded, per the
    /// <c>Program.cs</c> shuffle rationale — reproducibility is a test-only
    /// concern.
    /// </para>
    /// </summary>
    public bool RandomHomeBoardOnRight { get; private set; }

    /// <summary>Cumulative running score. Resets on <see cref="StartAsync"/> / <see cref="RestartAsync"/>.</summary>
    public QuizScore Score { get; private set; } = QuizScore.Empty;

    /// <summary>Per-problem in-list checker-play submission history.</summary>
    public IReadOnlyList<SubmittedPlay> History => _history;

    /// <summary>Per-problem cube-decision submission history.</summary>
    public IReadOnlyList<SubmittedCubeAction> CubeHistory => _cubeHistory;

    /// <summary>True once the underlying source has been fully consumed.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>
    /// Count of user-driven non-scoring outcomes: explicit Skip-button clicks
    /// plus off-list submissions. Auto-skipped pass positions (the user never
    /// saw them) are excluded.
    /// </summary>
    public int SkippedCount { get; private set; }

    /// <summary>True when a quiz is in progress (active or finished).</summary>
    public bool HasStarted => _source is not null;

    /// <summary>
    /// True while an async transition (Start / Restart / Continue / Skip /
    /// End quiz) is in flight. While set, every transition entry point no-ops — see the
    /// class-level transition-gate doc. Pages drive their busy affordances
    /// (progress cursor, disabled controls) from this; <see cref="StateChanged"/>
    /// fires on both flips, and the gate yields once after setting it so the
    /// busy state can render and paint <i>before</i> the transition's churn
    /// begins (the sources' time-budgeted yields keep paints possible during
    /// the churn itself).
    /// </summary>
    public bool IsBusy { get; private set; }

    /// <summary>
    /// The active mix composition's telemetry (requested vs. drawn, overall
    /// and per entry, in declared entry order), or null when no composition
    /// layer is wired — a blank/overridden mix — or before the composing
    /// enumeration begins. Assigned by the producer before the first yield,
    /// so it is readable the moment a weighted quiz shows its first problem;
    /// the pages' shortfall and composed-to-zero notices render from it.
    /// </summary>
    public MixComposition? LastComposition => _mixedSource?.LastComposition;

    /// <summary>
    /// Whether the active run's <i>effective</i> mix binds its percentages to
    /// a requested <see cref="QuizMix.QuizLength"/>. False for a passthrough
    /// run (blank mix, or the per-run ignore-mix override) and for a capless
    /// mix. The Quiz page keys its notice framing on this: per-entry
    /// <see cref="MixCompositionEntry.Requested"/> is a user ask only under a
    /// requested length — capless, it is largest-remainder apportionment of
    /// the pool union, so an entry outdrawn there is composition noise, not a
    /// shortfall, and "requested" framing would misreport it. Committed in
    /// <see cref="ResetAndAdvanceAsync"/> past the refusal checks, so a
    /// refused start leaves it — like all active-run state — untouched.
    /// </summary>
    public bool ActiveMixHasLength { get; private set; }

    /// <summary>
    /// The 1-based position of <see cref="Current"/> within the quiz stream:
    /// how many decisions have been consumed from the source so far,
    /// auto-skipped pass positions included. Zero before the first advance;
    /// reset by Start / Restart; untouched by Redo (same problem).
    ///
    /// <para>
    /// <b>Counts consumed stream slots, not presentations — deliberately.</b>
    /// Every total a page can show against it (<see cref="ProblemCount"/>)
    /// counts the stream — a composition's
    /// <see cref="MixComposition.DrawnCount"/> or a source's
    /// <see cref="IProblemSetSource.Count"/> — and a pass position occupies a
    /// stream slot even though <see cref="AdvanceAsync"/> resolves it without
    /// presenting. Counting consumed slots keeps the two commensurable:
    /// "problem N of M" never exceeds M, and N lands exactly on M when the
    /// stream ends. The accepted trade-off is that an auto-skip shows as a
    /// gap in the presented sequence (problem 3 follows problem 1 when slot 2
    /// was a pass position) — rare, and honest about the slot having been in
    /// the quiz — rather than a presented-only N that ends below M.
    /// </para>
    /// </summary>
    public int ProblemNumber { get; private set; }

    /// <summary>
    /// Total number of problems in the active quiz stream, when knowable:
    /// the composition's <see cref="MixComposition.DrawnCount"/> for a
    /// weighted quiz, the source's declared
    /// <see cref="IProblemSetSource.Count"/> for a passthrough run. Null
    /// before start, or when a passthrough source streams without a count.
    /// Auto-skipped pass positions are included — the stream-slot convention
    /// shared with <see cref="ProblemNumber"/>.
    /// </summary>
    public int? ProblemCount => LastComposition?.DrawnCount ?? _source?.Count;

    /// <summary>
    /// Raised after every state transition so observing pages can re-render.
    /// For the gated async transitions this fires exactly twice — once when
    /// <see cref="IsBusy"/> flips on (before any churn, so busy affordances
    /// render) and once when it flips off with the transition's end state in
    /// place. The synchronous mutators (Submit / Redo) fire once as before. A
    /// refused weighted start therefore fires the two busy flips and nothing
    /// else — quiz state is untouched, so the re-renders are no-ops.
    /// </summary>
    public event Action? StateChanged;

    /// <summary>
    /// Begin a fresh quiz against <paramref name="userConfig"/> and
    /// <paramref name="mix"/>. Materializes the user's <see cref="FilterConfig"/>
    /// into a <see cref="DecisionFilterSet"/> owned entirely by this controller
    /// and advances to the first non-pass problem. Resets score / histories /
    /// skipped-count.
    ///
    /// <para>
    /// <b>Mix ownership mirrors filter ownership.</b> The mix is user config
    /// handed in at Start — never caller-set mutable state — stored alongside
    /// the filter pipeline so <see cref="RestartAsync"/> re-attempts it. A
    /// blank mix (<see cref="QuizMix.Empty"/>) is the inert default: no
    /// composition layer is wired at all.
    /// </para>
    ///
    /// <para>
    /// <b>A weighted start can be refused.</b> A mix with entries composes
    /// against the lifetime-stats document, so it requires a bindable stats
    /// context; without one the start is refused
    /// (<see cref="QuizStartOutcome.MixRequiresStats"/>) rather than silently
    /// run unweighted — see <see cref="ResetAndAdvanceAsync"/> for the
    /// two-stage check and the state guarantees. The caller offers the
    /// explicit escape: <paramref name="ignoreMix"/> runs <i>this one quiz</i>
    /// as passthrough while <paramref name="mix"/> is still stored, so the mix
    /// re-applies on the next Start/Restart that can honor it. The stored mix
    /// is never rewritten by any refusal or override.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="userConfig"/> or <paramref name="mix"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="userConfig"/> contains a malformed value — propagated
    /// from <see cref="FilterConfig.Build"/>.
    /// </exception>
    public async Task<QuizStartOutcome> StartAsync(FilterConfig userConfig, QuizMix mix, bool ignoreMix = false)
    {
        ArgumentNullException.ThrowIfNull(userConfig);
        ArgumentNullException.ThrowIfNull(mix);

        // Build a fresh, controller-owned pipeline from the immutable DTO. The
        // user's DecisionType choice governs decision-type admission; the
        // controller adds no filter of its own. Restart re-uses this pipeline
        // without re-building. Built (and validated) before ResetAndAdvanceAsync
        // commits anything, so a Build failure or a refused start leaves the
        // previous quiz's config — and a later Restart — untouched.
        var pipeline = userConfig.Build();

        if (!await TryBeginTransitionAsync()) return QuizStartOutcome.Busy;
        try
        {
            return await ResetAndAdvanceAsync(pipeline, mix, ignoreMix);
        }
        finally
        {
            EndTransition();
        }
    }

    /// <summary>
    /// Summarize the decisions in the picked corpus that match
    /// <paramref name="userConfig"/>, bucketed by the kind of answer each one
    /// calls for — the pre-Start affordance that tells the user what their
    /// filters selected, and what that selection is made of. Builds the same
    /// controller-owned pipeline <see cref="StartAsync"/> would
    /// (<see cref="FilterConfig.Build"/>) and folds a source from the injected
    /// <see cref="ProblemSetSourceFactory"/> over a <b>throwaway</b> enumerator.
    ///
    /// <para>
    /// <b>The match count is <see cref="AnswerTypeDistribution.Total"/>.</b>
    /// Every <see cref="AnswerTypeDistribution.Add"/> increments exactly one
    /// bucket (producer contract), so the pool's size falls out of the same fold
    /// that classifies it — one enumeration, one encoding of "what matches",
    /// nothing that can drift. There is deliberately no separate count-returning
    /// overload: a second way to ask the question is a second answer waiting to
    /// disagree.
    /// </para>
    ///
    /// <para>
    /// <b>Touches no live quiz state.</b> The source and its enumerator are
    /// local — the shared <c>_enumerator</c>, <see cref="Current"/>,
    /// <see cref="Score"/>, and the histories are never read or written — so a
    /// count is safe to run against a controller with a quiz already in
    /// progress. It deliberately does <b>not</b> take the transition gate: it
    /// owns no shared enumerator to protect (the callers serialize Apply
    /// against Start on their side).
    /// </para>
    ///
    /// <para>
    /// <b>Warms the parse cache.</b> The count is a byproduct of the source's
    /// in-memory match pass. The first count after a pick pays the one-time
    /// corpus parse and populates the shared cache
    /// (<see cref="PickedProblemFolder.ParsedDecisions"/>, via the factory's
    /// <c>CachedProblemSetSource</c>), so the Start that follows reuses it and
    /// is near-instant — the count is not a cost added on top of Start.
    /// </para>
    ///
    /// <para>
    /// <b>Counts matches, not presentations.</b> Every matching decision is
    /// counted, forced-move pass positions included — a few may auto-skip at
    /// quiz time (<see cref="AdvanceAsync"/>), so the numbers are "decisions
    /// that match", not "problems you'll be shown". An active mix composes from
    /// this pool at Start, so this is the pre-mix pool. The passed
    /// <see cref="QuizMix.Empty"/> keeps the factory from wiring a composition
    /// layer for the throwaway pass; the controller's own composition wiring is
    /// unaffected.
    /// </para>
    ///
    /// <para>
    /// <b>Classification is the producer's.</b> Each decision is folded via
    /// <see cref="AnswerTypeDistribution.Add"/>, which keys a cube decision by
    /// its analysis-declared best pair; nothing here re-derives an answer type
    /// from equities. Note the fold takes
    /// <see cref="BgDecisionData.Decision"/> — the composite forwards
    /// <see cref="BgDecisionData.IsCube"/> but not the best-pair halves, so the
    /// inner <see cref="DecisionData"/> is what carries the classification.
    /// </para>
    ///
    /// <para>
    /// <b>It also reports what the pool collapsed.</b> The source stack dedupes
    /// by content identity, so the count is a count of distinct positions and a
    /// user comparing it to their file count sees a gap they cannot account for
    /// (halheinrich/backgammon#104). The composed stack reports how many
    /// matching records it dropped as duplicates, and that magnitude travels
    /// back in the same <see cref="MatchSummary"/> as the distribution it was
    /// measured alongside — read after the drain, since it is telemetry of the
    /// enumeration just completed. This method composes its own stack per call,
    /// which is what keeps that per-instance telemetry out of the way of a live
    /// quiz or a concurrent count.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="userConfig"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="userConfig"/> contains a malformed value — propagated
    /// from <see cref="FilterConfig.Build"/>.
    /// </exception>
    public async Task<MatchSummary> SummarizeMatchesAsync(FilterConfig userConfig)
    {
        ArgumentNullException.ThrowIfNull(userConfig);

        var pipeline = userConfig.Build();
        var composed = _sourceFactory(pipeline, QuizMix.Empty);

        var distribution = AnswerTypeDistribution.Empty;
        await foreach (var decision in composed.Source.EnumerateAsync())
            distribution = distribution.Add(decision.Decision);
        return new MatchSummary(distribution, composed.GetDuplicatesCollapsed());
    }

    /// <summary>
    /// Score the user's <paramref name="play"/> against <see cref="Current"/>'s
    /// candidate list and enter the <i>review</i> state — set
    /// <see cref="Review"/> and fire <see cref="StateChanged"/> without
    /// advancing. <see cref="ContinueAsync"/> moves to the next problem.
    ///
    /// <para>
    /// Matching is by canonical play equality: the submitted play is compared
    /// to each candidate's <see cref="PlayCandidate.Play"/> via
    /// <see cref="Play.Equals(Play)"/> in list order; the first match scores.
    /// Equality is order- and decomposition-insensitive but hit-sensitive, so
    /// a play entered as decomposed hops (e.g. 13/10, 10/8) matches its
    /// combined candidate (13/8) while a play whose intermediate hop hits does
    /// not match a non-hitting candidate.
    /// <see cref="PlayCandidate.EquityLoss"/> <c>== 0.0</c> identifies
    /// best-play candidates (the established convention — multiple may share
    /// zero loss; <see cref="DecisionData.BestPlayIndex"/> is the canonical
    /// representative when one is needed).
    /// </para>
    ///
    /// <para>
    /// Off-list submission (the user assembled a structurally-legal play that
    /// doesn't appear in the analyzer's candidate list) counts as a skip
    /// rather than a scoring miss — there is no equity-loss to record. This
    /// is rare on well-analyzed positions and signals an analysis omission
    /// rather than a user error. It still produces a <see cref="Review"/>
    /// (<see cref="ProblemReview.Play.OffList"/> true, index <c>-1</c>) so the
    /// user sees the best play on the solution diagram.
    /// </para>
    ///
    /// <para>
    /// No-op when <see cref="Current"/> is null, <see cref="IsFinished"/>, or
    /// already in the review state (<see cref="Review"/> set — Continue first).
    /// </para>
    /// </summary>
    public void SubmitPlay(Play play)
    {
        // The IsBusy guard closes the stale window a pending Continue/Skip
        // opens: mid-advance, Review is already null and Current still points
        // at the outgoing problem, so without it a submit would double-score
        // a problem the quiz is moving past.
        if (IsBusy || Current is null || IsFinished || Review is not null) return;

        int? matchedIdx = null;
        var plays = Current.Decision.Plays;
        for (int i = 0; i < plays.Count; i++)
        {
            if (plays[i].Play.Equals(play))
            {
                matchedIdx = i;
                break;
            }
        }

        if (matchedIdx is int idx)
        {
            var candidate = plays[idx];
            var submitted = new SubmittedPlay(
                KeyFor(Current),
                play,
                idx,
                candidate.EquityLoss,
                candidate.EquityLoss == 0.0);
            _history.Add(submitted);
            Score = Score.Plus(submitted);
            Review = new ProblemReview.Play(idx, candidate.EquityLoss, submitted.IsCorrect, OffList: false);
        }
        else
        {
            SkippedCount++;
            Review = new ProblemReview.Play(UserPlayIndex: -1, EquityLoss: 0.0, IsCorrect: false, OffList: true);
        }

        StateChanged?.Invoke();
    }

    /// <summary>
    /// Score the user's cube <paramref name="answer"/> against
    /// <see cref="Current"/>'s analysis and enter the <i>review</i> state — set
    /// <see cref="Review"/> and fire <see cref="StateChanged"/> without
    /// advancing. <see cref="ContinueAsync"/> moves to the next problem.
    ///
    /// <para>
    /// A cube position is two independent atomic decisions — the doubler's
    /// offer choice and the taker's response choice — so this always scores
    /// both halves: the per-half equity loss
    /// (<see cref="DecisionData.DoublerActionError"/> /
    /// <see cref="DecisionData.TakerActionError"/>) and per-half correctness
    /// (against <see cref="DecisionData.BestDoublerAction"/> /
    /// <see cref="DecisionData.BestTakerAction"/>). Unlike
    /// <see cref="SubmitPlay"/> there is no off-list / skip path — every
    /// cube answer is a complete, scorable pair (the doubler and taker button
    /// groups can only yield in-range actions). The two per-half losses are
    /// carried on <see cref="ProblemReview.Cube"/> and drive the solution
    /// diagram's "Actual" banner.
    /// </para>
    ///
    /// <para>
    /// No-op when <see cref="Current"/> is null, <see cref="IsFinished"/>, or
    /// already in the review state (<see cref="Review"/> set — Continue first).
    /// </para>
    /// </summary>
    public void SubmitCubeAction(CubeDecisionPair answer)
    {
        // Same IsBusy rationale as SubmitPlay: mid-advance the state guards
        // read stale-pass, so the gate is the guard that actually holds.
        if (IsBusy || Current is null || IsFinished || Review is not null) return;

        var d = Current.Decision;
        var submitted = new SubmittedCubeAction(
            KeyFor(Current),
            answer,
            d.DoublerActionError(answer.Doubler),
            d.TakerActionError(answer.Taker),
            answer.Doubler == d.BestDoublerAction,
            answer.Taker == d.BestTakerAction);
        _cubeHistory.Add(submitted);
        Score = Score.Plus(submitted);
        Review = new ProblemReview.Cube(
            answer,
            submitted.DoublerEquityLoss,
            submitted.TakerEquityLoss,
            submitted.DoublerCorrect,
            submitted.TakerCorrect);

        StateChanged?.Invoke();
    }

    /// <summary>
    /// The content identity stamped on a submission — the problem's
    /// <see cref="ProblemKey"/>, or <see langword="null"/> on the ratified
    /// no-key rung (SPEC-stats-identity.md §2), where the record's facts are
    /// malformed or degenerate and a derivation would have to guess. Null is
    /// carried, never substituted for: the submission still scores the session
    /// exactly as any other, and only the lifetime record abstains — the
    /// producer's document performs that skip, so nothing here branches on it.
    ///
    /// <para>
    /// The one derivation site in this app, over the producer's single factory:
    /// the two submit paths differ in everything except which problem they
    /// answer, and a key assembled twice is a key that can be assembled two
    /// ways.
    /// </para>
    /// </summary>
    private static ProblemKey? KeyFor(BgDecisionData problem) =>
        ProblemKey.TryDerive(problem, out var key) ? key : null;

    /// <summary>
    /// Reverse the just-submitted answer and return to the <i>answering</i>
    /// state on the same <see cref="Current"/> problem — the inverse of Submit.
    ///
    /// <para>
    /// Removes the just-added entry from <see cref="History"/> (a checker play)
    /// or <see cref="CubeHistory"/> (a cube submission); an off-list play
    /// submission never added a history entry, so that branch instead
    /// decrements <see cref="SkippedCount"/>. <see cref="Score"/> is then
    /// recomputed by refolding <see cref="History"/> and <see cref="CubeHistory"/>
    /// from <see cref="QuizScore.Empty"/> — cheaper than adding a subtract path
    /// to <c>QuizScore</c> / <c>ScoreSegment</c>. Refolding is safe regardless of
    /// how the two histories were interleaved in time: a play only folds into
    /// <c>PlayDecisions</c> and a cube submission only folds into
    /// <c>DoubleDecisions</c> / <c>TakeDecisions</c>, so each segment's
    /// accumulation order matches its own history's list order either way.
    /// </para>
    ///
    /// <para>
    /// <see cref="Current"/>, the source enumerator, and <see cref="IsFinished"/>
    /// are left completely untouched — that is the whole point: the user
    /// re-answers the exact problem just submitted rather than skipping past it.
    /// </para>
    ///
    /// <para>
    /// No-op outside the review state (<see cref="Review"/> null).
    /// </para>
    /// </summary>
    public Task RedoAsync()
    {
        // IsBusy: a Continue suspended in the stats fold still has Review set,
        // so without the gate a Redo there would pop the history entry the
        // fold is recording — an un-poppable inconsistency (the document has
        // no Minus).
        if (IsBusy || Review is null) return Task.CompletedTask;

        switch (Review)
        {
            case ProblemReview.Play { OffList: false }:
                _history.RemoveAt(_history.Count - 1);
                break;
            case ProblemReview.Play { OffList: true }:
                SkippedCount--;
                break;
            case ProblemReview.Cube:
                _cubeHistory.RemoveAt(_cubeHistory.Count - 1);
                break;
        }

        Score = RefoldScore();
        Review = null;
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Leave the <i>review</i> state and advance to the next problem, clearing
    /// <see cref="Review"/>. No-op outside the review state (when
    /// <see cref="Review"/> is null). This is the <i>advancing</i> exit from
    /// review — <see cref="RedoAsync"/> is the backward one and
    /// <see cref="EndQuizAsync"/> the terminal one; exhausting the source here
    /// flips <see cref="IsFinished"/>.
    ///
    /// <para>
    /// <b>Lifetime-stats fold point.</b> The just-reviewed submission folds
    /// into the <see cref="IProblemStatsSink"/> here — on leaving review, not
    /// at Submit — because <see cref="RedoAsync"/> pops the last submission
    /// while <see cref="Review"/> is set and the stats document has no
    /// <c>Minus</c>: an answer is final only once the user moves forward past
    /// it. The flip side is deliberate and must not be "fixed": an answer
    /// abandoned in review (tab close, or a Start/Restart that resets without
    /// Continue) never folds, consistent with Redo's semantics. Off-list
    /// submissions carry no history entry and never fold (producer contract:
    /// skips and off-list plays aren't lifetime submissions); the fold happens
    /// before <see cref="AdvanceAsync"/>, so the final problem's answer folds
    /// before <see cref="IsFinished"/> flips. The fold itself lives in
    /// <see cref="RecordReviewedSubmissionAsync"/>, shared with the other
    /// forward exit, <see cref="EndQuizAsync"/>.
    /// </para>
    /// </summary>
    public async Task ContinueAsync()
    {
        if (Review is null) return;
        if (!await TryBeginTransitionAsync()) return;
        try
        {
            await RecordReviewedSubmissionAsync();

            Review = null;
            await AdvanceAsync();
        }
        finally
        {
            EndTransition();
        }
    }

    /// <summary>
    /// End the run here, at the user's request, and finish with the score of
    /// what they answered — the quiz-side exit a run that has served its purpose
    /// needs (issue #57). A real transition, not a navigation: only the
    /// controller can retire the enumerator and flip <see cref="IsFinished"/>,
    /// and doing it anywhere else would leave a live quiz behind a Done page.
    /// No-op before start and after finish, and gated like every other async
    /// transition (an overlapping gesture no-ops rather than queueing).
    ///
    /// <para>
    /// <b>The current problem is abandoned, not answered.</b> From the
    /// <i>answering</i> state whatever the user had entered is discarded and the
    /// problem records nothing — the same non-scoring outcome an explicit
    /// <see cref="SkipCurrentAsync"/> records, reusing
    /// <see cref="SkippedCount"/> rather than inventing a category for it, so
    /// Done's "problems shown" still counts a problem the user actually saw.
    /// There is no partial-answer path and no new scoring path: a play half
    /// assembled on the board was never a submission, and <c>BackgammonPlayEntry</c>
    /// exposes nothing that would let one be salvaged — the same producer gap the
    /// Quiz page's Undo enablement records.
    /// </para>
    ///
    /// <para>
    /// <b>A reviewed answer stands, and folds.</b> Ending from the <i>review</i>
    /// state is a forward exit, not an abandonment: the answer was submitted,
    /// scored into <see cref="Score"/>, and read — so it stays in the partial
    /// score and folds into the <see cref="IProblemStatsSink"/> exactly as
    /// <see cref="ContinueAsync"/> would fold it, through the one shared
    /// <see cref="RecordReviewedSubmissionAsync"/>. That is what keeps the
    /// standing invariant true: <i>every answer visible on Done has reached the
    /// lifetime record</i>, which until this method existed held only because
    /// Continue was the sole route there — and which Done's "nothing here needs
    /// saving" line states to the user. No double-fold hazard rides along: the
    /// fold happens once, and <see cref="Review"/> is cleared under the same
    /// gate that makes <see cref="RedoAsync"/> unreachable afterwards.
    /// </para>
    /// </summary>
    public async Task EndQuizAsync()
    {
        if (!HasStarted || IsFinished) return;
        if (!await TryBeginTransitionAsync()) return;
        try
        {
            if (Review is not null)
            {
                await RecordReviewedSubmissionAsync();
            }
            else if (Current is not null)
            {
                SkippedCount++;
            }

            Review = null;
            Current = null;
            IsFinished = true;
            // The run is over, so the one live enumerator is released here
            // rather than waiting for the next Start's reset — safe precisely
            // because the gate guarantees no MoveNextAsync is in flight.
            await DisposeEnumeratorAsync();
        }
        finally
        {
            EndTransition();
        }
    }

    /// <summary>
    /// Skip the current problem without recording; advance immediately (no
    /// review). No-op outside the <i>answering</i> state — before start, after
    /// finish, or while a <see cref="Review"/> is showing.
    /// </summary>
    public async Task SkipCurrentAsync()
    {
        if (Current is null || IsFinished || Review is not null) return;
        if (!await TryBeginTransitionAsync()) return;
        try
        {
            SkippedCount++;
            await AdvanceAsync();
        }
        finally
        {
            EndTransition();
        }
    }

    /// <summary>
    /// Restart the quiz from the beginning of the source using the stored
    /// filter pipeline and mix. Always re-attempts the stored mix unless
    /// <paramref name="ignoreMix"/> — the per-run override for a refused
    /// weighted restart, mirroring <see cref="StartAsync"/> — so the mix
    /// applies again whenever stats allow. With a mix active this is a fresh
    /// composition against the stats document <i>as it stands now</i>, this
    /// quiz's folds included (the provider is resolved per enumeration — the
    /// deliberate Restart-recomposes semantics).
    ///
    /// <para>
    /// Restarting a never-started controller <b>throws</b>: there is no prior
    /// run to repeat, so the call is a caller bug, not an outcome — the same
    /// contract class as the producer's null-provider throw
    /// (<see cref="MixedProblemSetSource"/> refuses to compose plausibly over
    /// a wiring bug). A fabricated <see cref="QuizStartOutcome.Started"/>
    /// here would mask a mis-wired future caller; the outcome enum stays
    /// two-membered because refusal outcomes are for <i>reachable</i> states.
    /// No shipped page can hit this (Done requires a started quiz).
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">No quiz has been started.</exception>
    public async Task<QuizStartOutcome> RestartAsync(bool ignoreMix = false)
    {
        // The gate is checked before the never-started throw: a Restart
        // overlapping an in-flight transition is a UI double-gesture (an
        // outcome, no-op'd like every overlap), not the caller bug the throw
        // exists for. The throw sits inside the try so a mis-wired caller
        // still releases the gate.
        if (!await TryBeginTransitionAsync()) return QuizStartOutcome.Busy;
        try
        {
            if (_filterPipeline is null)
                throw new InvalidOperationException(
                    "RestartAsync requires a prior successful StartAsync — no quiz has been started.");
            return await ResetAndAdvanceAsync(_filterPipeline, _mix, ignoreMix);
        }
        finally
        {
            EndTransition();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeEnumeratorAsync();
    }

    // -----------------------------------------------------------------------
    //  Internal
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enter the transition gate, or report it already held. On entry
    /// <see cref="IsBusy"/> flips on, <see cref="StateChanged"/> fires, and
    /// the method yields once — deliberately — so observing pages can render
    /// (and the browser paint) the busy state before the caller's churn
    /// begins. Single-threaded scheduler: the check-and-set runs unbroken
    /// before the yield, so no interleaved caller can slip past it.
    /// </summary>
    private async ValueTask<bool> TryBeginTransitionAsync()
    {
        if (IsBusy) return false;
        IsBusy = true;
        StateChanged?.Invoke();
        await Task.Yield();
        return true;
    }

    /// <summary>
    /// Release the transition gate (always via <c>finally</c>, so a faulted
    /// transition never wedges it) and fire <see cref="StateChanged"/> with
    /// the transition's end state in place — the single completion signal for
    /// every gated transition (<see cref="AdvanceAsync"/> itself no longer
    /// fires; all its callers are gated).
    /// </summary>
    private void EndTransition()
    {
        IsBusy = false;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Fold the submission the current <see cref="Review"/> describes into the
    /// lifetime-stats sink — the one encoding of "which history entry this
    /// review finalizes", shared by the two forward exits from review
    /// (<see cref="ContinueAsync"/> and <see cref="EndQuizAsync"/>). An off-list
    /// play added no history entry and never folds (producer contract: skips and
    /// off-list plays aren't lifetime submissions); outside review there is
    /// nothing to fold and this is a no-op.
    /// </summary>
    private async Task RecordReviewedSubmissionAsync()
    {
        switch (Review)
        {
            case ProblemReview.Play { OffList: false }:
                await _statsSink.RecordAsync(_history[^1]);
                break;
            case ProblemReview.Cube:
                await _statsSink.RecordAsync(_cubeHistory[^1]);
                break;
                // ProblemReview.Play { OffList: true }: no history entry — never folds.
        }
    }

    /// <summary>
    /// Recompute <see cref="Score"/> from scratch by folding <see cref="_history"/>
    /// then <see cref="_cubeHistory"/> into <see cref="QuizScore.Empty"/>. Order
    /// between the two histories doesn't matter: a play only touches
    /// <c>PlayDecisions</c> and a cube submission only touches
    /// <c>DoubleDecisions</c> / <c>TakeDecisions</c>, so each segment's
    /// accumulation order matches its own history's list order regardless of how
    /// the two histories are interleaved here.
    /// </summary>
    private QuizScore RefoldScore()
    {
        var score = QuizScore.Empty;
        foreach (var play in _history) score = score.Plus(play);
        foreach (var cube in _cubeHistory) score = score.Plus(cube);
        return score;
    }

    /// <summary>
    /// The one shared path under Start and Restart: refusal checks, the
    /// lifetime-stats bind, pipeline (re)assembly, state reset, and the
    /// advance to the first showable problem.
    ///
    /// <para>
    /// <b>Two-stage refusal for a weighted start.</b> A mix with entries
    /// composes against the stats document, and composing without one is
    /// banned (the producer's provider contract throws on null by design) —
    /// so no stats means the start is refused, never silently unweighted.
    /// Stage 1 is the side-effect-free shared predicate
    /// (<see cref="IProblemStatsSink.CanWeightMix"/>): a folder that can't
    /// save stats, or has no stats record yet, refuses before anything —
    /// including the stats bind — happens. Stage 2 runs after the bind:
    /// a context that bound without a document (unreadable stats file)
    /// refuses before any quiz state is touched, so the prior quiz — its
    /// enumerator, score, histories, <see cref="Current"/>, and
    /// <see cref="IsFinished"/> — survives a refused Start/Restart intact;
    /// the only <see cref="StateChanged"/> firings are the enclosing gate's
    /// two busy flips, which deliver unchanged quiz state. The stage-2
    /// re-bind itself is the one residual side effect (see the Pitfalls in
    /// INSTRUCTIONS.md).
    /// </para>
    ///
    /// <para>
    /// The stored config (<see cref="_filterPipeline"/> / <see cref="_mix"/>)
    /// commits only past the refusal checks, so a refused Start never
    /// retargets what a later Restart re-runs.
    /// </para>
    /// </summary>
    private async Task<QuizStartOutcome> ResetAndAdvanceAsync(
        DecisionFilterSet pipeline, QuizMix mix, bool ignoreMix)
    {
        // The composition this run actually uses: the override runs one quiz
        // as passthrough while the stored mix stays what the user configured.
        var effectiveMix = ignoreMix ? QuizMix.Empty : mix;

        if (!effectiveMix.IsPassthrough && !_statsSink.CanWeightMix)
            return QuizStartOutcome.MixRequiresStats;

        // Bind the lifetime-stats context for the quiz now starting. This is
        // the one shared path under Start and Restart, so it is exactly where
        // the picked folder promotes to the active stats slot — a mid-quiz
        // Clear or re-pick changes nothing until the next pass through here.
        // Bound before the source is built because the mix decision below
        // needs the freshly bound context.
        await _statsSink.BeginQuizAsync();

        if (!effectiveMix.IsPassthrough && _statsSink.CurrentDocument is null)
            return QuizStartOutcome.MixRequiresStats;

        _filterPipeline = pipeline;
        _mix = mix;
        ActiveMixHasLength = effectiveMix.QuizLength is not null;

        await DisposeEnumeratorAsync();

        var inner = _sourceFactory(pipeline, effectiveMix).Source;
        // A blank mix wires no composition layer at all — the settled
        // passthrough default. An active mix composes via the producer's
        // decorator; holding the typed reference is what surfaces
        // LastComposition without type-testing.
        _mixedSource = effectiveMix.IsPassthrough
            ? null
            : new MixedProblemSetSource(inner, GetCurrentDocumentOrThrow, effectiveMix, _clock);
        _source = _mixedSource ?? inner;
        _enumerator = _source.EnumerateAsync().GetAsyncEnumerator();

        Score = QuizScore.Empty;
        _history.Clear();
        _cubeHistory.Clear();
        SkippedCount = 0;
        ProblemNumber = 0;
        IsFinished = false;
        Current = null;
        Review = null;
        // No problem is showing, so this carries no meaning until the advance
        // below rolls one. Cleared to the CLR default rather than to a side:
        // which side is "default" is the settings service's decision, not this
        // type's, and a run must not inherit the previous run's last roll.
        RandomHomeBoardOnRight = false;

        await AdvanceAsync();
        return QuizStartOutcome.Started;
    }

    /// <summary>
    /// The stats provider handed to <see cref="MixedProblemSetSource"/>,
    /// resolved fresh per enumeration so a Restart recomposes against the
    /// lifetime record as it stands, this session's folds included. The
    /// decorator is only ever wired past the refusal checks, so a null
    /// document here is a wiring bug — the throw mirrors the producer's own
    /// null-provider contract rather than masking the bug as an
    /// all-never-seen quiz.
    /// </summary>
    private ProblemStatsDocument GetCurrentDocumentOrThrow() =>
        _statsSink.CurrentDocument ?? throw new InvalidOperationException(
            "The mix composition layer is wired but the stats sink holds no document — " +
            "a weighted start should have been refused.");

    private async Task AdvanceAsync()
    {
        if (_enumerator is null) return;

        while (true)
        {
            if (!await _enumerator.MoveNextAsync())
            {
                Current = null;
                IsFinished = true;
                break;
            }

            // A stream slot is consumed whether or not it presents — pass
            // positions included, per ProblemNumber's documented convention.
            ProblemNumber++;

            var next = _enumerator.Current;
            if (IsPassPosition(next))
            {
                // Auto-skip silently — the user never saw this position.
                continue;
            }

            Current = next;
            // Beside the assignment, and after the pass-skip continue above:
            // one roll per problem the user actually sees. See the property.
            RandomHomeBoardOnRight = Random.Shared.Next(2) == 0;
            break;
        }
        // No StateChanged here: every caller runs inside the transition gate,
        // whose EndTransition fire delivers the post-advance state.
    }

    private async ValueTask DisposeEnumeratorAsync()
    {
        if (_enumerator is not null)
        {
            await _enumerator.DisposeAsync();
            _enumerator = null;
        }
    }

    private static bool IsPassPosition(BgDecisionData data)
    {
        // Cube decisions are always shown — never auto-skipped. They carry no
        // dice ([0, 0] by the data-layer invariant), which would otherwise hit
        // the no-legal-play sentinel below and silently skip every cube
        // position.
        if (data.Decision.IsCube) return false;

        var board = BoardState.FromMop(data.Position.Mop);
        var dice = data.Decision.Dice;
        var legal = MoveGenerator.GeneratePlays(board, dice[0], dice[1]);
        // BgMoveGen's no-legal-play sentinel: a single Play with zero moves
        // (the dice are forfeited). Empty `legal` is not used.
        return legal.Count == 1 && legal[0].Count == 0;
    }
}

/// <summary>
/// Factory delegate for constructing the active problem source from a
/// user-supplied filter set. The client's <c>Program.cs</c> binds this to the
/// in-browser source for the current run (the picked-files source, or a
/// bundled sample); tests substitute a fake source via the same delegate shape.
///
/// <para>
/// It returns a <see cref="ComposedProblemSource"/> rather than the source
/// itself: a caller needs the stack to enumerate <i>and</i> — for the pre-Start
/// match summary — how many duplicates that stack collapsed, which only the
/// composer knows how to read back out of its own layers. A caller that wants
/// only the stack takes <see cref="ComposedProblemSource.Source"/> and ignores
/// the rest.
/// </para>
///
/// <para>
/// The <paramref name="mix"/> is the run's <i>effective</i> composition config,
/// passed for one reason: shuffle arbitration. An active mix owns presentation
/// order through <see cref="QuizMix.RandomOrder"/>, so the factory must not
/// wrap a shuffle decorator under it — a shuffled inner would silently break
/// the deterministic contract of <c>RandomOrder: false</c> (the composing
/// decorator draws and presents in <i>source</i> order there). The factory
/// never wires the composition layer itself; that stays with the controller.
/// </para>
/// </summary>
internal delegate ComposedProblemSource ProblemSetSourceFactory(DecisionFilterSet filters, QuizMix mix);

/// <summary>
/// The result of <see cref="QuizController.StartAsync"/> /
/// <see cref="QuizController.RestartAsync"/>: whether a quiz actually began.
/// The non-<see cref="Started"/> members are <i>outcomes</i> of reachable
/// states, not failures — exceptions remain the failure channel.
/// </summary>
internal enum QuizStartOutcome
{
    /// <summary>A quiz began (weighted or passthrough) and advanced to its first problem — or finished immediately (the caller's empty-result check still applies).</summary>
    Started,

    /// <summary>
    /// The start was refused: the mix has entries but no lifetime-stats
    /// document is available (unsupported browser, declined permission,
    /// nothing picked, no stats record in the picked folder yet, or an
    /// unreadable stats file). No quiz state changed —
    /// the prior quiz, if any, is untouched. The caller renders an actionable
    /// notice whose escape is the explicit per-run <c>ignoreMix</c> override;
    /// the stored mix itself is never rewritten.
    ///
    /// <para>
    /// <b>A backstop, not a routine outcome</b>, since issue
    /// <c>halheinrich/backgammon#87</c>: the host offers no way to build a mix
    /// where <see cref="IProblemStatsSink.CanWeightMix"/> is false, so a
    /// non-passthrough mix can barely coexist with absent stats. What is left
    /// is the genuinely reachable case this member is kept for — a bind that
    /// fails <i>after</i> the pick looked capable (the file changed, or turned
    /// out unparseable) — plus any future caller that reaches the controller
    /// without the host's gating.
    /// </para>
    /// </summary>
    MixRequiresStats,

    /// <summary>
    /// The call was ignored by the transition gate: another transition was
    /// already in flight (<see cref="QuizController.IsBusy"/>), so nothing
    /// happened and nothing will — overlaps no-op rather than queue. Callers
    /// do nothing with it (no navigation, no notice); the in-flight
    /// transition owns the UI. A reachable outcome — a double-click on
    /// Start/Restart — not a caller bug.
    /// </summary>
    Busy,
}
