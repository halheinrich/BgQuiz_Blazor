namespace BgQuiz_Blazor.Quiz;

using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;
using Microsoft.Extensions.Logging;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;

/// <summary>
/// <see cref="IProblemSetSource"/> backed by a server-side directory of
/// XG-format files (<c>.xg</c> match files and <c>.xgp</c> position files),
/// walked through <see cref="FilteredDecisionIterator.IterateXgDirectoryDiagrams"/>.
///
/// <para>
/// Re-iterability is satisfied trivially: each call to <see cref="EnumerateAsync"/>
/// invokes the underlying iterator fresh, so two concurrent or sequential
/// enumerations are independent.
/// </para>
///
/// <para>
/// <see cref="Count"/> is null — computing it would mean a full pre-pass through
/// the (potentially large) filtered iterator. Consumers that want to display a
/// "Problem N" counter without "of M" can do so off the running enumerator.
/// </para>
///
/// <para>
/// Decision-type admission is governed by the supplied
/// <paramref name="filters"/> set — the controller materializes it from the
/// user's <c>FilterConfig</c>, including any <c>DecisionTypeFilter</c> the
/// user's choice implies. This source applies whatever set it is handed and
/// injects no policy of its own.
/// </para>
/// </summary>
public sealed class ServerDiskProblemSetSource : IProblemSetSource
{
    private readonly string _directory;
    private readonly FilteredDecisionIterator _iterator;

    /// <summary>
    /// Construct a source over <paramref name="directory"/> applying
    /// <paramref name="filters"/> on each enumeration. Per-file read failures
    /// inside the underlying iterator are logged through a
    /// <see cref="FilteredDecisionIterator"/> logger created from
    /// <paramref name="loggerFactory"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="filters"/> or <paramref name="loggerFactory"/> is null.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directory"/> does not exist on disk.</exception>
    public ServerDiskProblemSetSource(
        string directory,
        DecisionFilterSet filters,
        ILoggerFactory loggerFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(
                $"Problem-set directory not found: {directory}");

        _directory = directory;
        _iterator = new FilteredDecisionIterator(
            filters,
            loggerFactory.CreateLogger<FilteredDecisionIterator>());
    }

    /// <inheritdoc />
    public string Name => Path.GetFileName(
        _directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <inheritdoc />
    public int? Count => null;

    /// <inheritdoc />
    public async IAsyncEnumerable<BgDecisionData> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var decision in _iterator.IterateXgDirectoryDiagrams(_directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return decision;
            // Cooperative yield so a long synchronous run doesn't hog the
            // request thread; also gives cancellation a chance between items.
            await Task.Yield();
        }
    }
}
