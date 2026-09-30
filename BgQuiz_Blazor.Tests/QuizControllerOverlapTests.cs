using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The transition-gate overlap suite: a second gesture arriving while a
/// Start / Restart / Continue / Skip / End-quiz transition is in flight must
/// <b>no-op</b> — not queue, not throw, and above all not touch the one live
/// enumerator (an overlapped <c>MoveNextAsync</c> faults on a thread-pool
/// continuation no page can catch, terminating the WASM runtime; that crash
/// is the dogfooding finding this gate exists to close).
///
/// <para>
/// Overlap windows are frozen deterministically: <see cref="GatedProblemSetSource"/>
/// suspends the controller inside an awaited <c>MoveNextAsync</c>, and
/// <see cref="FakeProblemStatsSink.RecordGate"/> suspends it inside the
/// awaited stats fold (where <c>Review</c> is still set — the double-fold
/// window). The gate flips <c>IsBusy</c> synchronously before its first
/// await, so a second call issued while the first task is pending observes
/// the gate deterministically regardless of where the first's continuation
/// has progressed.
/// </para>
/// </summary>
public class QuizControllerOverlapTests
{
    private static Play BestPlay() => TestFixtures.OpeningBest();
    private static Play AltPlay() => TestFixtures.OpeningAlternative();

