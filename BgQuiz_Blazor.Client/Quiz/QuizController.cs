namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;
using BgGame_Lib;
using BgMoveGen;
using Microsoft.Extensions.Logging;
using XgFilter_Lib.Filtering;

/// <summary>
/// The quiz's orchestrator, one per app. It owns what a run needs from outside
/// itself — the run's <see cref="IProblemSetSource"/> and the one stream drawn
/// from it, the busy gate, the lifetime-stats sink — and holds the current
/// <see cref="QuizRun"/>, to which everything about the run itself is
/// delegated: the problems presented, the problem on screen, what is of record
/// for each, the totals and the ranking. Pages observe state via
/// <see cref="StateChanged"/> and drive transitions via
/// <see cref="StartAsync"/> / <see cref="SubmitPlayAsync"/> /
/// <see cref="SubmitCubeAnswerAsync"/> / <see cref="NextAsync"/> /
/// <see cref="GoToFirst"/> / <see cref="GoBack"/> / <see cref="GoToLast"/> /
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
/// lifetime record, and refuse a gesture that arrives mid-transition. It wires
/// the whole model: ▶ (<see cref="NextAsync"/>) and ⏮ ◀ ⏭
/// (<see cref="GoToFirst"/> / <see cref="GoBack"/> / <see cref="GoToLast"/>)
/// are <c>SPEC-quiz-history.md</c> §2's controls, each the run's own step, and
/// §4's transitions are the run's.
/// </para>
///
/// <para>
/// <b>Two view states, and the moves between problems.</b> The problem on
/// screen is being answered, or its review is showing (<see cref="Review"/>;
/// SPEC-quiz-view.md §1). Submit (<see cref="SubmitPlayAsync"/> /
/// <see cref="SubmitCubeAnswerAsync"/>) scores the answer, sets
/// <see cref="Review"/> without moving — the page flips to a static solution
/// view, the filled analysis panel the PPTX exporter renders in
/// <c>DiagramMode.Solution</c> — and writes an answer of record to the lifetime
/// record. ▶ (<see cref="NextAsync"/>) goes to the next problem from either
/// state: Continue from a review, Skip or Next while answering; at the
/// frontier it brings a new problem from the source, behind it it moves the
/// cursor. ⏮ ◀ ⏭ move the cursor among the problems already presented. Every
/// landing is the answering state, ready for a live answer on a problem still
/// unresolved and a practice one on a completed problem (SPEC-quiz-history.md
/// §3). <see cref="EndQuizAsync"/> is the one path that leaves the flow
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
/// <b>Starting a run: prepare, then commit</b> (<c>SPEC-quiz-history.md</c> §8;
/// halheinrich/backgammon#325 item 1). <see cref="StartAsync"/> and
/// <see cref="RestartAsync"/> are one transition, an <i>attempt</i> to replace
/// the current run, and <see cref="AttemptAsync"/> is its one path. Everything
/// that can be refused or fail comes first and belongs to the attempt; then a
/// single step that cannot fail makes the new run current along with all it
/// holds beyond the <see cref="QuizRun"/> — its source and stream, its
/// telemetry and name, the filters and mix its Restart replays — and only
/// after that is the previous run's stream released. So an attempt that is
/// refused or faults keeps the current run whole, active or finished, and
/// the state "started, not ended, nothing on screen" never occurs between
/// transitions.
/// </para>
///
/// <para>
/// Decision-type policy: the user's <see cref="FilterConfig.DecisionType"/>
/// choice governs which decisions the quiz admits — checker plays, cube
/// decisions, or both. The controller adds no decision-type filter of its
/// own; both checker plays (scored via <see cref="SubmitPlayAsync"/>) and
/// cube decisions (scored via <see cref="SubmitCubeAnswerAsync"/>) flow when
/// the user's filter admits them.
/// </para>
///
/// <para>
/// Lifetime stats: the controller drives the injected
/// <see cref="IProblemStatsSink"/> at exactly two points — the context bind
/// in <see cref="AttemptAsync"/> (every Start/Restart) and the fold of
/// each answer of record <b>at the submission that made it</b>
/// (<see cref="SubmitPlayAsync"/> / <see cref="SubmitCubeAnswerAsync"/>,
/// through one shared <see cref="FoldAsync"/>; SPEC-scoring.md §2's fold
/// trigger). What folds is exactly what the run reports that submission made
/// of record: this type decides neither whether a submission was live nor
/// what it recorded. Nothing else folds — not moving on, not ending the quiz,
/// not a new run — so an answer folds once, and an answer submitted and then
/// left is in the record however it is left. The run holds the record;
/// folding it is this type's, since the sink is outside the run. The sink
/// never throws for stats trouble, so quiz flow is independent of whether
/// stats are recording.
/// </para>
///
/// <para>
/// <b>Transition gate.</b> The <i>async</i> transitions —
/// <see cref="StartAsync"/> / <see cref="RestartAsync"/> /
/// <see cref="SubmitPlayAsync"/> / <see cref="SubmitCubeAnswerAsync"/> /
/// <see cref="NextAsync"/> at the frontier / <see cref="EndQuizAsync"/> —
/// share one busy gate: a second gesture arriving
/// while a transition is in flight <b>no-ops</b> (it does not queue). Only the
/// gate's holder draws from or releases a stream: the current run's, or —
/// while a Start or Restart prepares, when two are open — the attempt's
/// own, the current run's lying idle beside it. An overlapping
/// <c>MoveNextAsync</c> on one stream — or a release during one — throws on a
/// thread-pool continuation where no page can catch it, terminating the WASM
/// runtime; the per-method state guards
/// (<see cref="Current"/> / <see cref="Review"/> / <see cref="IsFinished"/>)
/// cannot close that window because they read <i>stale</i> state while the
/// first call is suspended mid-await. Submit holds the gate for its write to
/// the lifetime record, so nothing can begin a new run, move on or end the
/// quiz while an answer it has made of record is still on its way there. The
/// gate lives here, not in the pages, so no caller needs to know the
/// enumerator contract to be safe. The synchronous moves — ⏮ ◀ ⏭, and ▶
/// behind the frontier — cannot overlap an await themselves but <i>can</i>
/// land inside one, so they no-op while <see cref="IsBusy"/> too. See
/// <see cref="IsBusy"/> for observability and the <see cref="StateChanged"/>
/// contract.
/// </para>
/// </summary>
internal sealed class QuizController : IAsyncDisposable
{
    private readonly ProblemSetSourceFactory _sourceFactory;
    private readonly IProblemStatsSink _statsSink;
    private readonly TimeProvider _clock;
    private readonly ILogger<QuizController> _logger;

