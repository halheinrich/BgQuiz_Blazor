using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The transition-gate overlap suite: a second gesture arriving while a
/// Start / Restart / Submit / ▶ / End-quiz transition is in
/// flight must <b>no-op</b> — not queue, not throw, and above all not touch the
/// one live enumerator (an overlapped <c>MoveNextAsync</c> faults on a
/// thread-pool continuation no page can catch, terminating the WASM runtime;
/// that crash is the dogfooding finding this gate exists to close) or the
/// lifetime record a Submit is writing.
///
/// <para>
/// Overlap windows are frozen deterministically: <see cref="GatedProblemSetSource"/>
/// suspends the controller inside an awaited <c>MoveNextAsync</c>, and
/// <see cref="FakeProblemStatsSink.RecordGate"/> suspends it inside a Submit's
/// awaited write to the lifetime record (where its review is already on
/// screen). The gate flips <c>IsBusy</c> synchronously before its first
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
    public async Task StartAsync_WhileAnAdvanceIsPending_NoOpsWithBusyOutcome()
    {
        // The double-Start hazard mid-advance: without the gate a second
        // Start disposes the enumerator the pending ▶ is awaiting.
        var c = MakeGated(out var source, out _, out var factoryCalls, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());

        var pending = c.NextAsync(); // suspends at the gated advance to the second problem

        Assert.Equal(QuizStartOutcome.Busy, await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity));

        source.ReleaseNext();
        await pending;
        Assert.Equal(1, factoryCalls());
        Assert.NotNull(c.Current);
    }

    // -----------------------------------------------------------------------
    //  Overlapped ▶ / Submit / End quiz during an advance
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Next_DuringAPendingAdvance_NoOps()
    {
        // Mid-advance, Current still points at the outgoing problem — the state
        // guard stale-passes, so the busy check is the guard that actually
        // holds; a second ▶ there would overlap the enumerator.
        var c = MakeGated(out var source, out _, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.SubmitPlayAsync(BestPlay());

        var pending = c.NextAsync(); // suspends at the gated advance

        await c.NextAsync();      // must no-op

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
        await c.SubmitPlayAsync(BestPlay());

        var pending = c.NextAsync(); // suspends at the gated advance

        // Wait for the window the gate exists for. The pending advance's
        // continuation runs on its own schedule; once the review is gone it has
        // moved on from the problem and is waiting on the source, and it cannot
        // go further until this test releases one. There the state guards
        // stale-pass — the outgoing problem is back in its answering state — so
        // an ungated submission would be taken, as practice on a completed
        // problem, and put a review up over a problem the run is leaving.
        Assert.True(
            SpinWait.SpinUntil(() => c.Review is null, TimeSpan.FromSeconds(10)),
            "The pending advance never reached the gated draw.");

        await c.SubmitPlayAsync(BestPlay());        // must no-op — the outgoing problem is not re-scorable

        Assert.Null(c.Review);           // no review went up: the submission was refused
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);

        source.ReleaseNext();
        await pending;
        Assert.Null(c.Review);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
    }

    [Fact]
    public async Task SubmitPlay_DuringAPendingSkip_NoOps()
    {
        // The window a pending Skip — ▶ on the unresolved frontier — opens is
        // sharper than a pending Continue's. It completes nothing, so the problem
        // it left is still the frontier,
        // still unresolved and in its answering state while the source is
        // asked: an ungated submission there would be live — an answer of
        // record, written to the lifetime record, on a problem the user has
        // moved on from, with a review going up under the advance.
        var c = MakeGated(out var source, out var sink, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);

        var pending = c.NextAsync();
        source.WaitForDrawRequest(2);    // moved on, and waiting on the source

        await c.SubmitPlayAsync(BestPlay());        // must no-op

        Assert.Null(c.Review);
        Assert.Equal(0, c.Score.PlayDecisions.Submitted);

        source.ReleaseNext();
        await pending;
        Assert.Equal(0, c.Score.PlayDecisions.Submitted);
        Assert.Equal(1, c.SkippedCount);
        Assert.Equal(0, sink.TotalFolds);
    }

    [Fact]
    public async Task Skip_WhileItsDrawIsPending_CountsNothingUntilTheNextProblemLands()
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

        var pending = c.NextAsync();
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
        await c.SubmitPlayAsync(BestPlay());

        var pending = c.NextAsync(); // suspends at the gated advance

        await c.EndQuizAsync();          // must no-op

        Assert.False(c.IsFinished);

        source.ReleaseNext();
        await pending;
        Assert.False(c.IsFinished);      // the advance completed normally
        Assert.NotNull(c.Current);
        Assert.Equal(0, c.SkippedCount); // and no abandon was recorded
    }

    // -----------------------------------------------------------------------
    //  A Submit's pending write to the lifetime record
    //
    //  Submit folds its answer of record and awaits the write inside the gate
    //  (SPEC-scoring.md §2's fold trigger), so the answer cannot be lost to a
    //  gesture that begins a new run, moves on or ends the quiz while it is on
    //  its way. Its review is on screen through the window, so for ▶ and End
    //  quiz the state guards pass and the busy check is what refuses them;
    //  Start and Restart have no state guard at all. The moves among the
    //  presented problems are pinned below, on a problem where each of them is
    //  available by state.
    // -----------------------------------------------------------------------

    public enum Gesture { Start, Restart, Next, EndQuiz }

    [Theory]
    [InlineData(Gesture.Start)]
    [InlineData(Gesture.Restart)]
    [InlineData(Gesture.Next)]
    [InlineData(Gesture.EndQuiz)]
    public async Task AGesture_WhileASubmitsWriteIsPending_NoOps_AndTheSinkHoldsOneFold(Gesture gesture)
    {
        var c = MakeGated(out var source, out var sink, out var factoryCalls, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        var problem = c.Current;

        var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sink.RecordGate = write.Task;
        var submit = c.SubmitPlayAsync(BestPlay()); // suspended inside the write, review on screen
        var review = Assert.IsType<ProblemReview.Play>(c.Review);
        Assert.True(review.Submission.TryGetScored(out var submitted));
        Assert.True(c.IsBusy);
        Assert.False(submit.IsCompleted);

        switch (gesture)
        {
            case Gesture.Start:
                Assert.Equal(QuizStartOutcome.Busy,
                    await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity));
                break;
            case Gesture.Restart:
                Assert.Equal(QuizStartOutcome.Busy, await c.RestartAsync(PlayRanking.Equity));
                break;
            case Gesture.Next: await c.NextAsync(); break;
            case Gesture.EndQuiz: await c.EndQuizAsync(); break;
        }

        // Nothing moved inside the window: the same problem, its review up.
        Assert.Same(problem, c.Current);
        Assert.Same(review, c.Review);
        Assert.False(c.IsFinished);

        write.SetResult();
        await submit;

        Assert.False(c.IsBusy);
        Assert.Same(problem, c.Current);
        Assert.Same(review, c.Review);
        Assert.False(c.IsFinished);
        Assert.Equal(1, c.ProblemNumber);
        Assert.Equal(1, factoryCalls());            // no new run's source was built
        Assert.Equal(1, sink.BeginQuizCallCount);   // nor its stats context bound
        Assert.Same(submitted, Assert.Single(sink.Plays));
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
    }

    public enum CursorMove { GoToFirst, GoBack, GoToLast, NextBehindTheFrontier }

    [Theory]
    [InlineData(CursorMove.GoToFirst)]
    [InlineData(CursorMove.GoBack)]
    [InlineData(CursorMove.GoToLast)]
    [InlineData(CursorMove.NextBehindTheFrontier)]
    public async Task AMove_WhileASubmitsWriteIsPending_NoOps(CursorMove move)
    {
        // A deferred problem answered live on return: its write is pending, its
        // review up, behind the frontier — where every move is available by
        // state (an earlier problem for ⏮ ◀, the frontier ahead for ⏭ and ▶).
        // These moves take no gate of their own, so the busy check each makes
        // is all that keeps it from taking the review down mid-write.
        var c = MakeGated(out var source, out var sink, out _, Decision(), Decision(), Decision());
        source.ReleaseNext(3);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.NextAsync();
        await c.NextAsync();                        // the first two deferred, the third the frontier
        c.GoBack();                                 // the second, deferred and live
        var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sink.RecordGate = write.Task;
        var submit = c.SubmitPlayAsync(BestPlay());
        var review = c.Review;
        Assert.NotNull(review);
        Assert.True(c.IsBusy);
        Assert.True(c.CanGoBack);
        Assert.True(c.CanGoToLast);

        switch (move)
        {
            case CursorMove.GoToFirst: c.GoToFirst(); break;
            case CursorMove.GoBack: c.GoBack(); break;
            case CursorMove.GoToLast: c.GoToLast(); break;
            case CursorMove.NextBehindTheFrontier: await c.NextAsync(); break;
        }

        Assert.Equal(2, c.ProblemNumber);
        Assert.Same(review, c.Review);

        write.SetResult();
        await submit;

        Assert.False(c.IsBusy);
        Assert.Equal(2, c.ProblemNumber);
        Assert.Same(review, c.Review);
        Assert.Single(sink.Plays);
    }

    [Fact]
    public async Task ASecondSubmit_WhileTheFirstsWriteIsPending_NoOps()
    {
        // A double press of Submit: the first has put its review up and holds
        // the gate for its write. The second is refused — by the review's state
        // guard and by the gate alike — so it neither re-scores nor writes.
        var c = MakeGated(out var source, out var sink, out _, Decision(), Decision());
        source.ReleaseNext();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sink.RecordGate = write.Task;

        var first = c.SubmitPlayAsync(AltPlay());
        var review = c.Review;
        await c.SubmitPlayAsync(BestPlay());

        Assert.Same(review, c.Review);
        write.SetResult();
        await first;
        Assert.Single(sink.Plays);
        Assert.Equal(0, c.Score.PlayDecisions.Correct); // the first answer, not the second
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
        await c.NextAsync(); // a fresh transition passes the released gate

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
