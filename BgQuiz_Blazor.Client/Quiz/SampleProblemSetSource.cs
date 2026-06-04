namespace BgQuiz_Blazor.Client.Quiz;

using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;
using XgFilter_Lib.Filtering;

/// <summary>
/// <b>TEMPORARY</b> built-in sample source — a tiny in-memory set of decisions
/// that proves the migrated WebAssembly quiz flow end-to-end before the browser
/// file-picker source lands. It is deliberately self-contained (no file or
/// network I/O) so the page/controller migration can be verified in isolation,
/// and is <b>removed</b> when the user-file source
/// (<c>WasmUploadedProblemSetSource</c>) replaces it.
///
/// <para>
/// Carries one checker-play decision and one cube decision so both board-entry
/// components (<c>BackgammonPlayEntry</c> / <c>BackgammonCubeEntry</c>) render
/// under the WebAssembly runtime. Applies the supplied
/// <see cref="DecisionFilterSet"/> via <see cref="DecisionFilterSet.Matches"/>
/// on each enumeration, mirroring the real sources' filter contract so filter
/// selections (e.g. cube-only) behave consistently. <see cref="Count"/> is null
/// for the same reason as the real sources — filtering can drop items, so the
/// up-front count is unknown.
/// </para>
/// </summary>
internal sealed class SampleProblemSetSource : IProblemSetSource
{
    private readonly DecisionFilterSet _filters;

    /// <exception cref="ArgumentNullException"><paramref name="filters"/> is null.</exception>
    public SampleProblemSetSource(DecisionFilterSet filters)
    {
        _filters = filters ?? throw new ArgumentNullException(nameof(filters));
    }

    /// <inheritdoc />
    public string Name => "Built-in sample";

    /// <inheritdoc />
    public int? Count => null;

    /// <inheritdoc />
    public async IAsyncEnumerable<BgDecisionData> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var decision in BuildSampleDecisions())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_filters.Matches(decision))
                yield return decision;
            await Task.Yield();
        }
    }

    private static IEnumerable<BgDecisionData> BuildSampleDecisions()
    {
        yield return CheckerSample();
        yield return CubeSample();
    }

    /// <summary>Standard backgammon opening position in MOP form (26 entries).</summary>
    private static int[] StandardMop()
    {
        var m = new int[26];
        m[6] = 5; m[8] = 3; m[13] = 5; m[24] = 2;
        m[19] = -5; m[17] = -3; m[12] = -5; m[1] = -2;
        return m;
    }

    private static Play Play(params (int from, int to)[] moves)
    {
        var play = new Play();
        foreach (var (from, to) in moves)
            play.Add(new Move(from, to));
        return play;
    }

    /// <summary>
    /// Opening 3-1 checker decision: making the five-point (8/5 6/5) is best at
    /// zero loss; a running alternative carries a small equity loss.
    /// </summary>
    private static BgDecisionData CheckerSample() => new()
    {
        Id = new XgpDecisionId("sample-checker.xgp"),
        Position = new PositionData { Mop = StandardMop() },
        Decision = new DecisionData
        {
            Dice = [3, 1],
            Plays =
            [
                new PlayCandidate { Play = Play((8, 5), (6, 5)), EquityLoss = 0.0, MoveNotation = "8/5 6/5" },
                new PlayCandidate { Play = Play((24, 21), (24, 23)), EquityLoss = 0.12, MoveNotation = "24/21 24/23" },
            ],
            BestPlayIndex = 0,
        },
        Descriptive = new DescriptiveData { OnRollName = "Sample", OpponentName = "Opponent" },
    };

    /// <summary>
    /// Cube decision on the opening position where Double / Take is best at zero
    /// loss on both halves.
    /// </summary>
    private static BgDecisionData CubeSample() => new()
    {
        Id = new XgpDecisionId("sample-cube.xgp"),
        Position = new PositionData { Mop = StandardMop() },
        Decision = new DecisionData
        {
            IsCube = true,
            NoDoubleEquity = 0.5,
            DoubleTakeEquity = 0.7,
        },
        Descriptive = new DescriptiveData { OnRollName = "Sample", OpponentName = "Opponent" },
    };
}
