namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;
using Microsoft.Extensions.Logging;
using XgFilter_Lib;
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
/// <see cref="PickedProblemFolder"/> (<see cref="PickedProblemFolder.Parsed"/>),
/// so cache lifecycle <i>is</i> pick lifecycle: a re-pick or Clear nulls it
/// by construction, with no invalidation wiring to forget. This source is
/// the slot's only writer. The cached parse is <b>unfiltered</b> so any
/// filter config reuses it; the per-Start filters re-apply here, per
/// enumeration, via <see cref="DecisionFilterSet.Matches"/>.
/// </para>
///
/// <para>
/// <b>The parse carries its own source report</b> (halheinrich/backgammon#368).
/// What the cache stores is a <see cref="ParsedProblemSet"/>: the decisions
/// and, inseparably, the producer's <see cref="SourceReport"/> of the walk
/// that produced them — which files were attempted, which were rejected and
/// why. The facts exist exactly once per pick, at the parse, because every
/// later enumeration under any filter reads the cache and never walks the
/// files again; pairing them with the decisions in one value at <i>both</i>
/// retention sites — the holder's slot and this source's own reference — is
/// what makes "stored without its report", "replaced by a partial report" and
/// "paired with another parse's decisions" unrepresentable. Each parsing
/// attempt gets a fresh report (a report serves one walk, by the producer's
/// contract), and <see cref="Report"/> reads the one belonging to the parse
/// this source draws from.
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
    private ParsedProblemSet? _parsed;

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

    /// <summary>
    /// The completed <see cref="SourceReport"/> of the parse this source draws
    /// from — the files attempted, the files rejected and why — or null until
    /// its first enumeration has resolved one (halheinrich/backgammon#368).
    /// It is <i>this source's</i> parse: adopted from the holder's cache when
    /// the pick was still current, or its own, so a source built against a
    /// since-superseded pick reports the walk over its own files and never
    /// whatever the holder holds now. A cache hit reuses the completed report
    /// stored with the parse; nothing here walks the files again, so nothing
    /// can clear, append to or replace it.
    /// </summary>
    internal SourceReport? Report => _parsed?.Report;

    /// <inheritdoc />
    public async IAsyncEnumerable<BgDecisionData> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var decisions = (await GetOrParseAsync(cancellationToken)).Decisions;

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
    /// This source's parse result, resolved in cheapest-first order: its own
    /// prior resolution, then the holder's cache (only while the pick it was
    /// built from is still current), then a full unfiltered parse — stored
    /// back to the holder, which drops it if the pick has been superseded.
    ///
    /// <para>
    /// <b>The report is the parse's own by-product.</b> Each parsing attempt
    /// hands the inner source a fresh <see cref="SourceReport"/> — the
    /// producer claims a report for one walk and refuses it a second, so a
    /// retry after a cancelled or thrown parse never offers the spent one —
    /// and the decisions and the report become one <see cref="ParsedProblemSet"/>
    /// only once the walk has finished. A cancelled or thrown parse reaches
    /// neither assignment below: no partial decisions and no partial report
    /// are ever retained or stored, at either site.
    /// </para>
    ///
    /// <para>
    /// <b>Cancellation is observed at this layer's own boundaries</b> — before
    /// the parse starts and again before its result is installed — not only
    /// inside the loops over yielded decisions. A walk whose every file was
    /// rejected yields nothing, so neither loop ever reads the token: it would
    /// finish, complete its report truthfully, and install an all-rejected
    /// result under a token cancelled before it began, which this leg's
    /// contract forbids (an interrupted parse installs neither half). The
    /// producer's report semantics are untouched: the report still records
    /// the walk it saw; it is this consumer that declines to publish it. A
    /// previously completed result — this source's own or the holder's — is
    /// served regardless, and never discarded.
    /// </para>
    /// </summary>
    private async ValueTask<ParsedProblemSet> GetOrParseAsync(
        CancellationToken cancellationToken)
    {
        if (_parsed is { } own) return own;

        if (_folder.PickGeneration == _generation && _folder.Parsed is { } cached)
        {
            _parsed = cached;
            return cached;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var report = new SourceReport();
        var parsing = ImmutableArray.CreateBuilder<BgDecisionData>();
        await foreach (var decision in _inner.EnumerateAsync(report, cancellationToken))
        {
            parsing.Add(decision);
        }

        // The publication boundary: a cancellation requested during a walk that
        // yielded nothing arrives here with a complete report and no earlier
        // check having run.
        cancellationToken.ThrowIfCancellationRequested();
        var parsed = new ParsedProblemSet(parsing.ToImmutable(), report);
        _parsed = parsed;
        _folder.StoreParsed(_generation, parsed);
        return parsed;
    }
}