    /// <summary>
    /// The current run, or null before the first Start. It is immutable, so
    /// every transition below replaces it with the run its own transition
    /// returns, and a Start or Restart that does not commit — refused or
    /// faulted — leaves the one here, its ranking and its record included,
    /// exactly as it was. Everything this type reports about the run is read
    /// off it on each call; nothing about the run is kept beside it but
    /// <see cref="_source"/>, which is what the run holds beyond itself.
    /// </summary>
    private QuizRun? _run;

    /// <summary>
    /// What the current run holds beyond the <see cref="QuizRun"/>
    /// (<c>SPEC-quiz-history.md</c> §8): its source and the one stream drawn
    /// from it, the telemetry and name, the filters and mix its Restart
    /// replays. Null exactly while <see cref="_run"/> is: the two are
    /// assigned together, by <see cref="SwitchTo"/> alone, and change only
    /// when the run is replaced.
    /// </summary>
    private RunSource? _source;

    public QuizController(
        ProblemSetSourceFactory sourceFactory, IProblemStatsSink statsSink, TimeProvider clock,
        ILogger<QuizController> logger)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _statsSink = statsSink ?? throw new ArgumentNullException(nameof(statsSink));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The current run's source name once started; null otherwise.</summary>
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
    /// Set by Submit and gone once the cursor leaves the problem
    /// (<see cref="NextAsync"/>, <see cref="GoToFirst"/> / <see cref="GoBack"/> /
    /// <see cref="GoToLast"/>, and on start / restart): every landing shows the
    /// decision, never an earlier review. Non-null marks the <i>review</i> state:
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
    /// being answered, in its solution review, and on every return to it, so
    /// the board does not flip under the user of its own accord. The side actually drawn is the
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
    /// A Start or Restart that does not commit — refused or faulted — begins
    /// no run, so the run under way keeps its own.
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
    /// quiz finished — plus the problems ▶ deferred, which stay unresolved
    /// behind the frontier until answered. Auto-skipped no-choice positions
    /// (the user never saw them) are excluded — see
    /// <see cref="HasNoPlayChoice"/>.
    ///
    /// <para>
    /// <b>A Skip counts when the next problem lands</b>, not at the press: the
    /// problem is deferred, not completed, and it joins the count once another
    /// problem is presented beyond it — or, if the source has none, when the
    /// run finishes and converts it. While the draw is pending the count is
    /// unchanged. <see cref="NextAddsToSkipCount"/> says where a press of ▶
    /// would add to it.
    /// </para>
    ///
    /// <para>
    /// A skip of record stands: practice on a problem skipped of record leaves
    /// it counted (SPEC-scoring.md §2). A deferred problem's count is
    /// provisional: a live answer on returning to it takes it out of this count
    /// and into <see cref="Score"/>, so the count can fall within a run.
    /// </para>
    /// </summary>
    public int SkippedCount => _run?.SkippedCount ?? 0;

    /// <summary>True when a quiz is in progress (active or finished): there is a run.</summary>
    public bool HasStarted => _run is not null;

    /// <summary>
    /// How many problems the run has shown — its presented sequence
    /// (<see cref="QuizRun.Presented"/>; SPEC-quiz-history.md §1). A finished
    /// run has accounted for every one of them, answered or skipped of record
    /// (§4), so this is Done's "problems shown"; nothing rebuilds it from the
    /// score and the skip count. Zero before start.
    /// </summary>
    public int PresentedCount => _run?.Presented.Length ?? 0;

    /// <summary>
    /// True when there is an earlier problem to go back to
    /// (<see cref="QuizRun.CanGoBack"/>): ⏮ and ◀ are unavailable where it is
    /// false — on the first problem, and with nothing on screen
    /// (SPEC-quiz-history.md §2). The run's fact, not a gate: a page adds
    /// <see cref="IsBusy"/>, as it does for every control.
    /// </summary>
    public bool CanGoBack => _run?.CanGoBack ?? false;

    /// <summary>
    /// True when the problem on screen is behind the frontier
    /// (<see cref="QuizRun.CanGoToLast"/>): ⏭ is unavailable where it is false
    /// (SPEC-quiz-history.md §2). The run's fact, not a gate, as
    /// <see cref="CanGoBack"/> is.
    /// </summary>
    public bool CanGoToLast => _run?.CanGoToLast ?? false;

    /// <summary>
    /// True when ▶ pressed now would add to <see cref="SkippedCount"/> — the
    /// run's rule (<see cref="QuizRun.NextAddsToSkipCount"/>), which ▶'s name
    /// follows: Skip where this holds and Next elsewhere
    /// (SPEC-quiz-history.md §2). A page reads it here and never rebuilds it
    /// from the cursor, the frontier and a disposition.
    /// </summary>
    public bool NextAddsToSkipCount => _run?.NextAddsToSkipCount ?? false;

