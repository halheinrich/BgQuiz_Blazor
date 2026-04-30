namespace BgQuiz_Blazor.Quiz;

using BgDataTypes_Lib;
using BgGame_Lib;
using BgMoveGen;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;

/// <summary>
/// Per-circuit quiz state machine. Owns the active <see cref="IProblemSetSource"/>
/// enumerator, the running <see cref="QuizScore"/>, and the per-problem
/// <see cref="SubmittedPlay"/> history. Pages observe state via
/// <see cref="StateChanged"/> and drive transitions via
/// <see cref="StartAsync"/> / <see cref="SubmitPlayAsync"/> /
/// <see cref="SkipCurrentAsync"/> / <see cref="RestartAsync"/>.
///
/// <para>
/// Lifetime: <b>Scoped</b> — one instance per Blazor Server circuit. State is
/// lost on reload (server-side circuit teardown). Pre-Azure-deployment
/// follow-up will revisit render mode and persistence.
/// </para>
///
/// <para>
/// The <see cref="IProblemSetSource"/> is constructed via an injected
/// <see cref="ProblemSetSourceFactory"/> delegate, registered in
/// <c>Program.cs</c>. Phase 1 wires this to <see cref="ServerDiskProblemSetSource"/>;
/// future Phase 2+ implementations (upload, deployed bundles, curated
/// libraries) plug in by registering a different factory without controller
/// changes. Tests substitute a fake source the same way.
/// </para>
///
/// <para>
/// Phase 1 cube policy: a <see cref="DecisionTypeFilter"/> with
/// <see cref="DecisionTypeOption.CheckerPlaysOnly"/> is appended to the user's
/// filter set on <see cref="StartAsync"/>. AND-semantics with the user's
/// existing decision-type choice means CheckerPlaysOnly always wins (or, if
/// the user picked CubeOnly, the intersection is empty — the
/// <c>Home.razor</c> banner has set that expectation).
/// </para>
/// </summary>
public sealed class QuizController : IAsyncDisposable
{
    private readonly ProblemSetSourceFactory _sourceFactory;
    private readonly List<SubmittedPlay> _history = [];

    private DecisionFilterSet? _userFilters;
    private IProblemSetSource? _source;
    private IAsyncEnumerator<BgDecisionData>? _enumerator;

    public QuizController(ProblemSetSourceFactory sourceFactory)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
    }

    /// <summary>Source name once started; null otherwise.</summary>
    public string? Name => _source?.Name;

    /// <summary>The decision currently being shown to the user; null before start or after finish.</summary>
    public BgDecisionData? Current { get; private set; }

    /// <summary>Cumulative running score. Resets on <see cref="StartAsync"/> / <see cref="RestartAsync"/>.</summary>
    public QuizScore Score { get; private set; } = QuizScore.Empty;

    /// <summary>Per-problem in-list submission history.</summary>
    public IReadOnlyList<SubmittedPlay> History => _history;

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

    /// <summary>Raised after every state transition so observing pages can re-render.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Begin a fresh quiz against <paramref name="userFilters"/>. Augments the
    /// filter set with Phase 1's CheckerPlaysOnly policy. Resets score / history /
    /// skipped-count and advances to the first non-pass problem.
    /// </summary>
    public async Task StartAsync(DecisionFilterSet userFilters)
    {
        ArgumentNullException.ThrowIfNull(userFilters);

        // Stash the user's set; augment once with the Phase 1 cube policy.
        // Restart re-uses the augmented set without re-adding.
        _userFilters = userFilters;
        _userFilters.Add(new DecisionTypeFilter(DecisionTypeOption.CheckerPlaysOnly));

        await ResetAndAdvanceAsync();
    }

    /// <summary>
    /// Score the user's <paramref name="play"/> against <see cref="Current"/>'s
    /// candidate list and advance.
    ///
    /// <para>
    /// Matching is structural: <see cref="Play.DeduplicationKey"/> on the
    /// submitted play is compared to each candidate's
    /// <see cref="PlayCandidate.Play"/> key in list order; the first match
    /// scores. <see cref="PlayCandidate.EquityLoss"/> <c>== 0.0</c> identifies
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
    /// rather than a user error.
    /// </para>
    ///
    /// <para>No-op when <see cref="Current"/> is null or <see cref="IsFinished"/>.</para>
    /// </summary>
    public async Task SubmitPlayAsync(Play play)
    {
        if (Current is null || IsFinished) return;

        var key = play.DeduplicationKey();
        int? matchedIdx = null;
        var plays = Current.Decision.Plays;
        for (int i = 0; i < plays.Count; i++)
        {
            if (plays[i].Play.DeduplicationKey().Equals(key))
            {
                matchedIdx = i;
                break;
            }
        }

        if (matchedIdx is int idx)
        {
            var candidate = plays[idx];
            var submitted = new SubmittedPlay(
                play,
                idx,
                candidate.EquityLoss,
                candidate.EquityLoss == 0.0);
            _history.Add(submitted);
            Score = Score.Plus(submitted);
        }
        else
        {
            SkippedCount++;
        }

        await AdvanceAsync();
    }

    /// <summary>Skip the current problem without recording; advance.</summary>
    public async Task SkipCurrentAsync()
    {
        if (Current is null || IsFinished) return;
        SkippedCount++;
        await AdvanceAsync();
    }

    /// <summary>
    /// Restart the quiz from the beginning of the source using the existing
    /// (already-augmented) filter set. No-op when no quiz has been started.
    /// </summary>
    public async Task RestartAsync()
    {
        if (_userFilters is null) return;
        await ResetAndAdvanceAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeEnumeratorAsync();
    }

    // -----------------------------------------------------------------------
    //  Internal
    // -----------------------------------------------------------------------

    private async Task ResetAndAdvanceAsync()
    {
        await DisposeEnumeratorAsync();

        _source = _sourceFactory(_userFilters!);
        _enumerator = _source.EnumerateAsync().GetAsyncEnumerator();

        Score = QuizScore.Empty;
        _history.Clear();
        SkippedCount = 0;
        IsFinished = false;
        Current = null;

        await AdvanceAsync();
    }

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

            var next = _enumerator.Current;
            if (IsPassPosition(next))
            {
                // Auto-skip silently — the user never saw this position.
                continue;
            }

            Current = next;
            break;
        }

        StateChanged?.Invoke();
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
        var board = BoardState.FromMop(data.Position.Mop);
        var dice = data.Decision.Dice;
        var legal = MoveGenerator.GeneratePlays(board, dice[0], dice[1]);
        // BgMoveGen's no-legal-play sentinel: a single Play with zero moves
        // (the dice are forfeited). Empty `legal` is not used.
        return legal.Count == 1 && legal[0].Count == 0;
    }
}

/// <summary>
/// Factory delegate for constructing the active <see cref="IProblemSetSource"/>
/// from a user-supplied filter set. Phase 1's <c>Program.cs</c> registers a
/// closure over <see cref="ServerDiskProblemSetSource"/>; tests substitute a
/// fake source via the same delegate shape.
/// </summary>
public delegate IProblemSetSource ProblemSetSourceFactory(DecisionFilterSet filters);