    private static BgDecisionData Decision() => TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay());

    /// <summary>
    /// Controller over a <see cref="GatedProblemSetSource"/> holding
    /// <paramref name="items"/>, with the factory-invocation count exposed so
    /// tests can pin that an overlapped Start/Restart never built a source.
    /// </summary>
    private static QuizController MakeGated(
        out GatedProblemSetSource source, out FakeProblemStatsSink sink,
        out Func<int> factoryCalls, params BgDecisionData[] items)
    {
        var gated = new GatedProblemSetSource(items);
        source = gated;
        sink = new FakeProblemStatsSink();
        var calls = 0;
        factoryCalls = () => calls;
        return new QuizController((_, _, _) => { calls++; return TestFixtures.Composed(gated); }, sink, TimeProvider.System);
    }

    // -----------------------------------------------------------------------
    //  Overlapped Start / Restart
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_WhileStartPending_NoOpsWithBusyOutcome()
    {
        var c = MakeGated(out var source, out _, out var factoryCalls, Decision());

        var first = c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity); // suspends at the gated first advance
        Assert.False(first.IsCompleted);
        Assert.True(c.IsBusy);

        var second = await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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

        var first = c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        // No throw either: an overlap is an outcome, not the never-started
        // caller bug — the gate is checked first.
        Assert.Equal(QuizStartOutcome.Busy, await c.RestartAsync(PlayRanking.Equity));

        source.ReleaseNext();
        Assert.Equal(QuizStartOutcome.Started, await first);
        Assert.Equal(1, factoryCalls());
    }

    [Fact]
    public async Task RestartAsync_WhileRestartPending_NoOpsWithBusyOutcome()
    {
        var c = MakeGated(out var source, out _, out var factoryCalls, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        var first = c.RestartAsync(PlayRanking.Equity); // suspends at the re-enumeration's gated first advance
        Assert.True(c.IsBusy);

        Assert.Equal(QuizStartOutcome.Busy, await c.RestartAsync(PlayRanking.Equity));

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());

        var pending = c.ContinueAsync(); // suspends at the gated advance to the second problem

        Assert.Equal(QuizStartOutcome.Busy, await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity));

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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());

        var pending = c.ContinueAsync(); // suspends at the gated advance

        // Wait for the window the gate exists for. The pending Continue's
        // continuation runs on its own schedule; once the review is gone it has
        // moved on from the problem and is waiting on the source, and it cannot
        // go further until this test releases one. There the state guards
        // stale-pass — the outgoing problem is back in its answering state — so
        // an ungated submission would be taken, as practice on a completed
        // problem, and put a review up over a problem the run is leaving.
        Assert.True(
            SpinWait.SpinUntil(() => c.Review is null, TimeSpan.FromSeconds(10)),
            "The pending Continue never reached the gated advance.");

        c.SubmitPlay(BestPlay());        // must no-op — the outgoing problem is not re-scorable

        Assert.Null(c.Review);           // no review went up: the submission was refused
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);

        source.ReleaseNext();
        await pending;
        Assert.Null(c.Review);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
    }

    [Fact]
    public async Task SubmitPlay_DuringPendingSkip_NoOps()
    {
        // The window a pending Skip opens is sharper than a pending Continue's.
        // Skip completes nothing, so the problem it left is still the frontier,
        // still unresolved and in its answering state while the source is
        // asked: an ungated submission there would be live — an answer of
        // record on a problem the user has moved on from, written after this
        // advance's fold point and so never folded.
        var c = MakeGated(out var source, out var sink, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        var pending = c.SkipCurrentAsync();
        source.WaitForDrawRequest(2);    // moved on, and waiting on the source

        c.SubmitPlay(BestPlay());        // must no-op

        Assert.Null(c.Review);
        Assert.Equal(0, c.Score.PlayDecisions.Submitted);

        source.ReleaseNext();
        await pending;
        Assert.Equal(0, c.Score.PlayDecisions.Submitted);
        Assert.Equal(1, c.SkippedCount);
        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task SkipCurrentAsync_WhileItsDrawIsPending_CountsNothingUntilTheNextProblemLands()
    {
        // SPEC-quiz-history.md §5 (amended 2026-09-30): the skip count "does not
        // rise when Skip is pressed … the count rises when the next problem
        // lands". Until then the problem the Skip left is the frontier, still
        // unresolved, and nothing records that the press happened — so mid-draw
        // the controller reports what it reported before the press, the number
        // on screen included. The page-level pin, render by render, is
        // PageTests.Quiz_AdvancePending_DrawsTheProblemStillOnScreen_ThenLandsOnTheNext.
        var c = MakeGated(out var source, out _, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        var skipped = c.Current;

        var pending = c.SkipCurrentAsync();
        source.WaitForDrawRequest(2);

        Assert.True(c.IsBusy);
        Assert.Equal(0, c.SkippedCount);
        Assert.Same(skipped, c.Current);
        Assert.Equal(1, c.ProblemNumber);

        source.ReleaseNext();
        await pending;
        Assert.Equal(1, c.SkippedCount);
        Assert.NotSame(skipped, c.Current);
        Assert.Equal(2, c.ProblemNumber);
    }

    [Fact]
    public async Task RedoAsync_DuringPendingFold_NoOps()
    {
        // A Continue suspended in the fold still has Review set; a Redo there
        // would re-open a problem the run is already leaving — the fold would
        // complete, the advance would land, and the user would be answering the
        // NEXT problem with no visible break. The busy gate refuses it.
        //
        // Review is the observable, not the score: since halheinrich/backgammon#152
        // a Redo changes nothing of record, so asserting the score alone would
        // pass with the gate deleted.
        var c = MakeGated(out var source, out var sink, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());

        var foldGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sink.RecordGate = foldGate.Task;

        var pending = c.ContinueAsync(); // suspended inside RecordAsync; Review still set

        await c.RedoAsync();             // must no-op

        Assert.NotNull(c.Review);        // not re-opened — the pending Continue owns the flow
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);

        foldGate.SetResult();
        source.ReleaseNext();
        await pending;
        Assert.Equal(1, sink.TotalFolds);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task EndQuizAsync_DuringPendingAdvance_NoOps()
    {
        // End quiz retires the live enumerator, which is precisely what must
        // never happen underneath an in-flight MoveNextAsync — a dispose during
        // one faults on a thread-pool continuation no page can catch. Its state
        // guards stale-pass mid-advance (HasStarted is true and IsFinished is
        // still false), so the busy gate is the guard that actually holds.
        var c = MakeGated(out var source, out _, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());

        var pending = c.ContinueAsync(); // suspends at the gated advance

        await c.EndQuizAsync();          // must no-op

        Assert.False(c.IsFinished);

        source.ReleaseNext();
        await pending;
        Assert.False(c.IsFinished);      // the advance completed normally
        Assert.NotNull(c.Current);
        Assert.Equal(0, c.SkippedCount); // and no abandon was recorded
    }

    [Fact]
    public async Task EndQuizAsync_DuringPendingFold_NoOps()
    {
        // The other overlap window: a Continue suspended inside the stats fold
        // still has Review set, so an End quiz there would fold the same
        // submission a second time — the document has no Minus, and the gate is
        // what keeps the fold count at one.
        var c = MakeGated(out var source, out var sink, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        c.SubmitPlay(BestPlay());

        var foldGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sink.RecordGate = foldGate.Task;

        var pending = c.ContinueAsync(); // suspended inside RecordAsync; Review still set

        await c.EndQuizAsync();          // must no-op

        Assert.False(c.IsFinished);

        foldGate.SetResult();
        source.ReleaseNext();
        await pending;
        Assert.Equal(1, sink.TotalFolds);
    }

    // -----------------------------------------------------------------------
    //  Gate release
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Gate_ReleasesAfterCompletion_NextTransitionRuns()
    {
        var c = MakeGated(out var source, out _, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
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
            (_, _, _) => throwNext
                ? throw new InvalidOperationException("boom")
                : TestFixtures.Composed(fake),
            new FakeProblemStatsSink(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity));

        Assert.False(c.IsBusy); // released via finally, not wedged

        throwNext = false;
        Assert.Equal(QuizStartOutcome.Started, await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity));
        Assert.NotNull(c.Current);
    }

    [Fact]
    public async Task Gate_ReleasesWhenEnumeratorThrows_NextStartRuns()
    {
        var good = new FakeProblemSetSource([Decision()]);
        var throwNext = true;
        var c = new QuizController(
            (_, _, _) => TestFixtures.Composed(throwNext ? new ThrowingProblemSetSource() : good),
            new FakeProblemStatsSink(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity));

        Assert.False(c.IsBusy);

        throwNext = false;
        Assert.Equal(QuizStartOutcome.Started, await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity));
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
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

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
