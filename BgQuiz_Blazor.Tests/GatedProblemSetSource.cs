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
///
/// <para>
/// <b>Knowing the consumer is parked.</b> A transition started without being
/// awaited runs on its own schedule, so "the advance is now waiting on the
/// source" is not something a test can assume from having started it.
/// <see cref="DrawsRequested"/> says it: it counts the item draws that have
/// reached the gate, so once it reads <c>n</c> the consumer has asked for the
/// <c>n</c>-th item — having taken, and acted on, every item before it — and
/// goes no further until a permit is released. <see cref="WaitForDrawRequest"/>
/// is the wait on that.
/// </para>
/// </summary>
internal sealed class GatedProblemSetSource : IProblemSetSource
{
    private readonly IReadOnlyList<BgDecisionData> _items;
    private readonly SemaphoreSlim _permits = new(0);
    private int _drawsRequested;

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

    /// <summary>
    /// How many item draws have reached the gate, over every enumeration of
    /// this source: the 1-based number of the item the consumer last asked for.
    /// </summary>
    public int DrawsRequested => Volatile.Read(ref _drawsRequested);

    /// <summary>
    /// Block until the consumer has asked for the item numbered
    /// <paramref name="draw"/> (1-based, as <see cref="DrawsRequested"/> counts),
    /// failing the test if it never does. Returns with the consumer parked at
    /// the gate for that item, unless a permit for it was already released.
    /// </summary>
    public void WaitForDrawRequest(int draw) =>
        Assert.True(
            SpinWait.SpinUntil(() => DrawsRequested >= draw, TimeSpan.FromSeconds(10)),
            $"The consumer never asked for item {draw}; it asked for {DrawsRequested}.");

    public async IAsyncEnumerable<BgDecisionData> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnumerateCallCount++;
        foreach (var item in _items)
        {
            Interlocked.Increment(ref _drawsRequested);
            await _permits.WaitAsync(cancellationToken);
            yield return item;
        }
    }
}
