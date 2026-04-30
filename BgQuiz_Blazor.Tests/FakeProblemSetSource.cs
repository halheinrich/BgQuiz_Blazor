using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// In-memory <see cref="IProblemSetSource"/> that yields a fixed list each
/// time <see cref="EnumerateAsync"/> is called. Re-iterability is satisfied
/// trivially — enumeration walks the captured list from the start. Tracks
/// invocation count so tests can assert factory calls.
/// </summary>
internal sealed class FakeProblemSetSource : IProblemSetSource
{
    private readonly IReadOnlyList<BgDecisionData> _items;
    public string Name { get; }
    public int? Count => _items.Count;
    public int EnumerateCallCount { get; private set; }

    public FakeProblemSetSource(IReadOnlyList<BgDecisionData> items, string name = "Fake")
    {
        _items = items;
        Name = name;
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
