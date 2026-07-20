using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// In-memory <see cref="IProblemSetSource"/> whose enumeration is externally
/// gated: every item's <c>MoveNextAsync</c> suspends until the test grants a
/// permit via <see cref="ReleaseNext"/>. This is the overlap-suite double —
/// it lets a test freeze the controller mid-advance (inside an awaited
/// <c>MoveNextAsync</c>) and fire a second gesture into that window, pinning
/// that the transition gate no-ops it.
///
/// <para>
/// Only item yields are gated; the final exhausting <c>MoveNextAsync</c> (the
/// one returning false) completes without a permit — a test that wants the
/// last advance held open simply appends one more item and never releases it.
/// Continuations run asynchronously so awaiting test code observes
/// post-release state rather than racing the release call.
/// </para>
/// </summary>
internal sealed class GatedProblemSetSource : IProblemSetSource
{
    private readonly IReadOnlyList<BgDecisionData> _items;
    private readonly SemaphoreSlim _permits = new(0);

    public GatedProblemSetSource(IReadOnlyList<BgDecisionData> items, string name = "Gated")
    {
        _items = items;
        Name = name;
    }

    public string Name { get; }

    public int? Count => _items.Count;

    public int EnumerateCallCount { get; private set; }

    /// <summary>Allow the next <paramref name="count"/> gated item yields to proceed.</summary>
    public void ReleaseNext(int count = 1) => _permits.Release(count);

    public async IAsyncEnumerable<BgDecisionData> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnumerateCallCount++;
        foreach (var item in _items)
        {
            await _permits.WaitAsync(cancellationToken);
            yield return item;
        }
    }
}
