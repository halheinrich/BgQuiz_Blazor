using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The transition-gate overlap suite: a second gesture arriving while a
/// Start / Restart / Continue / Skip transition is in flight must
/// <b>no-op</b> — not queue, not throw, and above all not touch the one live
/// enumerator (an overlapped <c>MoveNextAsync</c> faults on a thread-pool
/// continuation no page can catch, terminating the WASM runtime; that crash
/// is the dogfooding finding this gate exists to close).
///
/// <para>
/// Overlap windows are frozen deterministically: <see cref="GatedProblemSetSource"/>
/// suspends the controller inside an awaited <c>MoveNextAsync</c>, and
/// <see cref="FakeDecisionStatsSink.RecordGate"/> suspends it inside the
/// awaited stats fold (where <c>Review</c> is still set — the double-fold
/// window). The gate flips <c>IsBusy</c> synchronously before its first
/// await, so a second call issued while the first task is pending observes
/// the gate deterministically regardless of where the first's continuation
/// has progressed.
/// </para>
/// </summary>
public class QuizControllerOverlapTests
{
    private static Play BestPlay() => TestFixtures.MakePlay((8, 5), (8, 5));
    private static Play AltPlay() => TestFixtures.MakePlay((13, 11), (11, 8));

    private static BgDecisionData Decision() => TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());

    /// <summary>
    /// Controller over a <see cref="GatedProblemSetSource"/> holding
    /// <paramref name="items"/>, with the factory-invocation count exposed so
    /// tests can pin that an overlapped Start/Restart never built a source.
    /// </summary>
    private static QuizController MakeGated(
        out GatedProblemSetSource source, out FakeDecisionStatsSink sink,
        out Func<int> factoryCalls, params BgDecisionData[] items)
    {
        var gated = new GatedProblemSetSource(items);
        source = gated;
        sink = new FakeDecisionStatsSink();
        var calls = 0;
        factoryCalls = () => calls;
        return new QuizController((_, _) => { calls++; return gated; }, sink, TimeProvider.System);
    }

    // -----------------------------------------------------------------------
    //  Overlapped Start / Restart
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_WhileStartPending_NoOpsWithBusyOutcome()
    {
        var c = MakeGated(out var source, out _, out var factoryCalls, Decision());

        var first = c.StartAsync(new FilterConfig(), QuizMix.Empty); // suspends at the gated first advance
        Assert.False(first.IsCompleted);
        Assert.True(c.IsBusy);

        var second = await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        Assert.Equal(QuizStartOutcome.Busy, second);

        source.ReleaseNext();
        Assert.Equal(QuizStartOutcome.Started, await first);
        Assert.False(c.IsBusy);           // gate released on completion
        Assert.NotNull(c.Current);
        Assert.Equal(1, factoryCalls());  // the overlapped Start never built a source
        Assert.Equal(1, source.EnumerateCallCount);
    }

    [Fact]
    public async Task RestartAsync_WhileStartPending_NoOpsWithBusyOutcome()
    {
        var c = MakeGated(out var source, out _, out var factoryCalls, Decision());

        var first = c.StartAsync(new FilterConfig(), QuizMix.Empty);

        // No throw either: an overlap is an outcome, not the never-started
        // caller bug — the gate is checked first.
        Assert.Equal(QuizStartOutcome.Busy, await c.RestartAsync());

        source.ReleaseNext();
        Assert.Equal(QuizStartOutcome.Started, await first);
        Assert.Equal(1, factoryCalls());
    }

    [Fact]
    public async Task RestartAsync_WhileRestartPending_NoOpsWithBusyOutcome()
    {
        var c = MakeGated(out var source, out _, out var factoryCalls, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        var first = c.RestartAsync(); // suspends at the re-enumeration's gated first advance
        Assert.True(c.IsBusy);

        Assert.Equal(QuizStartOutcome.Busy, await c.RestartAsync());

        source.ReleaseNext();
        Assert.Equal(QuizStartOutcome.Started, await first);
        Assert.False(c.IsBusy);
        Assert.Equal(2, factoryCalls()); // Start + the one Restart that ran
    }

    [Fact]
    public async Task StartAsync_WhileContinuePending_NoOpsWithBusyOutcome()
    {
        // The double-Start hazard mid-advance: without the gate a second
        // Start disposes the enumerator the pending Continue is awaiting.
        var c = MakeGated(out var source, out _, out var factoryCalls, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());

        var pending = c.ContinueAsync(); // suspends at the gated advance to the second problem

        Assert.Equal(QuizStartOutcome.Busy, await c.StartAsync(new FilterConfig(), QuizMix.Empty));

        source.ReleaseNext();
        await pending;
        Assert.Equal(1, factoryCalls());
        Assert.NotNull(c.Current);
    }

    // -----------------------------------------------------------------------
    //  Overlapped Continue / Skip / Submit / Redo
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ContinueAsync_DoubleGestureDuringFold_FoldsStatsExactlyOnce()
    {
        // The double-fold window: the first Continue is suspended inside the
        // awaited stats fold, Review still set — exactly where the Quiz
        // page's dice-click + Continue-button double-binding can land a
        // second Continue. It must no-op: one fold, one advance.
        var c = MakeGated(out var source, out var sink, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());

        var foldGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sink.RecordGate = foldGate.Task;

        var first = c.ContinueAsync(); // suspended inside RecordAsync; Review still set
        Assert.True(c.IsBusy);

        await c.ContinueAsync();       // the overlapped gesture — must no-op

        foldGate.SetResult();
        source.ReleaseNext();          // then let the advance through
        await first;

        Assert.Equal(1, sink.TotalFolds);
        Assert.Single(c.History);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Null(c.Review);
        Assert.NotNull(c.Current);
        Assert.False(c.IsBusy);
    }

    [Fact]
    public async Task SkipCurrentAsync_DuringPendingAdvance_NoOps()
    {
        // Mid-advance, Current still points at the outgoing problem and
        // Review is already null — both state guards stale-pass, so the busy
        // gate is the guard that actually holds.
        var c = MakeGated(out var source, out _, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());

        var pending = c.ContinueAsync(); // suspends at the gated advance

        await c.SkipCurrentAsync();      // must no-op

        Assert.Equal(0, c.SkippedCount);

        source.ReleaseNext();
        await pending;
        Assert.Equal(0, c.SkippedCount);
        Assert.NotNull(c.Current);
    }

    [Fact]
    public async Task SubmitPlay_DuringPendingAdvance_NoOps()
    {
        var c = MakeGated(out var source, out _, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());

        var pending = c.ContinueAsync(); // suspends at the gated advance

        c.SubmitPlay(BestPlay());        // must no-op — the outgoing problem is not re-scorable

        // History is only ever touched by Submit/Redo, both busy-gated, so
        // this holds deterministically even while the advance is in flight.
        // (Review is cleared by the pending Continue's own continuation at an
        // indeterminate point, so it is only asserted after completion.)
        Assert.Single(c.History);

        source.ReleaseNext();
        await pending;
        Assert.Single(c.History);
        Assert.Null(c.Review);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
    }

    [Fact]
    public async Task RedoAsync_DuringPendingFold_NoOps()
    {
        // A Continue suspended in the fold still has Review set; a Redo there
        // would pop the entry the fold is recording. The busy gate refuses it.
        var c = MakeGated(out var source, out var sink, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        c.SubmitPlay(BestPlay());

        var foldGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sink.RecordGate = foldGate.Task;

        var pending = c.ContinueAsync(); // suspended inside RecordAsync; Review still set

        await c.RedoAsync();             // must no-op

        Assert.Single(c.History);        // not popped

        foldGate.SetResult();
        source.ReleaseNext();
        await pending;
        Assert.Equal(1, sink.TotalFolds);
        Assert.Single(c.History);
    }

    // -----------------------------------------------------------------------
    //  Gate release
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Gate_ReleasesAfterCompletion_NextTransitionRuns()
    {
        var c = MakeGated(out var source, out _, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);
        Assert.False(c.IsBusy);

        source.ReleaseNext();
        await c.SkipCurrentAsync(); // a fresh transition passes the released gate

        Assert.Equal(1, c.SkippedCount);
        Assert.False(c.IsBusy);
    }

    [Fact]
    public async Task Gate_ReleasesWhenFactoryThrows_NextStartRuns()
    {
        var fake = new FakeProblemSetSource([Decision()]);
        var throwNext = true;
        var c = new QuizController(
            (_, _) => throwNext
                ? throw new InvalidOperationException("boom")
                : fake,
            new FakeDecisionStatsSink(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => c.StartAsync(new FilterConfig(), QuizMix.Empty));

        Assert.False(c.IsBusy); // released via finally, not wedged

        throwNext = false;
        Assert.Equal(QuizStartOutcome.Started, await c.StartAsync(new FilterConfig(), QuizMix.Empty));
        Assert.NotNull(c.Current);
    }

    [Fact]
    public async Task Gate_ReleasesWhenEnumeratorThrows_NextStartRuns()
    {
        var good = new FakeProblemSetSource([Decision()]);
        var throwNext = true;
        var c = new QuizController(
            (_, _) => throwNext ? new ThrowingProblemSetSource() : good,
            new FakeDecisionStatsSink(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => c.StartAsync(new FilterConfig(), QuizMix.Empty));

        Assert.False(c.IsBusy);

        throwNext = false;
        Assert.Equal(QuizStartOutcome.Started, await c.StartAsync(new FilterConfig(), QuizMix.Empty));
    }

    // -----------------------------------------------------------------------
    //  Busy observability
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IsBusy_ObservableThroughStateChanged_OnAndOffAroundTransition()
    {
        // Pages drive their busy affordances from IsBusy at each StateChanged:
        // a gated transition must surface as exactly [busy, not-busy].
        var c = MakeGated(out var source, out _, out _, Decision());
        var snapshots = new List<bool>();
        c.StateChanged += () => snapshots.Add(c.IsBusy);

        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty);

        Assert.Equal(new[] { true, false }, snapshots);
    }

    /// <summary>Source whose enumeration faults on the first advance.</summary>
    private sealed class ThrowingProblemSetSource : IProblemSetSource
    {
        public string Name => "Throwing";
        public int? Count => null;

        public async IAsyncEnumerable<BgDecisionData> EnumerateAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new InvalidOperationException("enumeration faulted");
#pragma warning disable CS0162 // unreachable — required for the iterator shape
            yield break;
#pragma warning restore CS0162
        }
    }
}
