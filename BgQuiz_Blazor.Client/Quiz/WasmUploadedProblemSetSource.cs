namespace BgQuiz_Blazor.Client.Quiz;

using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
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
/// <b>Re-iterability.</b> The source holds the file <em>bytes</em>
/// (<see cref="PickedFile.Bytes"/>), not open streams, and mints a fresh
/// <see cref="MemoryStream"/> positioned at zero for every
/// <see cref="EnumerateAsync"/> call. The stream iterator reads each stream
/// exactly once, forward (see <see cref="XgFileStream"/>); buffering up front
/// is what lets a Restart re-enumerate the same set without the streams having
/// been consumed.
/// </para>
///
/// <para>
/// <see cref="Count"/> is null for the same reason as the directory source —
/// the up-front count would require a full filtered pre-pass. Decision-type
/// admission is governed entirely by the supplied <paramref name="filters"/>;
/// this source injects no policy of its own. Per-file parse failures inside the
/// iterator are skipped and logged; a name missing its extension is a usage
/// error the iterator rejects when it reaches that entry.
/// </para>
/// </summary>
internal sealed class WasmUploadedProblemSetSource : IProblemSetSource
{
    private readonly IReadOnlyList<PickedFile> _files;
    private readonly FilteredDecisionIterator _iterator;

    /// <summary>
    /// Construct a source over <paramref name="files"/> applying
    /// <paramref name="filters"/> on each enumeration. Per-file read failures in
    /// the underlying iterator are logged through a logger created from
    /// <paramref name="loggerFactory"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="files"/>, <paramref name="filters"/>, or
    /// <paramref name="loggerFactory"/> is null.
    /// </exception>
    public WasmUploadedProblemSetSource(
        IReadOnlyList<PickedFile> files,
        DecisionFilterSet filters,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _files = files;
        _iterator = new FilteredDecisionIterator(
            filters,
            loggerFactory.CreateLogger<FilteredDecisionIterator>());
    }

    /// <inheritdoc />
    public string Name => _files.Count switch
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
        // Fresh MemoryStreams per enumeration keep the source re-iterable: the
        // iterator reads each stream once, forward, from position zero, so the
        // buffered bytes must back a new stream on every pass. The caller owns
        // disposal; wrapping in `using` here would dispose the streams before the
        // lazy iterator reads them, so they are intentionally left to GC.
        var streams = _files.Select(f => new XgFileStream(f.FileName, new MemoryStream(f.Bytes)));

        foreach (var decision in _iterator.IterateXgStreamDiagrams(streams))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return decision;
            // Cooperative yield so a long synchronous run doesn't monopolise the
            // single WASM thread; also gives cancellation a chance between items.
            await Task.Yield();
        }
    }
}
