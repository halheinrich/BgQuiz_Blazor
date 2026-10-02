namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;
using BgGame_Lib;
using BgMoveGen;
using XgFilter_Lib.Filtering;

/// <summary>
/// The quiz's orchestrator, one per app. It owns what a run needs from outside
/// itself — the active <see cref="IProblemSetSource"/> and its one live
/// enumerator, the busy gate, the lifetime-stats sink — and holds the current
/// <see cref="QuizRun"/>, to which everything about the run itself is
/// delegated: the problems presented, the problem on screen, what is of record
/// for each, the totals and the ranking. Pages observe state via
/// <see cref="StateChanged"/> and drive transitions via
/// <see cref="StartAsync"/> / <see cref="SubmitPlay"/> /
/// <see cref="SubmitCubeAnswer"/> / <see cref="RedoAsync"/> /
/// <see cref="ContinueAsync"/> / <see cref="SkipCurrentAsync"/> /
/// <see cref="EndQuizAsync"/> / <see cref="RestartAsync"/>.
///
/// <para>
/// <b>The run is the model; this is what drives it.</b>
/// <c>SPEC-quiz-history.md</c> (halheinrich/backgammon#8) is the model of a
/// run, and <see cref="QuizRun"/> implements the whole of it as pure, immutable
/// state — moving back and forth through the presented problems included.
/// Every property here that describes the run reads it off the current run and
/// keeps no copy, and every transition here replaces the run with the one the
/// run's own transition returns. What this type adds is the part a pure model
/// cannot do: draw the next problem from the source, decide which drawn
/// positions are shown, take each board's side roll, fold answers into the
/// lifetime record, and refuse a gesture that arrives mid-transition.
/// <b>It wires the part of the model today's page uses</b> — a run that only
/// moves forward, with Redo as its one return to a decision — so the cursor
/// here is always on the frontier; the navigation controls are
/// <c>SPEC-quiz-history.md</c> §2's and arrive with their own leg.
/// </para>
///
/// <para>
/// <b>Three-state per-problem flow.</b> Each problem moves through
/// <i>answering</i> → <i>review</i> → <i>advance</i>. Submit
/// (<see cref="SubmitPlay"/> / <see cref="SubmitCubeAnswer"/>) scores the answer
/// and sets <see cref="Review"/> without advancing — the page flips to a static
/// solution view. <see cref="ContinueAsync"/> then moves on from the problem
/// and pulls the next one. Skip (<see cref="SkipCurrentAsync"/>) bypasses
/// review and advances immediately. The split lets the page show the filled
/// analysis panel (the same view the PPTX exporter renders in
/// <c>DiagramMode.Solution</c>) before moving on. <see cref="RedoAsync"/> is the
/// one path that moves <i>backward</i> — from review back to answering on the
/// same problem — re-opening the problem for practice instead of advancing
/// past it. <see cref="EndQuizAsync"/> is the one path that leaves the flow
/// altogether: it finishes the run where it stands, at the user's request.
/// </para>
///
/// <para>
/// <b>The answer of record, and the displayed review.</b> The first submission
/// against a problem is its answer of record, and every later one is practice:
/// scored and shown, and recorded nowhere. The run holds that record — each
/// presented problem's <see cref="ProblemDisposition"/> — apart from the
/// displayed <see cref="Review"/>, so after a practice cycle the two differ,
/// and it is the record that <see cref="Score"/> reads and that folds. The
/// rules are SPEC-scoring.md §2 and SPEC-quiz-history.md §1 and §3 — read them
/// there, not from this summary.
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
/// <b>One quiz, one ranking</b> (SPEC-scoring.md §2a). Which checker play is
/// best — and so every play's error, whether a play is scored at all, the
/// problem filter's "erred by more than x", and the solution's order and rank
/// numbers — is a ranking's. The caller hands the user's setting in at
/// <see cref="StartAsync"/> / <see cref="RestartAsync"/>, which begin a new
/// run under it; the run owns it from then on and no transition changes it
/// (<see cref="QuizRun.Ranking"/>, read here as <see cref="Ranking"/>). The
/// pool is filtered under it, every play is scored under it, and the pages draw
/// the answering board, the entry and the solution with it. No producer's
/// default stands in anywhere, and a setting changed mid-run reaches the next
/// run rather than splitting this one between two rankings.
/// </para>
///
/// <para>
/// Decision-type policy: the user's <see cref="FilterConfig.DecisionType"/>
/// choice governs which decisions the quiz admits — checker plays, cube
/// decisions, or both. The controller adds no decision-type filter of its
/// own; both checker plays (scored via <see cref="SubmitPlay"/>) and
/// cube decisions (scored via <see cref="SubmitCubeAnswer"/>) flow when
/// the user's filter admits them.
/// </para>
///
/// <para>
/// Lifetime stats: the controller drives the injected
/// <see cref="IProblemStatsSink"/> at exactly two points — the context bind
/// in <see cref="ResetAndAdvanceAsync"/> (every Start/Restart) and the
/// per-answer fold as the run advances past a problem
/// (<see cref="ContinueAsync"/>, <see cref="SkipCurrentAsync"/> and
/// <see cref="EndQuizAsync"/>, through one shared
/// <see cref="FoldAnswerOfRecordAsync"/>). The run holds the record; folding
/// it is this type's, since the sink is outside the run. The
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
/// <see cref="SubmitCubeAnswer"/> / <see cref="RedoAsync"/>) cannot overlap
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

    /// <summary>
    /// The current run, or null before the first Start. It is immutable, so
    /// every transition below replaces it with the run its own transition
    /// returns, and a Start or Restart that is refused — or that fails before
    /// its new run is in place — leaves the one here, its ranking and its
    /// record included, exactly as it was. Everything this type reports about
    /// the run is read off it on each call; nothing about the run is kept
    /// beside it.
    /// </summary>
    private QuizRun? _run;

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

    /// <summary>
    /// The decision currently being shown to the user — the problem under the
    /// run's cursor (<see cref="QuizRun.Cursor"/>); null before start, until
    /// the first problem is presented, and after finish.
    /// </summary>
    public BgDecisionData? Current => _run?.Cursor?.Problem;

    /// <summary>
    /// The scored outcome of the last submission against <see cref="Current"/>
    /// — the <i>displayed review</i>, the run's (<see cref="QuizRun.Review"/>).
    /// Set by Submit and gone once the run leaves the problem or returns to its
    /// decision (<see cref="ContinueAsync"/> / <see cref="RedoAsync"/>, and on
    /// start / restart). Non-null marks the <i>review</i> state:
    /// <see cref="Current"/> still points at the answered problem, and the
    /// page shows the solution view rather than the entry form. Null in the
    /// <i>answering</i> state and after finish.
    ///
    /// <para>
    /// <b>Displayed, not necessarily of record.</b> A practice submission gets
    /// the normal review — seeing how the retry scored is the point of the
    /// gesture — and says so through
    /// <see cref="ProblemReview.IsPractice"/>. What counts, and what folds, is
    /// the answer of record, which the run holds as the problem's disposition
    /// (SPEC-scoring.md §2). Nothing outside may read this property as "the
    /// answer".
    /// </para>
    /// </summary>
    public ProblemReview? Review => _run?.Review;

    /// <summary>
    /// The coin flip taken for the problem on screen, offered to the
    /// presentation layer as the per-problem home-board side. False while no
    /// problem is on screen, where it carries no meaning: which side is
    /// "default" is the settings service's decision, not this type's.
    ///
    /// <para>
    /// <b>Rolled here, kept by the run.</b> <see cref="PresentNextAsync"/>
    /// takes one roll for each problem it presents — unconditionally, so this
    /// type needs no knowledge of whether the user asked for randomization, and
    /// only for a problem the user is shown, so an auto-skipped no-choice
    /// position takes no roll, exactly as it takes no board — and hands it to
    /// the run, which holds it with the problem for the life of the run
    /// (<see cref="PresentedProblem.RandomHomeBoardOnRight"/>;
    /// SPEC-quiz-history.md §5). The run rolls nothing itself. Whether the
    /// value is used is settings policy, composed in exactly one place outside
    /// both (<c>QuizSettings.EffectiveHomeBoardOnRight</c>).
    /// </para>
    ///
    /// <para>
    /// <b>One problem, one roll.</b> The roll is the same while the problem is
    /// being answered, in its solution review, and after
    /// <see cref="RedoAsync"/> returns to its decision, so the board does not
    /// flip under the user of its own accord. The side actually drawn is the
    /// roll plus the current setting, so it can change if the user changes the
    /// setting — deliberately (SPEC-quiz-history.md §5).
    /// </para>
    ///
    /// <para>
    /// Presentation state, never persisted: "the same position looks different
    /// next time" is the whole point. The roll is unseeded, per the
    /// <c>Program.cs</c> shuffle rationale — reproducibility is a test-only
    /// concern.
    /// </para>
    /// </summary>
    public bool RandomHomeBoardOnRight => _run?.Cursor?.RandomHomeBoardOnRight ?? false;

    /// <summary>
    /// The run's ranking (<see cref="QuizRun.Ranking"/>) — the one it was
    /// started or restarted with, which the run owns and no transition changes
    /// (SPEC-scoring.md §2a; SPEC-quiz-history.md §7; see the class docs' "one
    /// quiz, one ranking"). Every ranking-dependent operation of the run reads
    /// it: the pool's filter, scoring, and the pages' diagrams and play entry.
    /// A refused Start or Restart begins no run, so the run under way keeps
    /// its own.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No quiz has been started, so there is no run and no ranking — read it
    /// only while <see cref="HasStarted"/>. No page can: each shows a problem
    /// only once a quiz has started.
    /// </exception>
    public PlayRanking Ranking => _run?.Ranking ?? throw new InvalidOperationException(
        "No quiz has been started, so there is no quiz ranking.");

    /// <summary>
    /// The session score, derived by the run from its answers of record
    /// (<see cref="QuizRun.Score"/>) on each read; empty before start. A new
    /// run — <see cref="StartAsync"/> / <see cref="RestartAsync"/> — starts
    /// from nothing.
    /// </summary>
    public QuizScore Score => _run?.Score ?? QuizScore.Empty;

    /// <summary>
    /// True once the run has ended (<see cref="QuizRun.IsEnded"/>): the source
    /// was fully consumed, or the user ended the quiz.
    /// </summary>
    public bool IsFinished => _run is { IsEnded: true };

    /// <summary>
    /// Count of user-driven non-scoring outcomes, derived by the run on each
    /// read (<see cref="QuizRun.SkippedCount"/>, which owns the rule;
    /// SPEC-quiz-history.md §5): the skips of record — off-list submissions,
    /// plays the run's ranking does not score (SPEC-scoring.md §2a: "a skip of
    /// record that folds nothing"), and every problem still unanswered when the
    /// quiz finished — plus the problems the Skip button moved on from, which
    /// stay unresolved behind the frontier. Auto-skipped no-choice positions
    /// (the user never saw them) are excluded — see
    /// <see cref="HasNoPlayChoice"/>.
    ///
    /// <para>
    /// <b>A Skip counts when the next problem lands</b>, not at the press: the
    /// problem is deferred, not completed, and it joins the count once another
    /// problem is presented beyond it — or, if the source has none, when the
    /// run finishes and converts it. While the draw is pending the count is
    /// unchanged.
    /// </para>
    ///
    /// <para>
    /// A skip of record stands: <see cref="RedoAsync"/> after an unscored
    /// submission leaves it counted (SPEC-scoring.md §2). A deferred problem's
    /// count is provisional — a live answer on returning to it would take it
    /// out of this count and into <see cref="Score"/> — but nothing this type
    /// wires today moves the cursor back, so here the count only ever rises
    /// within a run.
    /// </para>
    /// </summary>
    public int SkippedCount => _run?.SkippedCount ?? 0;

    /// <summary>True when a quiz is in progress (active or finished): there is a run.</summary>
    public bool HasStarted => _run is not null;

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
    /// the pages' shortfall and composed-to-zero notices render from it, and
    /// the Quiz page keys its capless-vs-length-bound notice framing on the
    /// composition's own <see cref="MixComposition.HasRequestedLength"/> —
    /// the producer records that split precisely so no consumer re-derives it
    /// (a local duplicate lived here until halheinrich/backgammon#12's
    /// recording landed). A refused Start/Restart replaces no active-run
    /// state, this reference included, so a running quiz keeps its telemetry
    /// — and its notice framing — behind a refusal.
    /// </summary>
    public MixComposition? LastComposition => _mixedSource?.LastComposition;

    /// <summary>
    /// The N of "Problem N of M": the 1-based stream slot of
    /// <see cref="Current"/>, read off the run's cursor
    /// (<see cref="PresentedProblem.StreamSlot"/>, which owns the convention —
    /// consumed stream slots, auto-skipped no-choice positions included, so an
    /// auto-skip shows as a gap and N never exceeds <see cref="ProblemCount"/>).
    /// Zero while no problem is on screen: before the first is presented, and
    /// after finish. Untouched by Redo (same problem).
    /// </summary>
    public int ProblemNumber => _run?.Cursor?.StreamSlot ?? 0;

    /// <summary>
    /// The M of "Problem N of M": the total number of slots in the run's
    /// problem stream, once established and held by the run
    /// (<see cref="QuizRun.ProblemCount"/>). Null before start, and for as
    /// long as no total is known — a passthrough source that streams without a
    /// count never supplies one, and the page then shows N alone.
    ///
    /// <para>
    /// <b>This type supplies it; the run never asks the source</b>
    /// (SPEC-quiz-history.md §5). A source that declares its size
    /// (<see cref="IProblemSetSource.Count"/>) is read when the run begins; a
    /// weighted quiz learns its size by composing, so its
    /// <see cref="MixComposition.DrawnCount"/> is handed over once the first
    /// draw has produced it. Either way it counts the stream, auto-skipped
    /// no-choice positions included — the convention it shares with
    /// <see cref="ProblemNumber"/>.
    /// </para>
    /// </summary>
    public int? ProblemCount => _run?.ProblemCount;

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
    /// <paramref name="mix"/>, under <paramref name="ranking"/>. Materializes
    /// the user's <see cref="FilterConfig"/> into a <see cref="DecisionFilterSet"/>
    /// owned entirely by this controller, begins a new <see cref="QuizRun"/> —
    /// nothing presented, nothing of record, so an empty score and no skips —
    /// and advances to the first problem that offers a play choice.
    ///
    /// <para>
    /// <b>The ranking is the new run's, for its whole life</b>
    /// (<see cref="Ranking"/>): the pool is filtered under it and every play is
    /// scored under it. It is required rather than defaulted, so a caller
    /// holding the user's setting cannot fall back to the producers' default by
    /// omission.
    /// </para>
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
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ranking"/> is not a defined ranking.</exception>
    public async Task<QuizStartOutcome> StartAsync(
        FilterConfig userConfig, QuizMix mix, PlayRanking ranking, bool ignoreMix = false)
    {
        ArgumentNullException.ThrowIfNull(userConfig);
        ArgumentNullException.ThrowIfNull(mix);
        RefuseUndefined(ranking);

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
            return await ResetAndAdvanceAsync(pipeline, ranking, mix, ignoreMix);
        }
        finally
        {
            EndTransition();
        }
    }

    /// <summary>
    /// Summarize the decisions in the picked corpus that match
    /// <paramref name="userConfig"/> under <paramref name="ranking"/>, bucketed
    /// by the kind of answer each one calls for — the pre-Start affordance that
    /// tells the user what their filters selected, and what that selection is
    /// made of. Builds the same controller-owned pipeline
    /// <see cref="StartAsync"/> would (<see cref="FilterConfig.Build"/>) and
    /// folds a source from the injected <see cref="ProblemSetSourceFactory"/>
    /// over a <b>throwaway</b> enumerator.
    ///
    /// <para>
    /// <b>The ranking is the one a Start would take</b>: the caller passes the
    /// user's setting, as it will at Start, because "erred by more than x" is
    /// read under a ranking and a count under another would describe a pool the
    /// quiz does not draw. It touches no run, so it changes no
    /// <see cref="Ranking"/>.
    /// </para>
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
    /// local — the shared <c>_enumerator</c> and the current run are never read
    /// or written — so a count is safe to run against a controller with a quiz
    /// already in progress. It deliberately does <b>not</b> take the transition gate: it
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
    /// counted, positions offering no play choice included — those auto-skip at
    /// quiz time (<see cref="HasNoPlayChoice"/>), so the numbers are "decisions
    /// that match", not "problems you'll be shown". Not a rounding error: about
    /// one checker decision in eleven is forced or a pass across the umbrella's
    /// own corpus, so the two numbers genuinely differ. An active mix composes from
    /// this pool at Start, so this is the pre-mix pool. The passed
    /// <see cref="QuizMix.Empty"/> keeps the factory from wiring a composition
    /// layer for the throwaway pass; the controller's own composition wiring is
    /// unaffected.
    /// </para>
    ///
    /// <para>
    /// <b>Classification is the producer's.</b> Each decision is folded via
    /// <see cref="AnswerTypeDistribution.Add"/>, which matches on the record's
    /// kind and keys a cube decision by its truth, the analysis's best answer;
    /// nothing here re-derives an answer type from equities.
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
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ranking"/> is not a defined ranking.</exception>
    public async Task<MatchSummary> SummarizeMatchesAsync(FilterConfig userConfig, PlayRanking ranking)
    {
        ArgumentNullException.ThrowIfNull(userConfig);
        RefuseUndefined(ranking);

        var pipeline = userConfig.Build();
        var composed = _sourceFactory(pipeline, ranking, QuizMix.Empty);

        var distribution = AnswerTypeDistribution.Empty;
        await foreach (var decision in composed.Source.EnumerateAsync())
            distribution = distribution.Add(decision);
        return new MatchSummary(distribution, composed.GetDuplicatesCollapsed());
    }

    /// <summary>
    /// Score the user's <paramref name="play"/> against <see cref="Current"/>'s
    /// candidate list and enter the <i>review</i> state — set
    /// <see cref="Review"/> and fire <see cref="StateChanged"/> without
    /// advancing. <see cref="ContinueAsync"/> moves to the next problem.
    ///
    /// <para>
    /// <b>The run scores it and files it</b> (<see cref="QuizRun.SubmitPlay"/>,
    /// which owns both rules). The first submission against a problem is its
    /// answer of record (SPEC-scoring.md §2): it alone reaches
    /// <see cref="Score"/> / <see cref="SkippedCount"/> and the lifetime fold.
    /// A submission made after <see cref="RedoAsync"/> re-opened the problem is
    /// practice — scored the same way and reviewed the same way, so the user
    /// sees how the retry did, and recorded nowhere.
    /// </para>
    ///
    /// <para>
    /// <b>Three outcomes, two of them skips.</b> Scoring is the producer's
    /// (<see cref="PlaySubmission.Score"/>), under the run's ranking. A scored
    /// play is the answer of record: it counts in <see cref="Score"/>, and
    /// folds as the run advances. A play the ranking does not score — under
    /// depth first, a candidate analyzed less deeply than the best that rated
    /// higher — and an off-list play, one no candidate is, are each a skip of
    /// record that folds nothing (SPEC-scoring.md §2 and §2a):
    /// <see cref="SkippedCount"/> counts it, and a redo after one leaves it
    /// standing. Every outcome still produces a <see cref="Review"/> carrying
    /// the producer's outcome whole and the play as entered, so the user sees
    /// the solution, what the verdict was, and — off the list — which play the
    /// app read (halheinrich/backgammon#274).
    /// </para>
    ///
    /// <para>
    /// No-op when <see cref="Current"/> is null, <see cref="IsFinished"/>, or
    /// already in the review state (<see cref="Review"/> set — Continue first).
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Current"/> is a cube decision, which is answered with
    /// <see cref="SubmitCubeAnswer"/> — a caller bug, since the page routes each
    /// kind to its own answer instrument.
    /// </exception>
    public void SubmitPlay(Play play)
    {
        // The IsBusy guard closes the window a pending Continue/Skip opens:
        // mid-advance no review is showing and Current still points at the
        // outgoing problem, so the run would take a submission there. Behind a
        // pending Skip that problem is still unresolved, so the submission
        // would be live: an answer of record on a problem the user has moved
        // on from, written after this advance's fold point and so never
        // folded. Behind a pending Continue it would be practice, and the
        // advance would land over its review.
        if (IsBusy || _run is not { IsAnswering: true } run) return;

        _run = run.SubmitPlay(play);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Score the user's cube <paramref name="answer"/> at
    /// <see cref="Current"/>'s cube decision and enter the <i>review</i> state — set
    /// <see cref="Review"/> and fire <see cref="StateChanged"/> without
    /// advancing. <see cref="ContinueAsync"/> moves to the next problem.
    ///
    /// <para>
    /// The run scores it and files it (<see cref="QuizRun.SubmitCubeAnswer"/>):
    /// of record only the first time, exactly as <see cref="SubmitPlay"/>
    /// describes (SPEC-scoring.md §2); a post-redo submission is practice, and
    /// the scoring below is what both get.
    /// </para>
    ///
    /// <para>
    /// A cube answer is one of four (<see cref="CubeAnswer"/>), and its cost
    /// and correctness are SPEC-scoring.md §3's — read the rule there. The
    /// scoring is the producer's, in one call,
    /// <see cref="SubmittedCubeAnswer.Score"/>, at the decision on screen: it
    /// reads the key, the truth and the answer's cost off that one record and
    /// derives whether the answer is correct from the cost. Nothing in this app
    /// reads an equity, compares answers, or restates a cost or verdict rule.
    /// Unlike <see cref="SubmitPlay"/> there is no off-list / skip path — every
    /// cube answer is scored. The scored answer and the decision it was scored
    /// at are carried on <see cref="ProblemReview.Cube"/>, which drives the
    /// verdict line.
    /// </para>
    ///
    /// <para>
    /// No-op when <see cref="Current"/> is null, <see cref="IsFinished"/>, or
    /// already in the review state (<see cref="Review"/> set — Continue first).
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Current"/> is a checker-play decision, which is answered with
    /// <see cref="SubmitPlay"/> — a caller bug, since the page routes each kind
    /// to its own answer instrument.
    /// </exception>
    public void SubmitCubeAnswer(CubeAnswer answer)
    {
        // Same IsBusy rationale as SubmitPlay: mid-advance the run would take
        // the submission, so the gate is the guard that actually holds.
        if (IsBusy || _run is not { IsAnswering: true } run) return;

        _run = run.SubmitCubeAnswer(answer);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Re-open the just-reviewed problem for <i>practice</i>: leave review and
    /// return to the <i>answering</i> state on the same <see cref="Current"/>
    /// problem, changing nothing that was recorded (SPEC-scoring.md §2).
    ///
    /// <para>
    /// <b>Only the problem re-opens, never the record.</b> What is of record
    /// for the problem — and so <see cref="Score"/>, <see cref="SkippedCount"/>
    /// and what will fold into the lifetime record — stands exactly as the
    /// first submission left it, a skip included. The submission that follows
    /// is practice: scored and reviewed so the user can see how the retry did,
    /// then discarded as if it never happened. Cycles are unbounded and each is
    /// equally recordless.
    /// </para>
    ///
    /// <para>
    /// So the whole transition is the run's <see cref="QuizRun.Redo"/>: the
    /// review goes and the decision is back on screen. <see cref="Current"/>,
    /// the source enumerator and <see cref="IsFinished"/> are untouched, and so
    /// is the problem's disposition — which, being completed, is what makes the
    /// next submission read as practice.
    /// </para>
    ///
    /// <para>
    /// No-op outside the review state (<see cref="Review"/> null).
    /// </para>
    /// </summary>
    public Task RedoAsync()
    {
        // IsBusy: a Continue suspended in the stats fold still has Review set,
        // so without the gate a Redo there would re-open a problem the run is
        // already leaving — the fold completes, the advance lands, and the user
        // is answering the NEXT problem with no visible break. The gate refuses
        // it; the in-flight transition owns the flow.
        if (IsBusy || _run is not { Review: not null } run) return Task.CompletedTask;

        _run = run.Redo();
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Leave the <i>review</i> state and advance to the next problem, which
    /// discards <see cref="Review"/>. No-op outside the review state (when
    /// <see cref="Review"/> is null). This is the <i>advancing</i> exit from
    /// review — <see cref="RedoAsync"/> is the backward one and
    /// <see cref="EndQuizAsync"/> the terminal one; exhausting the source here
    /// flips <see cref="IsFinished"/>.
    ///
    /// <para>
    /// <b>Lifetime-stats fold point.</b> The problem's <i>answer of record</i>
    /// folds into the <see cref="IProblemStatsSink"/> here, as the run advances
    /// past the problem — the advance-time trigger this build still uses (its
    /// move to the first submission is ruled and pending: SPEC-scoring.md §2).
    /// This is one of three sites (with <see cref="SkipCurrentAsync"/> and
    /// <see cref="EndQuizAsync"/>), all through the one shared
    /// <see cref="FoldAnswerOfRecordAsync"/>. What folds is the answer of
    /// record, never the displayed <see cref="Review"/>: after a practice cycle
    /// those differ, and §2 rules the practice submission discarded. The other
    /// side of the same trigger is that an answer of record the run never
    /// advances past — abandoned in review by a tab close, or by a
    /// Start/Restart that begins a new run without continuing — never folds.
    /// An off-list play, and a play the run's ranking does not score, are of
    /// record as skips and fold nothing (neither yields a submission to fold).
    /// The fold happens before the run moves on, so the final problem's answer
    /// folds before <see cref="IsFinished"/> flips.
    /// </para>
    /// </summary>
    public async Task ContinueAsync()
    {
        if (_run is not { Review: not null }) return;
        if (!await TryBeginTransitionAsync()) return;
        try
        {
            await MoveOnAsync();
        }
        finally
        {
            EndTransition();
        }
    }

    /// <summary>
    /// End the run here, at the user's request, and finish with the score of
    /// what they answered — the quiz-side exit a run that has served its purpose
    /// needs (issue halheinrich/backgammon#57). A real transition, not a navigation: only the
    /// controller can end the run and retire the enumerator,
    /// and doing it anywhere else would leave a live quiz behind a Done page.
    /// No-op before start and after finish, and gated like every other async
    /// transition (an overlapping gesture no-ops rather than queueing).
    ///
    /// <para>
    /// <b>What finishing does to the record is the run's</b>
    /// (<see cref="QuizRun.End"/>; SPEC-quiz-history.md §4), and it is the same
    /// whether the user ends the quiz here or the source runs out: every
    /// problem still unresolved becomes a skip of record. <b>An unanswered
    /// problem is abandoned:</b> whatever the user had entered is discarded and
    /// the problem is counted in <see cref="SkippedCount"/>, as every problem
    /// the Skip button moved on from already is, rather than in a category of
    /// its own — so Done's "problems shown" still counts a problem the user
    /// actually saw. There is no partial-answer path and no new scoring path: a
    /// play half assembled on the board was never a submission, and
    /// <c>BackgammonPlayEntry</c> exposes nothing that would let one be
    /// salvaged — the same producer gap the Quiz page's Undo enablement
    /// records.
    /// </para>
    ///
    /// <para>
    /// <b>An answered problem stands, and folds.</b> Ending on a problem that
    /// holds an answer of record is a forward exit, not an abandonment: the
    /// answer was submitted, counted in <see cref="Score"/>, and read — so it
    /// stays in the partial score and folds into the
    /// <see cref="IProblemStatsSink"/> exactly as <see cref="ContinueAsync"/>
    /// would fold it, through the one shared
    /// <see cref="FoldAnswerOfRecordAsync"/>, and nothing is counted as skipped
    /// on top of it. Both halves key on the <i>record</i>, not on
    /// <see cref="Review"/>: a run ended mid-practice-cycle (redone, not yet
    /// re-answered) is showing no review and has still answered the problem.
    /// That is what keeps the standing invariant true: <i>every answer visible
    /// on Done has reached the lifetime record</i>, which until this method
    /// existed held only because Continue was the sole route there — and which
    /// Done's "nothing here needs saving" line states to the user. No
    /// double-fold hazard rides along: the fold happens once, and an ended run
    /// accepts no further transition.
    /// </para>
    /// </summary>
    public async Task EndQuizAsync()
    {
        if (_run is not { IsEnded: false }) return;
        if (!await TryBeginTransitionAsync()) return;
        try
        {
            // Before the run ends, so that — as on Continue — the review is
            // still on screen while the fold is awaited.
            await FoldAnswerOfRecordAsync();

            _run = _run.End();
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
    /// Move on from the current problem without answering it here. Bypasses
    /// review and advances immediately. No-op outside the <i>answering</i>
    /// state — before start, after finish, or while a <see cref="Review"/> is
    /// showing.
    ///
    /// <para>
    /// <b>Skip completes nothing</b> — the run's rule for moving on
    /// (<see cref="QuizRun.Next"/>; SPEC-quiz-history.md §1, §4). The problem
    /// keeps whatever disposition it has, and the two answering states that
    /// reach this method differ in just that.
    /// </para>
    ///
    /// <para>
    /// <b>On an unanswered problem the Skip defers it.</b> It stays unresolved
    /// while the source is asked for the next problem, and nothing folds. If
    /// one is presented, the deferred problem is then behind the frontier and
    /// <see cref="SkippedCount"/> counts it from that moment — not from the
    /// press, so the count is unchanged while the draw is pending. If the
    /// source has none, the run finishes and finishing converts it to a skip
    /// of record, counted the same. A deferred problem is still open to a live
    /// answer, but nothing wired here goes back to one.
    /// </para>
    ///
    /// <para>
    /// <b>Mid-practice-cycle</b> — <see cref="RedoAsync"/> re-opened an
    /// already-answered problem and the user leaves rather than re-answering —
    /// the problem is completed, so this is the run advancing past it: the
    /// answer of record folds, and no skip is counted on top of it. Counting
    /// one would double-count a problem that was answered, and skipping the
    /// fold would strand an answer that Done still shows — the invariant
    /// <see cref="EndQuizAsync"/> states.
    /// </para>
    /// </summary>
    public async Task SkipCurrentAsync()
    {
        if (_run is not { IsAnswering: true }) return;
        if (!await TryBeginTransitionAsync()) return;
        try
        {
            await MoveOnAsync();
        }
        finally
        {
            EndTransition();
        }
    }

    /// <summary>
    /// Restart the quiz from the beginning of the source using the stored
    /// filter pipeline and mix, under <paramref name="ranking"/>. Always
    /// re-attempts the stored mix unless
    /// <paramref name="ignoreMix"/> — the per-run override for a refused
    /// weighted restart, mirroring <see cref="StartAsync"/> — so the mix
    /// applies again whenever stats allow. With a mix active this is a fresh
    /// composition against the stats document <i>as it stands now</i>, this
    /// quiz's folds included (the provider is resolved per enumeration — the
    /// deliberate Restart-recomposes semantics).
    ///
    /// <para>
    /// <b>The ranking is the caller's, not the stored one.</b> A restart is a new
    /// run, and a run takes the ranking the user's setting names when it begins
    /// — so a setting changed since the last run applies here, as it would to a
    /// Start (SPEC-scoring.md §2a: it applies to what is scored after it is
    /// set). The filter pipeline and the mix are the quiz's stored
    /// configuration and are replayed; the ranking is the user's setting and is
    /// re-read, the way the caller re-reads the mix's visibility for
    /// <paramref name="ignoreMix"/>. The restarted quiz is a new
    /// <see cref="QuizRun"/>; the run it replaces is simply let go.
    /// </para>
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
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ranking"/> is not a defined ranking.</exception>
    public async Task<QuizStartOutcome> RestartAsync(PlayRanking ranking, bool ignoreMix = false)
    {
        // A malformed argument is refused before the gate, as StartAsync
        // refuses a malformed config: it is a caller bug, not an outcome, and
        // nothing may have begun when it surfaces.
        RefuseUndefined(ranking);

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
            return await ResetAndAdvanceAsync(_filterPipeline, ranking, _mix, ignoreMix);
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
    /// every gated transition (<see cref="PresentNextAsync"/> itself fires
    /// nothing; all its callers are gated).
    /// </summary>
    private void EndTransition()
    {
        IsBusy = false;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Refuse a ranking that names none of <see cref="PlayRanking"/>'s members,
    /// at the controller's own door. Every producer operation the ranking
    /// reaches refuses such a value too, but only once the run is under way —
    /// after the stats bind and the committed config — so a start given one
    /// would fail half-reset. Refusing it here, before anything begins, keeps
    /// a malformed argument a caller bug that changes nothing, as a malformed
    /// filter config is.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ranking"/> is not a defined ranking.</exception>
    private static void RefuseUndefined(PlayRanking ranking)
    {
        if (!Enum.IsDefined(ranking))
            throw new ArgumentOutOfRangeException(nameof(ranking), ranking, "Not a defined play ranking.");
    }

    /// <summary>
    /// ▶ as today's page reaches it — Continue from a review, Skip from a
    /// decision: take the run's step to "the next problem"
    /// (<see cref="QuizRun.Next"/>) and, where that step moves on from the
    /// frontier, do the two things only this type can — fold the frontier's
    /// answer of record, and bring the problem the run is now owed.
    ///
    /// <para>
    /// <b>The order is the contract.</b> The step is computed first and taken
    /// second: the run is immutable, so asking it what ▶ comes to changes
    /// nothing, and the fold is awaited while the current run — its review
    /// still on screen — is the one the pages see. That is what keeps a review
    /// up, with its buttons showing busy, for as long as a slow stats write
    /// takes. Then the step is taken, and the source is asked.
    /// </para>
    ///
    /// <para>
    /// Nothing here moves the cursor behind the frontier, so today the step
    /// always moves on from it. The other half of the run's rule — ▶ behind the
    /// frontier goes to the next presented problem, recording and folding
    /// nothing — is honoured all the same, so this method stays right when the
    /// navigation controls arrive. Until they do, nothing can reach that branch
    /// through this type; the rule itself is pinned on the run.
    /// </para>
    /// </summary>
    private async Task MoveOnAsync()
    {
        var moved = _run!.Next(out var bringsNewProblem);
        if (!bringsNewProblem)
        {
            _run = moved;
            return;
        }

        await FoldAnswerOfRecordAsync();
        _run = moved;
        await PresentNextAsync();
    }

    /// <summary>
    /// Fold the frontier's answer of record into the lifetime-stats sink — the
    /// one encoding of "what folds", shared by the three exits that advance the
    /// run past its frontier (<see cref="ContinueAsync"/>,
    /// <see cref="SkipCurrentAsync"/>, <see cref="EndQuizAsync"/>). It reads the
    /// problem's disposition and never <see cref="Review"/>: after a practice
    /// cycle the displayed review is the practice submission's, and
    /// SPEC-scoring.md §2 rules that one discarded. A skip of record — an
    /// off-list play, or a play the run's ranking does not score — carries no
    /// submission, so it folds nothing; an unresolved problem, which is what
    /// the Skip gesture leaves, holds nothing of record; and a run with nothing
    /// presented has no frontier. For all three this is a no-op.
    ///
    /// <para>
    /// It reads the frontier because, with nothing here moving the cursor back,
    /// the frontier is the one problem an answer can be given on and the one
    /// the run advances past. An earlier, deferred problem answered on return
    /// is out of this method's reach by construction; the navigation leg moves
    /// the fold to the first submission before it adds any way back
    /// (SPEC-quiz-history.md §7).
    /// </para>
    /// </summary>
    private async Task FoldAnswerOfRecordAsync()
    {
        if (_run?.Frontier is { } frontier && frontier.Disposition.TryGetAnswer(out var answer))
        {
            await answer.Match(
                play: submission => _statsSink.RecordAsync(submission),
                cube: submission => _statsSink.RecordAsync(submission));
        }
    }

    /// <summary>
    /// The one shared path under Start and Restart: refusal checks, the
    /// lifetime-stats bind, pipeline (re)assembly, the new run, and the
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
    /// enumerator and its run, which is its record, its score, its ranking,
    /// <see cref="Current"/> and <see cref="IsFinished"/> — survives a refused
    /// Start/Restart intact;
    /// the only <see cref="StateChanged"/> firings are the enclosing gate's
    /// two busy flips, which deliver unchanged quiz state. The stage-2
    /// re-bind itself is the one residual side effect (see the Pitfalls in
    /// INSTRUCTIONS.md).
    /// </para>
    ///
    /// <para>
    /// The stored config (<see cref="_filterPipeline"/> / <see cref="_mix"/>)
    /// commits only past the refusal checks, so a refused Start never retargets
    /// what a later Restart re-runs. <b>The new run replaces the old one in a
    /// single assignment, once its source stands</b>: it is begun from
    /// <paramref name="ranking"/> — the setting the caller read — the source is
    /// built under the run's ranking, not the argument's, and only then does
    /// the run become the current one. So there is no moment at which one run
    /// is scored under another's ranking, and nothing of the old run is reset
    /// field by field: the new run starts with nothing presented and nothing of
    /// record, which is what an empty score and no skips are.
    /// </para>
    /// </summary>
    private async Task<QuizStartOutcome> ResetAndAdvanceAsync(
        DecisionFilterSet pipeline, PlayRanking ranking, QuizMix mix, bool ignoreMix)
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

        await DisposeEnumeratorAsync();

        // The new run, from the setting the caller read. From here on the
        // ranking is read off the run, the source's included.
        var run = QuizRun.Begin(ranking);

        var inner = _sourceFactory(pipeline, run.Ranking, effectiveMix).Source;
        // A blank mix wires no composition layer at all — the settled
        // passthrough default. An active mix composes via the producer's
        // decorator; holding the typed reference is what surfaces
        // LastComposition without type-testing.
        _mixedSource = effectiveMix.IsPassthrough
            ? null
            : new MixedProblemSetSource(inner, GetCurrentDocumentOrThrow, effectiveMix, _clock);
        _source = _mixedSource ?? inner;
        _enumerator = _source.EnumerateAsync().GetAsyncEnumerator();

        // A source that declares its size states the run's total now. A
        // composing one declares none — its size is known only once it has
        // composed — and PresentNextAsync hands that over after the first draw.
        _run = _source.Count is { } total ? run.WithProblemCount(total) : run;

        await PresentNextAsync();
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

    /// <summary>
    /// Bring the run the problem it is owed — its first, or the one after a
    /// frontier it has moved on from: draw from the source until a position
    /// offers a play choice and present it, or end the run when the source has
    /// none left. This is the whole of what the run is told about the source:
    /// which problem is shown, how many slots were passed over silently on the
    /// way to it, the side rolled for its board, and the stream's total once
    /// the first draw has established one.
    ///
    /// <para>
    /// Callers that fold do so before calling in. Start and Restart
    /// deliberately do not — they begin a new run instead — which is how an
    /// answer abandoned in review never reaches the lifetime record under the
    /// advance-time trigger.
    /// </para>
    /// </summary>
    private async Task PresentNextAsync()
    {
        if (_enumerator is null || _run is null) return;

        // Stream slots drawn and passed over on the way to the next problem:
        // local to this one advance. The run turns it into the problem's slot
        // number, so no counter of the stream is kept here.
        var silentlySkipped = 0;

        while (true)
        {
            var drew = await _enumerator.MoveNextAsync();

            // A composing source assigns its telemetry before its first yield,
            // so the first draw — even one that finds the composition empty —
            // is what establishes a weighted run's total.
            if (_run.ProblemCount is null && LastComposition is { } composition)
                _run = _run.WithProblemCount(composition.DrawnCount);

            if (!drew)
            {
                // The source has no further problem, so the run finishes — and
                // finishing is what converts a frontier the user skipped, still
                // unresolved, into a skip of record. The rule is the run's.
                _run = _run.End();
                break;
            }

            var next = _enumerator.Current;
            if (HasNoPlayChoice(next))
            {
                // Auto-skip silently — the user never saw this position, so it
                // never enters the run; it still occupied a stream slot.
                silentlySkipped++;
                continue;
            }

            // One roll per problem the user actually sees, taken here and
            // handed over: the run keeps it, and rolls nothing itself. See
            // RandomHomeBoardOnRight.
            _run = _run.Present(next, silentlySkipped, randomHomeBoardOnRight: Random.Shared.Next(2) == 0);
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

    /// <summary>
    /// Whether <paramref name="data"/> offers the user no play choice — the one
    /// rule the advance path auto-skips on. True when the record's board and
    /// dice admit exactly one legal play, in either of its two shapes: the play
    /// moves <i>nothing</i> (a dance or a closed-out bar — the position the app
    /// has always skipped), or it moves something and is simply the only one
    /// (a forced checker play, halheinrich/backgammon#140).
    ///
    /// <para>
    /// <b>The two are one fact, not two rules.</b> A pass is the degenerate
    /// case of "exactly one legal play" — the case where that play is empty.
    /// Either way the position poses no question, so quizzing it presents a
    /// decision the dice never offered; both skip, silently and identically.
    /// </para>
    ///
    /// <para>
    /// <b>Derived from the board and the dice, never from the record's
    /// candidate list.</b> That list is an XG analysis artefact — truncated,
    /// analysis-depth-dependent, and absent altogether on an unanalysed
    /// record — so a one-entry list is no evidence of one legal play.
    /// <see cref="MoveGenerator.GeneratePlays"/> over the position is the only
    /// honest source.
    /// </para>
    ///
    /// <para>
    /// <b>Counted by list length, on the producer's distinctness contract.</b>
    /// <see cref="MoveGenerator.GeneratePlays"/> holds exactly one play per
    /// distinct position a legal play reaches — its own doc states the contract
    /// and says a consumer may read <c>Count == 1</c> as "no choice" — so
    /// <c>legal.Count == 1</c> <i>is</i> the forced test. The list is also
    /// never empty: the no-legal-play sentinel is a one-element list holding
    /// the empty <see cref="Play"/>, so the pass case needs no branch of its
    /// own.
    /// </para>
    ///
    /// <para>
    /// The rule once compared every entry against the first instead, because
    /// the generator emitted a two-die bear-off twice — one play, two
    /// candidates, since a bear-off move encodes as <c>(point, 0)</c> whichever
    /// die paid for it. That was a consumer-side workaround for a producer
    /// defect (halheinrich/backgammon#140's verdict), and it is retired now the
    /// producer is fixed (halheinrich/backgammon#141). Since the count is only
    /// as honest as that contract, <c>GeneratedPlayDistinctnessTests</c> pins it
    /// from this side: a producer regression is caught at the layer where the
    /// miscount would silently happen, not left to be inferred from an
    /// over-quizzed run.
    /// </para>
    ///
    /// <para>
    /// <b>Only a checker-play decision can offer no choice.</b> A cube decision
    /// is always shown; it has no roll to generate plays from, and the kind is
    /// the record's type, so the question is asked of checker plays alone.
    /// </para>
    /// </summary>
    private static bool HasNoPlayChoice(BgDecisionData data) =>
        data is CheckerPlayDecision checkerPlay
        && MoveGenerator.GeneratePlays(
            new BoardState(checkerPlay.Board), checkerPlay.Dice.High, checkerPlay.Dice.Low).Count == 1;
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
/// The <paramref name="ranking"/> is the run's (<see cref="QuizController.Ranking"/>),
/// or for a pre-Start count the one a Start would take: the filters read a
/// player's error under it (SPEC-scoring.md §2a), so the pool a stack yields is
/// the pool under that ranking, and a stack must never pick one of its own.
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
internal delegate ComposedProblemSource ProblemSetSourceFactory(
    DecisionFilterSet filters, PlayRanking ranking, QuizMix mix);

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
