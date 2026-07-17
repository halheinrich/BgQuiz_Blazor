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
/// <see cref="RestartAsync"/>.
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
/// it.
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
/// </summary>
internal sealed class QuizController : IAsyncDisposable
{
    private readonly ProblemSetSourceFactory _sourceFactory;
    private readonly List<SubmittedPlay> _history = [];
    private readonly List<SubmittedCubeAction> _cubeHistory = [];

    private DecisionFilterSet? _filterPipeline;
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

    /// <summary>
    /// The scored outcome of the just-submitted problem, set by Submit and
    /// cleared by <see cref="ContinueAsync"/> (and on start / restart). Non-null
    /// marks the <i>review</i> state: <see cref="Current"/> still points at the
    /// answered problem, and the page shows the solution view rather than the
    /// entry form. Null in the <i>answering</i> state and after finish.
    /// </summary>
    public ProblemReview? Review { get; private set; }

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

    /// <summary>Raised after every state transition so observing pages can re-render.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Begin a fresh quiz against <paramref name="userConfig"/>. Materializes
    /// the user's <see cref="FilterConfig"/> into a <see cref="DecisionFilterSet"/>
    /// owned entirely by this controller and advances to the first non-pass
    /// problem. Resets score / histories / skipped-count.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="userConfig"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="userConfig"/> contains a malformed value — propagated
    /// from <see cref="FilterConfig.Build"/>.
    /// </exception>
    public async Task StartAsync(FilterConfig userConfig)
    {
        ArgumentNullException.ThrowIfNull(userConfig);

        // Build a fresh, controller-owned pipeline from the immutable DTO. The
        // user's DecisionType choice governs decision-type admission; the
        // controller adds no filter of its own. Restart re-uses this pipeline
        // without re-building.
        _filterPipeline = userConfig.Build();

        await ResetAndAdvanceAsync();
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
        if (Current is null || IsFinished || Review is not null) return;

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
                Current.Id,
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
        if (Current is null || IsFinished || Review is not null) return;

        var d = Current.Decision;
        var submitted = new SubmittedCubeAction(
            Current.Id,
            answer,
            d.DoublerActionError(answer.Doubler),
            d.TakerActionError(answer.Taker),
            answer.Doubler == d.BestDoublerAction,
            answer.Taker == d.BestTakerAction);
        _cubeHistory.Add(submitted);
        Score = Score.Plus(submitted);
        Review = new ProblemReview.Cube(
            submitted.DoublerEquityLoss,
            submitted.TakerEquityLoss,
            submitted.DoublerCorrect,
            submitted.TakerCorrect);

        StateChanged?.Invoke();
    }

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
        if (Review is null) return Task.CompletedTask;

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
    /// <see cref="Review"/> is null). This is the only <i>forward</i> path out
    /// of review — see <see cref="RedoAsync"/> for the backward one; exhausting
    /// the source here flips <see cref="IsFinished"/>.
    /// </summary>
    public async Task ContinueAsync()
    {
        if (Review is null) return;
        Review = null;
        await AdvanceAsync();
    }

    /// <summary>
    /// Skip the current problem without recording; advance immediately (no
    /// review). No-op outside the <i>answering</i> state — before start, after
    /// finish, or while a <see cref="Review"/> is showing.
    /// </summary>
    public async Task SkipCurrentAsync()
    {
        if (Current is null || IsFinished || Review is not null) return;
        SkippedCount++;
        await AdvanceAsync();
    }

    /// <summary>
    /// Restart the quiz from the beginning of the source using the existing
    /// (already-augmented) filter pipeline. No-op when no quiz has been started.
    /// </summary>
    public async Task RestartAsync()
    {
        if (_filterPipeline is null) return;
        await ResetAndAdvanceAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeEnumeratorAsync();
    }

    // -----------------------------------------------------------------------
    //  Internal
    // -----------------------------------------------------------------------

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

    private async Task ResetAndAdvanceAsync()
    {
        await DisposeEnumeratorAsync();

        _source = _sourceFactory(_filterPipeline!);
        _enumerator = _source.EnumerateAsync().GetAsyncEnumerator();

        Score = QuizScore.Empty;
        _history.Clear();
        _cubeHistory.Clear();
        SkippedCount = 0;
        IsFinished = false;
        Current = null;
        Review = null;

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
/// Factory delegate for constructing the active <see cref="IProblemSetSource"/>
/// from a user-supplied filter set. The client's <c>Program.cs</c> binds this to
/// the in-browser source for the current run (the picked-files source, or a
/// bundled sample); tests substitute a fake source via the same delegate shape.
/// </summary>
internal delegate IProblemSetSource ProblemSetSourceFactory(DecisionFilterSet filters);
