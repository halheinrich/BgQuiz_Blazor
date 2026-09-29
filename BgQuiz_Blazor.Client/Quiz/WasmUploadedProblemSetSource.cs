namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using Microsoft.Extensions.Logging;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;

/// <summary>
/// <see cref="IProblemSetSource"/> backed by a set of browser-picked XG-format
/// files (<c>.xg</c> match files / <c>.xgp</c> position files) already buffered
/// into memory — the directory-free, in-browser counterpart to the old
/// server-disk source. Each enumeration runs the buffered files through
/// <see cref="FilteredDecisionIterator.IterateXgStreamDiagrams"/>; the files
/// are parsed entirely client-side and never leave the browser.
///
/// <para>
/// <b>Re-iterability.</b> The source holds the picked files' buffered
/// <em>bytes</em> (<see cref="PickedFile.Bytes"/>), not open streams, and
/// opens a fresh read of each (<see cref="PickedFile.OpenRead"/>, positioned at
/// zero, sharing the bytes without copying them) for every
/// <see cref="EnumerateAsync"/> call. The stream iterator reads each stream
/// exactly once, forward (see <see cref="XgFileStream"/>); buffering up front
/// is what lets a Restart re-enumerate the same set without the streams having
/// been consumed.
/// </para>
///
/// <para>
/// <b>The ranking is the quiz's.</b> Which play is best — and so whether a
/// decision's player "erred by more than x" — is a ranking's
/// (<c>SPEC-scoring.md</c> §2a), and the iterator filters under the one it is
/// constructed with. The source takes the caller's rather than any default, so
/// the pool is drawn under the ranking the quiz scores with.
/// </para>
///
/// <para>
/// <see cref="Count"/> is null for the same reason as the directory source —
/// the up-front count would require a full filtered pre-pass. Decision-type
/// admission is governed entirely by the supplied <c>filters</c>;
/// this source injects no policy of its own. Per-file parse failures inside the
/// iterator are skipped and logged; a name missing its extension is a usage
/// error the iterator rejects when it reaches that entry.
/// </para>
/// </summary>
internal sealed class WasmUploadedProblemSetSource : IProblemSetSource
{
    private readonly ImmutableArray<PickedFile> _files;
    private readonly FilteredDecisionIterator _iterator;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Construct a source over <paramref name="files"/> applying
    /// <paramref name="filters"/> under <paramref name="ranking"/> on each
    /// enumeration. Per-file read failures in the underlying iterator are logged
    /// through a logger created from <paramref name="loggerFactory"/>.
    /// </summary>
    /// <param name="files">The buffered picked files (bytes + extension-bearing names).</param>
    /// <param name="filters">The filter pipeline applied on every enumeration.</param>
    /// <param name="ranking">
    /// The quiz's ranking — the one the filters' error range reads a player's
    /// error under. The iterator refuses an undefined one.
    /// </param>
    /// <param name="loggerFactory">Creates the inner iterator's logger (the factory keeps the inner type out of this contract).</param>
    /// <param name="clock">
    /// The monotonic time source pacing the enumeration's cooperative yields
    /// (see <see cref="CooperativeYielder"/>). Pure pacing — it never affects
    /// which decisions flow or in what order. Production passes the DI
    /// <see cref="TimeProvider.System"/>; tests may pass a fake to pin the
    /// yield policy.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="filters"/>, <paramref name="loggerFactory"/>, or
    /// <paramref name="clock"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="files"/> is a default array.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ranking"/> is not a defined ranking — the iterator's
    /// refusal, raised here at construction.
    /// </exception>
    public WasmUploadedProblemSetSource(
        ImmutableArray<PickedFile> files,
        DecisionFilterSet filters,
        PlayRanking ranking,
        ILoggerFactory loggerFactory,
        TimeProvider clock)
    {
        if (files.IsDefault)
            throw new ArgumentException("A default array holds no files; none is an empty one.", nameof(files));
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(clock);

        _files = files;
        _iterator = new FilteredDecisionIterator(
            filters,
            ranking,
            loggerFactory.CreateLogger<FilteredDecisionIterator>());
        _clock = clock;
    }

    /// <inheritdoc />
    public string Name => _files.Length switch
    {
        0 => "No files",
        1 => _files[0].FileName,
        var n => $"{n} files",
    };

    /// <inheritdoc />
    public int? Count => null;

    /// <inheritdoc />
    public async IAsyncEnumerable<BgDecisionData> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // A fresh read per enumeration keeps the source re-iterable: the
        // iterator reads each stream once, forward, from position zero, so the
        // buffered bytes must back a new stream on every pass. The caller owns
        // disposal; wrapping in `using` here would dispose the streams before the
        // lazy iterator reads them, so they are intentionally left to GC.
        var streams = _files.Select(f => new XgFileStream(f.FileName, f.OpenRead()));

        // Time-budgeted cooperative yielding (one gate per enumeration; the
        // budget window starts here): frequent enough that the browser can
        // repaint — e.g. the busy cursor — during a long materialization,
        // rare enough that the yields aren't themselves the bottleneck. The
        // old per-item Task.Yield paid an event-loop round-trip for every
        // decision, which dominated large parses.
        var yielder = new CooperativeYielder(_clock);
        foreach (var decision in _iterator.IterateXgStreamDiagrams(streams))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return decision;
            await yielder.YieldIfDueAsync();
        }
    }
}
