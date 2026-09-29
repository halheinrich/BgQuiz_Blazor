namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;
using Microsoft.Extensions.Logging;
using XgFilter_Lib.Filtering;

/// <summary>
/// The production <see cref="IProblemSetSource"/>: parse the picked files
/// <b>once</b>, then serve every Start/Restart by filtering the cached
/// decisions in memory. Measured against v1.0.4, every shuffled or weighted
/// Start re-parsed the corpus from the picked bytes (~7.5&#160;s warm); with
/// the cache only the first Start after a pick parses — repeats are
/// milliseconds.
///
/// <para>
/// <b>Cache home &amp; lifecycle.</b> The cache slot lives on
/// <see cref="PickedProblemFolder"/> (<see cref="PickedProblemFolder.ParsedDecisions"/>),
/// so cache lifecycle <i>is</i> pick lifecycle: a re-pick or Clear nulls it
/// by construction, with no invalidation wiring to forget. This source is
/// the slot's only writer. The cached parse is <b>unfiltered</b> so any
/// filter config reuses it; the per-Start filters re-apply here, per
/// enumeration, via <see cref="DecisionFilterSet.Matches"/>.
/// </para>
///
/// <para>
/// <b>The filter pass is the quiz's ranking's; the cache is no ranking's.</b>
/// A record is filtered through its view for the ranking this source was built
/// with (<see cref="BgDecisionData.ViewFor"/>), because "erred by more than x"
/// reads the player's error, and which play is best is a ranking's
/// (<c>SPEC-scoring.md</c> §2a). The parse itself filters nothing, and the
/// records it yields depend on no ranking, so one cached parse serves every
/// Start whatever ranking each is started with. The parse is still handed this
/// source's ranking — the iterator requires one, and a quiz's operations take
/// its ranking, never a producer's default.
/// </para>
///
/// <para>
/// <b>Why post-hoc <c>Matches</c> equals filter-during-parse.</b> The
/// streaming iterator's other filter hooks are contractually pure early-exit
/// hints: <c>IMatchFilter.ShouldSkipMatch</c>/<c>ShouldSkipGame</c> may vote
/// to skip only when <i>no row inside can match</i>, and
/// <c>IDecisionFilter.ShouldAdvanceGame</c>/<c>ShouldAdvanceMatch</c> only
/// when <i>no further row can match</i> — every row they cut would fail
/// <c>Matches</c> anyway, so filtering the unfiltered parse yields exactly
/// the streamed set. (The unfiltered parse forgoes those skip
/// optimizations once; that single full parse is precisely what the cache
/// amortizes.)
/// </para>
///
/// <para>
/// <b>Staleness.</b> Files and <see cref="PickedProblemFolder.PickGeneration"/>
/// are captured at construction (factory invocation = Start time, the
/// established read-live-at-Start discipline). The holder's cache is
/// consulted only while the generation still matches; a parse stores back
/// through <see cref="PickedProblemFolder.StoreParsed"/>, which drops it if
/// the pick has been superseded meanwhile (the pick gesture is async, so it
/// can complete inside a Start's own await points). The source also keeps
/// its own reference to whatever it parsed or adopted, so a Restart after a
/// mid-quiz re-pick still replays <i>this quiz's</i> files without
/// re-parsing and without polluting the new pick's cache.
/// </para>
///
/// <para>
/// The parse delegates to <see cref="WasmUploadedProblemSetSource"/> with an
/// empty <see cref="DecisionFilterSet"/> (admits every row) — the stream
/// sources stay stream-pure; caching is entirely this app-side layer. Both
/// the parse and the filter pass yield cooperatively on the shared
/// time-budget policy (<see cref="CooperativeYielder"/>), so the busy cursor
/// keeps painting either way. <see cref="Count"/> is null (the filtered
/// count would need the filter pass this enumeration is about to do);
/// <see cref="Name"/> delegates to the inner source's naming rule.
/// </para>
/// </summary>
internal sealed class CachedProblemSetSource : IProblemSetSource
{
    private readonly PickedProblemFolder _folder;
    private readonly DecisionFilterSet _filters;
    private readonly PlayRanking _ranking;
    private readonly TimeProvider _clock;
    private readonly WasmUploadedProblemSetSource _inner;
    private readonly int _generation;
    private ImmutableArray<BgDecisionData>? _decisions;

    /// <summary>
    /// Construct a source over <paramref name="folder"/>'s current pick,
    /// applying <paramref name="filters"/> under <paramref name="ranking"/> on
    /// each enumeration. The picked files and generation are captured now; the
    /// parse itself is deferred to the first enumeration.
    /// </summary>
    /// <param name="folder">The picked-folder holder — supplies the files and carries the cross-Start parse cache.</param>
    /// <param name="filters">The filter pipeline applied on every enumeration (over the cached, unfiltered parse).</param>
    /// <param name="ranking">The quiz's ranking, which each record's filter view is built for.</param>
    /// <param name="loggerFactory">Forwarded to the parsing inner source for its per-file failure logging.</param>
    /// <param name="clock">Monotonic pacing clock for the cooperative yields (production: the DI system clock).</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ranking"/> is not a defined ranking.</exception>
    public CachedProblemSetSource(
        PickedProblemFolder folder,
        DecisionFilterSet filters,
        PlayRanking ranking,
        ILoggerFactory loggerFactory,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(filters);

        _folder = folder;
        _filters = filters;
        _ranking = ranking;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        // The inner source both parses (unfiltered) and owns the naming rule;
        // its ctor re-validates loggerFactory/clock, and its iterator refuses
        // an undefined ranking — here, at construction, rather than at the
        // first view built in the filter pass.
        _inner = new WasmUploadedProblemSetSource(
            folder.Files, new DecisionFilterSet(), ranking, loggerFactory, clock);
        _generation = folder.PickGeneration;
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public int? Count => null;

    /// <inheritdoc />
    public async IAsyncEnumerable<BgDecisionData> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var decisions = await GetOrParseAsync(cancellationToken);

        var yielder = new CooperativeYielder(_clock);
        foreach (var decision in decisions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_filters.Matches(decision.ViewFor(_ranking)))
            {
                yield return decision;
            }
            // Yield on the budget every iteration, matched or not: a sparse
            // filter over a large cache is CPU-bound in the misses.
            await yielder.YieldIfDueAsync();
        }
    }

    /// <summary>
    /// This source's decisions, resolved in cheapest-first order: its own
    /// prior resolution, then the holder's cache (only while the pick it was
    /// built from is still current), then a full unfiltered parse — stored
    /// back to the holder, which drops it if the pick has been superseded. A
    /// cancelled parse stores nothing (no partial caches).
    /// </summary>
    private async ValueTask<ImmutableArray<BgDecisionData>> GetOrParseAsync(
        CancellationToken cancellationToken)
    {
        if (_decisions is { } own) return own;

        if (_folder.PickGeneration == _generation && _folder.ParsedDecisions is { } cached)
        {
            _decisions = cached;
            return cached;
        }

        var parsing = ImmutableArray.CreateBuilder<BgDecisionData>();
        await foreach (var decision in _inner.EnumerateAsync(cancellationToken))
        {
            parsing.Add(decision);
        }

        var parsed = parsing.ToImmutable();
        _decisions = parsed;
        _folder.StoreParsed(_generation, parsed);
        return parsed;
    }
}
