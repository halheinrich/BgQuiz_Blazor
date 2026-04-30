namespace BgQuiz_Blazor.Quiz;

using BgDataTypes_Lib;
using BgGame_Lib;
using BgMoveGen;
using Microsoft.Extensions.Options;
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
/// <b>Status:</b> <see cref="SubmitPlayAsync"/> is currently blocked on a
/// cross-submodule arc that adds <c>Play</c> to <see cref="PlayCandidate"/>;
/// see that method's xmldoc for the session sequence. The rest of the state
/// machine (<see cref="StartAsync"/>, <see cref="SkipCurrentAsync"/>,
/// <see cref="RestartAsync"/>, <see cref="AdvanceAsync"/>, auto-pass detection)
/// is unblocked.
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
    private readonly IOptions<QuizOptions> _options;
    private readonly List<SubmittedPlay> _history = [];

    private DecisionFilterSet? _userFilters;
    private ServerDiskProblemSetSource? _source;
    private IAsyncEnumerator<BgDecisionData>? _enumerator;

    public QuizController(IOptions<QuizOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Source name (directory name) once started; null otherwise.</summary>
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
    /// Begin a fresh quiz against <paramref name="userFilters"/> and the
    /// configured <c>Quiz:ProblemSetDirectory</c>. Augments the filter set
    /// with Phase 1's CheckerPlaysOnly policy. Resets score / history /
    /// skipped-count and advances to the first non-pass problem.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>Quiz:ProblemSetDirectory</c> is not configured.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">
    /// The configured directory does not exist on disk.
    /// </exception>
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
    /// Score and advance. <b>BLOCKED — not implemented.</b>
    ///
    /// <para>
    /// Matching the user's submitted <see cref="Play"/> against the position's
    /// <see cref="PlayCandidate"/>s requires a structural comparison via
    /// <see cref="Play.DeduplicationKey"/>. <see cref="PlayCandidate"/> currently
    /// carries only <c>MoveNotation</c> (string). String equality on the
    /// formatter output is unreliable — different strings can represent the
    /// same logical play, producing false-negative misses on score.
    /// </para>
    ///
    /// <para>
    /// Resolution: the cross-submodule arc tracked in the umbrella
    /// <c>INSTRUCTIONS.md</c> Deferred section, per <c>CLAUDE.md</c>
    /// "API breakage bias":
    /// <list type="number">
    ///   <item>BgDataTypes_Lib — add <c>Play Play { get; init; }</c> to
    ///         <see cref="PlayCandidate"/> (introduces a BgMoveGen project
    ///         reference); change <c>EquityLoss</c> convention from
    ///         "null = best play" to "0.0 = best play" (type becomes
    ///         non-nullable).</item>
    ///   <item>ConvertXgToJson_Lib — populate <c>Play</c> and
    ///         <c>EquityLoss = 0.0</c> at the construction site
    ///         (<c>XgDecisionIterator</c> ~line 346).</item>
    ///   <item>BackgammonDiagram_Lib — sweep for <c>EquityLoss == null</c>
    ///         consumers in the analysis-panel renderer.</item>
    ///   <item>ExtractFromXgToCsv — verify CSV output handling of the new
    ///         convention.</item>
    ///   <item>Umbrella — coordinated submodule pointer bump.</item>
    ///   <item>BgQuiz_Blazor (this) — replace this method body with a
    ///         direct <see cref="Play.DeduplicationKey"/> lookup over
    ///         <c>PlayCandidate.Play</c>; drop the <c>?? 0.0</c>
    ///         unwrapping.</item>
    /// </list>
    /// </para>
    /// </summary>
    public Task SubmitPlayAsync(Play play)
    {
        _ = play;
        throw new NotImplementedException(
            "SubmitPlayAsync is blocked on the BgDataTypes_Lib cross-submodule " +
            "arc that adds Play to PlayCandidate. See class xmldoc for the " +
            "session sequence.");
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
        var dir = _options.Value.ProblemSetDirectory;
        if (string.IsNullOrWhiteSpace(dir))
            throw new InvalidOperationException(
                "Quiz:ProblemSetDirectory is not configured.");

        await DisposeEnumeratorAsync();

        _source = new ServerDiskProblemSetSource(dir, _userFilters!);
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
        return legal.Count == 0;
    }
}
