using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// In-memory <see cref="IProblemSetSource"/> that yields a fixed list each
/// time <see cref="EnumerateAsync"/> is called. Re-iterability is satisfied
/// trivially — enumeration walks the captured list from the start. Tracks
/// invocation count so tests can assert factory calls.
/// Constructing with <c>countKnown: false</c> simulates a streaming source
/// that declares no <see cref="Count"/> (the interface's null contract), for
/// tests of unknown-total behavior.
/// </summary>
internal sealed class FakeProblemSetSource : IProblemSetSource
{
    private readonly IReadOnlyList<BgDecisionData> _items;
    private readonly bool _countKnown;
    public string Name { get; }
    public int? Count => _countKnown ? _items.Count : null;
    public int EnumerateCallCount { get; private set; }

    public FakeProblemSetSource(
        IReadOnlyList<BgDecisionData> items, string name = "Fake", bool countKnown = true)
    {
        _items = items;
        Name = name;
        _countKnown = countKnown;
    }

    public async IAsyncEnumerable<BgDecisionData> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnumerateCallCount++;
        foreach (var item in _items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }
}