    /// <summary>
    /// True while an async transition (Start / Restart / Submit / ▶ at the
    /// frontier / End quiz) is in flight — for Submit, while its answer of record
    /// is being written to the lifetime record. While set, every transition
    /// entry point no-ops — see the class-level transition-gate doc. Pages
    /// drive their busy affordances (progress cursor, disabled controls) from
    /// this; <see cref="StateChanged"/> fires on both flips. The transitions
    /// that draw from the source yield once after setting it, so the busy state
    /// can render and paint <i>before</i> their churn begins (the sources'
    /// time-budgeted yields keep paints possible during the churn itself);
    /// Submit needs no such yield, since the write's own awaits let the review
    /// it has already put on screen paint.
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
    /// recording landed). It is the current run's, held with its source, so
    /// a Start or Restart that does not commit — refused or faulted — leaves
    /// a running quiz its telemetry, and its notice framing.
    /// </summary>
    public MixComposition? LastComposition => _source?.LastComposition;

    /// <summary>
    /// The N of "Problem N of M": the 1-based stream slot of
    /// <see cref="Current"/>, read off the run's cursor
    /// (<see cref="PresentedProblem.StreamSlot"/>, which owns the convention —
    /// consumed stream slots, auto-skipped no-choice positions included, so an
    /// auto-skip shows as a gap and N never exceeds <see cref="ProblemCount"/>).
    /// Zero while no problem is on screen: before the first is presented, and
    /// after finish. It follows the cursor wherever navigation takes it.
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
    /// place. Submit's run step is synchronous and taken before the gate's
    /// first fire, so that fire already carries the review: it is on screen,
    /// its controls busy, while the answer is written, and the second fire
    /// releases them. The synchronous moves — ⏮ ◀ ⏭, and ▶ behind the
    /// frontier — fire once. A Start or Restart that does not commit — a
    /// refused weighted one, or one that faults — fires the two busy flips and
    /// nothing else: the current run is kept, so the re-renders show it as
    /// it was.
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
    /// <b>An attempt, which keeps the current run unless it commits</b>
    /// (<c>SPEC-quiz-history.md</c> §8; the class docs' "prepare, then
    /// commit"). A Start that faults — building the source, opening its
    /// stream, or drawing to its first problem — throws, and the run that was
    /// current before it is still current, active or finished, its Restart
    /// still replaying its own filters and mix. What the attempt opened has
    /// been released.
    /// </para>
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
    /// run unweighted — see <see cref="AttemptAsync"/> for the
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
    /// <exception cref="Exception">
    /// Whatever building the source, opening its stream or drawing from it
    /// threw: the attempt faulted, and the current run is kept.
    /// </exception>
    public async Task<QuizStartOutcome> StartAsync(
        FilterConfig userConfig, QuizMix mix, PlayRanking ranking, bool ignoreMix = false)
    {
        ArgumentNullException.ThrowIfNull(userConfig);
        ArgumentNullException.ThrowIfNull(mix);
        RefuseUndefined(ranking);

        // Build a fresh, controller-owned pipeline from the immutable DTO. The
        // user's DecisionType choice governs decision-type admission; the
        // controller adds no filter of its own. Restart re-uses this pipeline
        // without re-building. Built (and validated) before the attempt
        // begins, so a malformed config is a caller bug that begins nothing.
        var pipeline = userConfig.Build();

        if (!await TryBeginTransitionAsync()) return QuizStartOutcome.Busy;
        try
        {
            return await AttemptAsync(pipeline, mix, ranking, ignoreMix);
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
    /// local — the current run and its stream are never read or written — so
    /// a count is safe to run against a controller with a quiz already in
    /// progress. It deliberately does <b>not</b> take the transition gate: it
    /// draws from no stream the gate protects (the callers serialize Apply
    /// against Start on their side).
    /// </para>
    ///
    /// <para>
    /// <b>Warms the parse cache.</b> The count is a byproduct of the source's
    /// in-memory match pass. The first count after a pick pays the one-time
    /// corpus parse and populates the shared cache
    /// (<see cref="PickedProblemFolder.Parsed"/>, via the factory's
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
    ///
    /// <para>
    /// <b>And it reports what the pool's parse could not read</b>
    /// (halheinrich/backgammon#368). The stack's second reader hands back the
    /// <see cref="XgFilter_Lib.SourceReport"/> of the parse the count drew from
    /// — the same object the parse-once cache retains with the decisions — and
    /// it rides in <see cref="MatchSummary.Sources"/>, so a count over a
    /// selection with a rejected file in it says so, and a zero over a
    /// selection whose every file was refused is told apart from a filter
    /// that matched nothing. The first count after a pick is the parse that
    /// makes the report; every later count and every Start read it back, since
    /// nothing walks the files again.
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

        // Read after the drain, like the collapse: the parse is resolved by
        // enumerating. A fully drained stack that still reports no walk is a
        // composition that lost the reader, not a selection nobody read — so
        // it is refused here rather than summarized as an empty, complete one.
        var sources = composed.GetSourceReport()
            ?? throw new InvalidOperationException(
                "The composed stack was enumerated to its end but reports no source walk; " +
                "its factory must hand back the parse layer's report (ComposedProblemSource).");
        return new MatchSummary(distribution, composed.GetDuplicatesCollapsed(), sources);
    }

    /// <summary>
    /// Score the user's <paramref name="play"/> against <see cref="Current"/>'s
    /// candidate list, enter the <i>review</i> state — set <see cref="Review"/>
    /// without advancing — and write the answer of record it makes, if it makes
    /// one, to the lifetime record. <see cref="NextAsync"/> moves to the
    /// next problem.
    ///
    /// <para>
    /// <b>The run scores it and files it</b> (<see cref="QuizRun.SubmitPlay"/>,
    /// which owns both rules). The first submission against a problem is its
    /// answer of record (SPEC-scoring.md §2): it alone reaches
    /// <see cref="Score"/> / <see cref="SkippedCount"/> and the lifetime record.
    /// A submission on a problem already completed — returned to by
    /// navigation — is practice: scored the same way and reviewed the same way,
    /// so the user sees how the retry did, and recorded nowhere.
    /// </para>
    ///
    /// <para>
    /// <b>Three outcomes, two of them skips.</b> Scoring is the producer's
    /// (<see cref="PlaySubmission.Score"/>), under the run's ranking. A scored
    /// play is the answer of record: it counts in <see cref="Score"/>, and
    /// folds here. A play the ranking does not score — under depth first, a
    /// candidate analyzed less deeply than the best play, yet rated higher — and an
    /// off-list play, one no candidate is, are each a skip of record that folds
    /// nothing (SPEC-scoring.md §2 and §2a): <see cref="SkippedCount"/> counts
    /// it, and practice on it later leaves it standing. Every outcome still
    /// produces a <see cref="Review"/> carrying the producer's outcome whole and
    /// the play as entered, so the user sees the solution, what the verdict
    /// was, and — off the list — which play the app read
    /// (halheinrich/backgammon#274).
    /// </para>
    ///
    /// <para>
    /// <b>The fold is the answer the run says this submission made of
    /// record</b> (SPEC-scoring.md §2: it folds "at the first submission
    /// itself"). The run reports it with the step (the <c>out</c> argument of
    /// <see cref="QuizRun.SubmitPlay"/>), and it is the only thing that folds:
    /// nothing here asks whether the submission was live, or reads the review.
    /// </para>
    ///
    /// <para>
    /// <b>The review is on screen before the write lands.</b> The run's step is
    /// taken as this call enters the transition gate, so <see cref="Review"/> is
    /// set by the time the call returns its task, and the first
    /// <see cref="StateChanged"/> already carries it. The task completes when
    /// the write has landed; until then <see cref="IsBusy"/> holds the gate, so
    /// the review's controls show busy and no Start, Restart, Continue, Skip or
    /// End quiz can interleave with the write. A submission that makes nothing
    /// of record holds the gate for no time at all.
    /// </para>
    ///
    /// <para>
    /// No-op while <see cref="IsBusy"/>, when <see cref="Current"/> is null,
    /// <see cref="IsFinished"/>, or already in the review state
    /// (<see cref="Review"/> set).
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Current"/> is a cube decision, which is answered with
    /// <see cref="SubmitCubeAnswerAsync"/> — a caller bug, since the page routes
    /// each kind to its own answer instrument.
    /// </exception>
    public Task SubmitPlayAsync(Play play) =>
        SubmitAsync(run => (run.SubmitPlay(play, out var made), made));

    /// <summary>
    /// Score the user's cube <paramref name="answer"/> at
    /// <see cref="Current"/>'s cube decision, enter the <i>review</i> state —
    /// set <see cref="Review"/> without advancing — and write the answer of
    /// record it makes, if it makes one, to the lifetime record.
    /// <see cref="NextAsync"/> moves to the next problem.
    ///
    /// <para>
    /// The run scores it and files it (<see cref="QuizRun.SubmitCubeAnswer"/>):
    /// of record only the first time, exactly as <see cref="SubmitPlayAsync"/>
    /// describes (SPEC-scoring.md §2), and folded, gated and put on screen the
    /// same way; a submission on a completed problem is practice, and the
    /// scoring below is what both get.
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
    /// Unlike <see cref="SubmitPlayAsync"/> there is no off-list / skip path —
    /// every cube answer is scored, so a live one always folds. The scored
    /// answer and the decision it was scored at are carried on
    /// <see cref="ProblemReview.Cube"/>, which drives the verdict line.
    /// </para>
    ///
    /// <para>
    /// No-op while <see cref="IsBusy"/>, when <see cref="Current"/> is null,
    /// <see cref="IsFinished"/>, or already in the review state
    /// (<see cref="Review"/> set).
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Current"/> is a checker-play decision, which is answered with
    /// <see cref="SubmitPlayAsync"/> — a caller bug, since the page routes each
    /// kind to its own answer instrument.
    /// </exception>
    public Task SubmitCubeAnswerAsync(CubeAnswer answer) =>
        SubmitAsync(run => (run.SubmitCubeAnswer(answer, out var made), made));

    /// <summary>
    /// ▶ — "the next problem", from either view state (SPEC-quiz-history.md
    /// §2, §4): Continue from a review, Skip or Next while answering. The step
    /// is the run's (<see cref="QuizRun.Next"/>), and it completes nothing —
    /// the problem left keeps the disposition it has — so nothing folds: an
    /// answer of record reached the lifetime record at its Submit.
    ///
    /// <para>
    /// <b>Behind the frontier</b> the cursor moves to the next presented
    /// problem. There is no source work, so the move is taken at once, fires
    /// <see cref="StateChanged"/> once, and holds no gate.
    /// </para>
    ///
    /// <para>
    /// <b>On the frontier</b> the run is owed a new problem: inside the
    /// transition gate the step is taken and the source is asked, and the next
    /// problem is presented — or, if the source has none, the run finishes. An
    /// unresolved frontier left this way is deferred: the skip count takes it
    /// in once the next problem lands, not at the press (§5's ruled transient
    /// difference), or the run's finishing converts it.
    /// <see cref="NextAddsToSkipCount"/> says beforehand whether a press adds to
    /// the count, which is what ▶'s name follows. A draw that faults leaves the
    /// cursor on the frontier, in the answering state, the gate released.
    /// </para>
    ///
    /// <para>
    /// No-op while <see cref="IsBusy"/>, and with nothing on screen — before
    /// start and after finish.
    /// </para>
    /// </summary>
    public async Task NextAsync()
    {
        // IsBusy is refused here, before the run is asked: the move behind the
        // frontier takes no gate, and inside a pending Submit's write the
        // review it would take down is still being written. The frontier's
        // draw is refused by the gate as well.
        if (IsBusy || _run is not { Cursor: not null } run) return;

        var moved = run.Next(out var bringsNewProblem);
        if (!bringsNewProblem)
        {
            Land(moved);
            return;
        }

        if (!await TryBeginTransitionAsync()) return;
        try
        {
            // In place while the draw is pending, so a fault leaves the run
            // here: the cursor on the frontier, answering.
            _run = moved;
            _run = await PresentNextAsync(moved, CurrentSource);
        }
        finally
        {
            EndTransition();
        }
    }

    /// <summary>
    /// ⏮ — land on the first problem presented (<see cref="QuizRun.GoToFirst"/>;
    /// SPEC-quiz-history.md §2). Like every landing it shows the decision,
    /// never the solution (§3): the review or the entry in progress on the
    /// problem left is discarded, and nothing of record changes. A move among
    /// the problems already presented, so it is taken at once and fires
    /// <see cref="StateChanged"/> once.
    /// </summary>
    /// <remarks>
    /// No-op while <see cref="IsBusy"/>, and where <see cref="CanGoBack"/> is
    /// false — on the first problem and with nothing on screen.
    /// </remarks>
    public void GoToFirst()
    {
        if (IsBusy || _run is not { CanGoBack: true } run) return;
        Land(run.GoToFirst());
    }

    /// <summary>
    /// ◀ — land on the problem before the one on screen
    /// (<see cref="QuizRun.GoBack"/>; SPEC-quiz-history.md §2), in its answering
    /// state, exactly as <see cref="GoToFirst"/> lands.
    /// </summary>
    /// <remarks>
    /// No-op while <see cref="IsBusy"/>, and where <see cref="CanGoBack"/> is
    /// false.
    /// </remarks>
    public void GoBack()
    {
        if (IsBusy || _run is not { CanGoBack: true } run) return;
        Land(run.GoBack());
    }

    /// <summary>
    /// ⏭ — land on the frontier (<see cref="QuizRun.GoToLast"/>;
    /// SPEC-quiz-history.md §2), in its answering state, exactly as
    /// <see cref="GoToFirst"/> lands. It never presents a problem: the frontier
    /// is already in the sequence, so this asks nothing of the source.
    /// </summary>
    /// <remarks>
    /// No-op while <see cref="IsBusy"/>, and where <see cref="CanGoToLast"/> is
    /// false — on the frontier and with nothing on screen.
    /// </remarks>
    public void GoToLast()
    {
        if (IsBusy || _run is not { CanGoToLast: true } run) return;
        Land(run.GoToLast());
    }

    /// <summary>
    /// End the run here, at the user's request, and finish with the score of
    /// what they answered — the quiz-side exit a run that has served its purpose
    /// needs (issue halheinrich/backgammon#57). A real transition, not a navigation: only the
    /// controller can end the run and release its stream,
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
    /// ▶ deferred already is, rather than in a category of
    /// its own — so Done's "problems shown" still counts a problem the user
    /// actually saw. There is no partial-answer path and no new scoring path: a
    /// play half assembled on the board was never a submission, and
    /// <c>BackgammonPlayEntry</c> exposes nothing that would let one be
    /// salvaged — the same producer gap the Quiz page's Undo enablement
    /// records.
    /// </para>
    ///
    /// <para>
    /// <b>An answered problem stands, and folds nothing more.</b> Ending on a
    /// problem that holds an answer of record is a forward exit, not an
    /// abandonment: the answer was submitted, counted in <see cref="Score"/>,
    /// and read — so it stays in the partial score, and nothing is counted as
    /// skipped on top of it. It reached the lifetime record when it was
    /// submitted (SPEC-scoring.md §2), so ending writes nothing: <i>every
    /// answer visible on Done has reached the lifetime record</i> by
    /// construction, whichever way the run finished — the line Done's "nothing
    /// here needs saving" states to the user. The conversion keys on the
    /// <i>record</i>, not on <see cref="Review"/>: a run ended on a completed
    /// problem returned to for practice, not yet re-answered, is showing no
    /// review and has still answered the problem. It acts on the run, not the
    /// cursor (§4): ended from behind the frontier, every problem still
    /// unresolved is converted, wherever it sits.
    /// </para>
    /// </summary>
    public async Task EndQuizAsync()
    {
        if (_run is not { IsEnded: false }) return;
        if (!await TryBeginTransitionAsync()) return;
        try
        {
            _run = _run.End();
            // The run is over, so its stream is released here rather than at
            // the switch to the next run — safe precisely because the gate
            // guarantees no MoveNextAsync is in flight. The source stays the
            // run's: its name, its telemetry, and what its Restart replays.
            await CurrentSource.ReleaseAsync();
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
    /// <see cref="QuizRun"/>.
    /// </para>
    ///
    /// <para>
    /// <b>The filters and mix are replayed; the source is built afresh</b>
    /// (<c>SPEC-quiz-history.md</c> §8, "What a Restart replays"): the factory
    /// is called again, so the new source reads the folder picked when Restart
    /// is pressed — a folder re-picked mid-quiz included — not the files this
    /// run drew from. Done promises "same filters", and no more.
    /// </para>
    ///
    /// <para>
    /// <b>An attempt, exactly as a Start is</b> (see <see cref="StartAsync"/>):
    /// a Restart that faults throws and keeps the run it would have replaced,
    /// whose summary stands and whose Restart can be pressed again.
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
    /// <exception cref="Exception">
    /// Whatever building the source, opening its stream or drawing from it
    /// threw: the attempt faulted, and the current run is kept.
    /// </exception>
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
            if (_source is not { } current)
                throw new InvalidOperationException(
                    "RestartAsync requires a prior successful StartAsync — no quiz has been started.");
            return await AttemptAsync(current.Filters, current.Mix, ranking, ignoreMix);
        }
        finally
        {
            EndTransition();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_source is not null) await _source.ReleaseAsync();
    }

    // -----------------------------------------------------------------------
    //  Internal
    // -----------------------------------------------------------------------

    /// <summary>
    /// The transition gate's check-and-set, and the only one: take the gate and
    /// say so, or report it already held. It fires nothing — each way in
    /// decides what the busy-on <see cref="StateChanged"/> carries. Single-
    /// threaded scheduler: it runs unbroken, so no interleaved caller can slip
    /// past it.
    /// </summary>
    private bool TryEnterGate()
    {
        if (IsBusy) return false;
        IsBusy = true;
        return true;
    }

    /// <summary>
    /// Enter the transition gate for a transition that draws from the source,
    /// or report it already held. On entry <see cref="IsBusy"/> flips on,
    /// <see cref="StateChanged"/> fires, and the method yields once —
    /// deliberately — so observing pages can render (and the browser paint) the
    /// busy state before the caller's churn begins. The check-and-set runs
    /// before the yield, so no interleaved caller can slip past it.
    /// </summary>
    private async ValueTask<bool> TryBeginTransitionAsync()
    {
        if (!TryEnterGate()) return false;
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
    /// reaches refuses such a value too, but only once the attempt is under
    /// way — after the stats bind — so a start given one would fault having
    /// re-bound the stats context. Refusing it here, before anything begins,
    /// keeps a malformed argument a caller bug that changes nothing, as a
    /// malformed filter config is.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ranking"/> is not a defined ranking.</exception>
    private static void RefuseUndefined(PlayRanking ranking)
    {
        if (!Enum.IsDefined(ranking))
            throw new ArgumentOutOfRangeException(nameof(ranking), ranking, "Not a defined play ranking.");
    }

    /// <summary>
    /// Put <paramref name="moved"/> — the run after a move of the cursor among
    /// the problems already presented — in place, and say so. Such a move asks
    /// nothing of the source, so it is one assignment and holds no gate; the
    /// caller has refused a busy controller in the same synchronous step.
    /// </summary>
    private void Land(QuizRun moved)
    {
        _run = moved;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Submit, for both answer kinds: take the transition gate, let the run
    /// take the submission (<paramref name="submit"/>, the run's own submit
    /// step for the kind, which reports what it made of record), put the run it
    /// returns in place — so its review is what the busy-on
    /// <see cref="StateChanged"/> carries — then write what the submission made
    /// of record, if anything, to the lifetime record, and release the gate
    /// once the write has landed.
    ///
    /// <para>
    /// <b>The gate is the one guard against a busy controller.</b> It is
    /// entered before the run is asked anything, and it closes the window a
    /// pending ▶ at the frontier opens: mid-draw no review is showing and
    /// <see cref="Current"/> still points at the outgoing problem, so the
    /// state guard passes and the run would take a submission there. Behind a
    /// Skip that problem is still unresolved, so the submission would be live
    /// — an answer of record, written to the lifetime record, on a problem the
    /// user has moved on from. Behind a Continue it would be practice, and the
    /// advance would land over its review.
    /// </para>
    ///
    /// <para>
    /// A submission of the wrong kind is a caller bug the run throws for; it
    /// throws out of here with the gate released and the run as it was.
    /// </para>
    /// </summary>
    private async Task SubmitAsync(Func<QuizRun, (QuizRun Submitted, AnswerOfRecord? Made)> submit)
    {
        if (_run is not { IsAnswering: true } run || !TryEnterGate()) return;
        try
        {
            (var submitted, var made) = submit(run);
            _run = submitted;
            StateChanged?.Invoke();
            if (made is not null) await FoldAsync(made);
        }
        finally
        {
            EndTransition();
        }
    }

    /// <summary>
    /// Fold <paramref name="answer"/> into the lifetime-stats sink — the one
    /// encoding of "what folds", and Submit's alone
    /// (<see cref="SubmitAsync"/>; SPEC-scoring.md §2: the answer of
    /// record folds "at the first submission itself"). It is handed exactly what
    /// the run reported a live submission made of record, and reads nothing
    /// else — not the disposition, not <see cref="Review"/> — so it cannot fold
    /// a practice submission, a skip, or one answer twice. Each answer kind
    /// goes to the sink's overload for it.
    /// </summary>
    private Task FoldAsync(AnswerOfRecord answer) =>
        answer.Match(
            play: submission => _statsSink.RecordAsync(submission),
            cube: submission => _statsSink.RecordAsync(submission));

    /// <summary>
    /// The one path under Start and Restart: an <i>attempt</i> to replace the
    /// current run, which prepares and then commits
    /// (<c>SPEC-quiz-history.md</c> §8 — read the rules there, not from this
    /// summary). Prepare is the refusal checks, the lifetime-stats bind, and
    /// <see cref="PrepareAsync"/>: building the source, opening its stream and
    /// drawing to the first problem with a choice or the end. Commit is
    /// <see cref="SwitchTo"/>, and the previous run's stream is released after
    /// it.
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
    /// refuses before anything is built. Either way the attempt ends with
    /// nothing to release, and the current run is kept; the only
    /// <see cref="StateChanged"/> firings are the enclosing gate's two busy
    /// flips.
    /// </para>
    ///
    /// <para>
    /// <b>The stats link is the one thing a kept run does not keep.</b> The
    /// bind is part of prepare, because the stage-2 check and a weighted
    /// composition read the context it binds, so any attempt that passes
    /// stage 1 has re-bound it, whether or not it commits — and the kept
    /// run's later answers fold into that context. A known limitation
    /// (§8; halheinrich/backgammon#404), not this type's to change: see the
    /// Pitfalls in INSTRUCTIONS.md.
    /// </para>
    ///
    /// <para>
    /// <b>A fault in prepare keeps the current run.</b> Nothing the current
    /// run holds is touched before the switch — not its stream, not the
    /// filters and mix its Restart replays — and what the attempt opened is
    /// released before the fault propagates. The new run is begun from
    /// <paramref name="ranking"/>, the setting the caller read, and its source
    /// is built under the run's ranking, not the argument's, so there is no
    /// moment at which one run is scored under another's ranking.
    /// </para>
    /// </summary>
    /// <param name="filters">The new run's filter set: a Start's freshly built one, or the set a Restart replays.</param>
    /// <param name="mix">The new run's stored mix — what its own Restart will re-attempt.</param>
    /// <param name="ranking">The new run's ranking.</param>
    /// <param name="ignoreMix">Run this one quiz as passthrough, while <paramref name="mix"/> is still stored.</param>
    private async Task<QuizStartOutcome> AttemptAsync(
        DecisionFilterSet filters, QuizMix mix, PlayRanking ranking, bool ignoreMix)
    {
        // The composition this run actually uses: the override runs one quiz
        // as passthrough while the stored mix stays what the user configured.
        var effectiveMix = ignoreMix ? QuizMix.Empty : mix;

        if (!effectiveMix.IsPassthrough && !_statsSink.CanWeightMix)
            return QuizStartOutcome.MixRequiresStats;

        // Bind the lifetime-stats context for the quiz now starting. This is
        // the one path under Start and Restart, so it is exactly where the
        // picked folder promotes to the active stats slot — a mid-quiz Clear
        // or re-pick changes nothing until the next pass through here. Bound
        // before the source is built because the mix decision below needs the
        // freshly bound context.
        await _statsSink.BeginQuizAsync();

        if (!effectiveMix.IsPassthrough && _statsSink.CurrentDocument is null)
            return QuizStartOutcome.MixRequiresStats;

        var (first, source) = await PrepareAsync(filters, mix, effectiveMix, ranking);

        var previous = SwitchTo(first, source);
        if (previous is not null) await ReleaseReplacedAsync(previous);
        return QuizStartOutcome.Started;
    }

    /// <summary>
    /// Prepare's part that builds: the new run, begun from
    /// <paramref name="ranking"/>; its source, from the factory and — for a
    /// weighted <paramref name="effectiveMix"/> — the producer's composing
    /// decorator; its stream, opened; and the draw to its first problem with a
    /// choice, or to the end of the stream. Returns the run as it will become
    /// current, with the source it holds. Everything here belongs to the
    /// attempt, and nothing of the current run is read or written.
    ///
    /// <para>
    /// <b>A fault releases what was opened, and stays the fault.</b> If the
    /// draw throws, the stream is released before the fault propagates; a
    /// release that throws as well is logged and dropped, so the error that
    /// ended the attempt is the one the caller sees (§8, "Releasing never
    /// misreports"). A fault in the factory, or in opening the stream, has
    /// opened nothing.
    /// </para>
    /// </summary>
    private async Task<(QuizRun First, RunSource Source)> PrepareAsync(
        DecisionFilterSet filters, QuizMix mix, QuizMix effectiveMix, PlayRanking ranking)
    {
        // From here on the ranking is read off the run, the source's included.
        var run = QuizRun.Begin(ranking);
        var source = RunSource.Open(
            filters, mix, _sourceFactory(filters, run.Ranking, effectiveMix).Source,
            effectiveMix, GetCurrentDocumentOrThrow, _clock);

        try
        {
            // A source that declares its size states the run's total now. A
            // composing one declares none — its size is known only once it has
            // composed — and PresentNextAsync hands that over after the first
            // draw.
            var counted = source.Count is { } total ? run.WithProblemCount(total) : run;
            return (await PresentNextAsync(counted, source), source);
        }
        catch (Exception fault)
        {
            await ReleaseAbandonedAsync(source, fault);
            throw;
        }
    }

    /// <summary>
    /// Commit: make <paramref name="first"/> the current run and
    /// <paramref name="source"/> what it holds beyond itself, in one step that
    /// cannot fail, and hand back the source of the run replaced — its stream
    /// still to be released, which the caller does only now that the switch
    /// has happened. The only writer of <see cref="_source"/>, so the run and
    /// what it holds change together or not at all.
    /// </summary>
    private RunSource? SwitchTo(QuizRun first, RunSource source)
    {
        var previous = _source;
        _run = first;
        _source = source;
        return previous;
    }

    /// <summary>
    /// Release the stream of the run a committed attempt replaced. The switch
    /// has happened, so a release that throws neither undoes it nor makes the
    /// Start or Restart a failed one (§8, "Releasing never misreports"): it is
    /// logged, and the new run stands.
    /// </summary>
    private async ValueTask ReleaseReplacedAsync(RunSource previous)
    {
        try
        {
            await previous.ReleaseAsync();
        }
        catch (Exception releaseFault)
        {
            _logger.LogWarning(releaseFault,
                "Releasing the previous quiz's problem stream failed after the new quiz became current; the new quiz stands.");
        }
    }

    /// <summary>
    /// Release the stream of an attempt that faulted, without letting a fault
    /// in the release replace <paramref name="fault"/>, the error that ended
    /// the attempt (§8, "Releasing never misreports"): a release that throws is
    /// logged, and the caller rethrows <paramref name="fault"/>.
    /// </summary>
    private async ValueTask ReleaseAbandonedAsync(RunSource source, Exception fault)
    {
        try
        {
            await source.ReleaseAsync();
        }
        catch (Exception releaseFault)
        {
            _logger.LogWarning(releaseFault,
                "Releasing the problem stream of a Start or Restart that faulted also failed; the attempt's own error is the one reported: {Fault}",
                fault.Message);
        }
    }

    /// <summary>
    /// The current run's source, for a transition that has just found a
    /// current run. A current run always has one, since the two are committed
    /// together (<see cref="SwitchTo"/>), so its absence is this type's bug.
    /// </summary>
    private RunSource CurrentSource => _source ?? throw new InvalidOperationException(
        "A current run always has its source; the two are committed together.");

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
    /// Bring <paramref name="run"/> the problem it is owed — its first, or the
    /// one after a frontier it has moved on from — from
    /// <paramref name="source"/>, the run's own: draw until a position offers
    /// a play choice and present it, or end the run when the stream has none
    /// left, and return the run that follows. This is the whole of what the
    /// run is told about the source: which problem is shown, how many slots
    /// were passed over silently on the way to it, the side rolled for its
    /// board, and the stream's total once the first draw has established one.
    ///
    /// <para>
    /// It writes no state, so the caller decides what the result becomes: a
    /// Start or Restart's prepare holds it as the attempt's until the switch,
    /// and ▶ puts it in place. A draw that throws returns nothing, and the
    /// fault propagates to the caller as it was.
    /// </para>
    /// </summary>
    private static async Task<QuizRun> PresentNextAsync(QuizRun run, RunSource source)
    {
        // Stream slots drawn and passed over on the way to the next problem:
        // local to this one advance. The run turns it into the problem's slot
        // number, so no counter of the stream is kept here.
        var silentlySkipped = 0;

        while (true)
        {
            var next = await source.DrawAsync();

            // A composing source assigns its telemetry before its first yield,
            // so the first draw — even one that finds the composition empty —
            // is what establishes a weighted run's total.
            if (run.ProblemCount is null && source.LastComposition is { } composition)
                run = run.WithProblemCount(composition.DrawnCount);

            // The source has no further problem, so the run finishes — and
            // finishing is what converts a frontier the user skipped, still
            // unresolved, into a skip of record. The rule is the run's.
            if (next is null) return run.End();

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
            return run.Present(next, silentlySkipped, randomHomeBoardOnRight: Random.Shared.Next(2) == 0);
        }
        // No StateChanged here: every caller runs inside the transition gate,
        // whose EndTransition fire delivers the post-advance state.
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

    /// <summary>
    /// What a run holds beyond the <see cref="QuizRun"/>
    /// (<c>SPEC-quiz-history.md</c> §8): the filters and mix its Restart
    /// replays, the source built from them, that source's composition
    /// telemetry and name, and the one stream drawn from it — the run's place
    /// in the problem stream (§5). Built whole by an attempt's prepare,
    /// belonging to the attempt until the switch, and from then on the run's,
    /// unchanged until the run is replaced.
    ///
    /// <para>
    /// <b>The stream is released once.</b> <see cref="ReleaseAsync"/> lets the
    /// stream go before it awaits the release, so End quiz, the switch to a
    /// new run and the controller's disposal can each ask for it without one
    /// stream being released twice — and a release that throws is not tried
    /// again. Nothing draws from a released stream: the run it belongs to has
    /// ended, or has been replaced.
    /// </para>
    /// </summary>
    private sealed class RunSource
    {
        private readonly IProblemSetSource _source;
        private readonly MixedProblemSetSource? _mixedSource;
        private IAsyncEnumerator<BgDecisionData>? _stream;

        private RunSource(
            DecisionFilterSet filters, QuizMix mix, IProblemSetSource source,
            MixedProblemSetSource? mixedSource, IAsyncEnumerator<BgDecisionData> stream)
        {
            Filters = filters;
            Mix = mix;
            _source = source;
            _mixedSource = mixedSource;
            _stream = stream;
        }

        /// <summary>
        /// Compose a run's source over <paramref name="built"/>, the factory's
        /// stack, and open its stream. A blank <paramref name="effectiveMix"/>
        /// wires no composition layer at all — the settled passthrough default.
        /// An active one composes via the producer's decorator, against
        /// <paramref name="stats"/>; holding the typed reference is what
        /// surfaces <see cref="LastComposition"/> without type-testing.
        /// Opening the stream is the last step, so a source that throws there
        /// leaves nothing open.
        /// </summary>
        public static RunSource Open(
            DecisionFilterSet filters, QuizMix mix, IProblemSetSource built,
            QuizMix effectiveMix, Func<ProblemStatsDocument> stats, TimeProvider clock)
        {
            var mixedSource = effectiveMix.IsPassthrough
                ? null
                : new MixedProblemSetSource(built, stats, effectiveMix, clock);
            var source = mixedSource ?? built;
            return new RunSource(filters, mix, source, mixedSource, source.EnumerateAsync().GetAsyncEnumerator());
        }

        /// <summary>The filter set a Restart of this run replays.</summary>
        public DecisionFilterSet Filters { get; }

        /// <summary>The stored mix a Restart of this run re-attempts — the one handed in, whatever the run itself composed with.</summary>
        public QuizMix Mix { get; }

        /// <summary>The source's name (<see cref="QuizController.Name"/>).</summary>
        public string Name => _source.Name;

        /// <summary>The source's declared size, if it declares one.</summary>
        public int? Count => _source.Count;

        /// <summary>The composition's telemetry, or null when no composition layer is wired (<see cref="QuizController.LastComposition"/>).</summary>
        public MixComposition? LastComposition => _mixedSource?.LastComposition;

        /// <summary>The next item of the stream, or null at its end.</summary>
        /// <exception cref="InvalidOperationException">The stream has been released.</exception>
        public async ValueTask<BgDecisionData?> DrawAsync()
        {
            var stream = _stream ?? throw new InvalidOperationException(
                "The run's problem stream has been released; nothing may draw from it.");
            return await stream.MoveNextAsync() ? stream.Current : null;
        }

        /// <summary>Release the stream, if it is still held; a no-op once it has been.</summary>
        public ValueTask ReleaseAsync()
        {
            var stream = _stream;
            _stream = null;
            return stream?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
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
