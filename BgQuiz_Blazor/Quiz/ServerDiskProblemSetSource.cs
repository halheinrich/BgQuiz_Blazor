namespace BgQuiz_Blazor.Quiz;

using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;

/// <summary>
/// <see cref="IProblemSetSource"/> backed by a server-side directory of
/// <c>.xg</c> files, walked through <see cref="FilteredDecisionIterator.IterateXgDirectoryDiagrams"/>.
///
/// <para>
/// Re-iterability is satisfied trivially: each call to <see cref="EnumerateAsync"/>
/// invokes the underlying iterator factory fresh, so two concurrent or sequential
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
/// Cube-decision exclusion is the consumer's responsibility — typically a
/// <c>DecisionTypeFilter(CheckerPlaysOnly)</c> appended to <paramref name="filters"/>
/// before construction. This source does not inject one.
/// </para>
///
/// <para>
/// <b>Pitfall:</b> the underlying iterator currently enumerates <c>*.xg</c> only,
/// not <c>*.xgp</c>; this is a known XgFilter_Lib limitation tracked on the
/// umbrella Deferred list.
/// </para>
/// </summary>
public sealed class ServerDiskProblemSetSource : IProblemSetSource
{
    private readonly string _directory;
    private readonly DecisionFilterSet _filters;

    /// <summary>
    /// Construct a source over <paramref name="directory"/> applying
    /// <paramref name="filters"/> on each enumeration.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="filters"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directory"/> does not exist on disk.</exception>
    public ServerDiskProblemSetSource(string directory, DecisionFilterSet filters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(filters);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(
                $"Problem-set directory not found: {directory}");

        _directory = directory;
        _filters = filters;
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
        foreach (var decision in FilteredDecisionIterator.IterateXgDirectoryDiagrams(_directory, _filters))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return decision;
            // Cooperative yield so a long synchronous run doesn't hog the
            // request thread; also gives cancellation a chance between items.
            await Task.Yield();
        }
    }
}
