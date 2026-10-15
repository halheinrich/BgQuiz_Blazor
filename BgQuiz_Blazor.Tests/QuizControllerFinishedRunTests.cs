using System.Runtime.CompilerServices;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// How a run finishes, and what a finished run holds (<c>SPEC-quiz-history.md</c>
/// §9; halheinrich/backgammon#407). However a run finishes — End quiz, the
/// source running out, or a new sample — it keeps its ranking, score, skip
/// count, presented count, name, composition telemetry and the filters and
/// mix a Restart replays, and lets go of its stream, its source's parsed
/// decisions and buffered files, and the decisions of the problems it
/// presented. Wherever it finishes, the live-quiz marker is cleared.
///
/// <para>
/// <b>How "lets go" is observed.</b> A <see cref="WeakReference"/> to what the
/// run must release, taken where nothing else in the test holds it, and a full
/// collection once the only other holder — the sample, as the picked-folder
/// holder holds it — has been dropped too: what survives the collection is
/// what the controller still references. The objects are made in methods
/// that are never inlined, and the controller's work runs on one thread
/// (<see cref="SingleThreadedRunner"/>), as it does in the browser, so no idle
/// thread-pool worker keeps a state machine of it reachable.
/// </para>
/// </summary>
public class QuizControllerFinishedRunTests
{
    private static Play BestPlay() => TestFixtures.OpeningBest();
    private static Play AltPlay() => TestFixtures.OpeningAlternative();

    private static CheckerPlayDecision Problem(string file) =>
        TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId(file));

    /// <summary>
    /// The sample the factory composes over, as <see cref="PickedProblemFolder"/>
    /// holds it for the real factory: a test drops it to stand in for the
    /// holder letting go of a sample, after which only the run can keep it.
    /// </summary>
    private sealed class SampleSlot
    {
        public IProblemSetSource? Current { get; set; }
    }

    private static QuizController Make(
        SampleSlot slot, FakeQuizLiveMarker? marker = null, ILogger<QuizController>? logger = null,
        FakeProblemStatsSink? sink = null) =>
        new(
            (_, _) => TestFixtures.Composed(
                slot.Current ?? throw new InvalidOperationException("No sample is held.")),
            sink ?? new FakeProblemStatsSink(), marker ?? new FakeQuizLiveMarker(), TimeProvider.System,
            logger ?? NullLogger<QuizController>.Instance);

    /// <summary>
    /// Put a two-problem sample in <paramref name="slot"/> and hand back weak
    /// references to the source and to the problem a run presents first.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Source, WeakReference FirstProblem) HoldASample(SampleSlot slot)
    {
        var first = Problem("first.xgp");
        var source = new FakeProblemSetSource([first, Problem("second.xgp")]);
        slot.Current = source;
        return (new WeakReference(source), new WeakReference(first));
    }

    private static void CollectEverythingUnreachable()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    // -----------------------------------------------------------------------
    //  Nothing heavy, however the run finishes
    // -----------------------------------------------------------------------

    public enum Finish
    {
        EndQuiz,
        SourceRunsOut,
        NewSample,
    }

    /// <summary>Finish <paramref name="c"/>'s run — two problems presented — the way named.</summary>
    private static async Task FinishAsync(QuizController c, Finish how)
    {
        switch (how)
        {
            case Finish.EndQuiz:
                await c.EndQuizAsync();
                break;
            case Finish.SourceRunsOut:
                await c.NextAsync(); // past the last: the source has run out
                break;
            case Finish.NewSample:
                await c.EndForNewSampleAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(how), how, null);
        }
    }

    [Theory]
    [InlineData(Finish.EndQuiz)]
    [InlineData(Finish.SourceRunsOut)]
    [InlineData(Finish.NewSample)]
    public void AFinishedRun_ReleasesItsSourceAndThePresentedDecisions(Finish how)
    {
        var slot = new SampleSlot();
        QuizController c = null!;
        WeakReference source = null!, firstProblem = null!;
        SingleThreadedRunner.Run(async () =>
        {
            c = Make(slot);
            (source, firstProblem) = HoldASample(slot);
            await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);
            await c.NextAsync(); // the second presented; the first deferred

            await FinishAsync(c, how);
        });
        slot.Current = null; // the holder lets go of the sample
        CollectEverythingUnreachable();

        Assert.True(c.IsFinished);
        Assert.False(source.IsAlive, "The finished run still holds its source.");
        Assert.False(firstProblem.IsAlive, "The finished run still holds a problem it presented.");
    }

    [Theory]
    [InlineData(Finish.EndQuiz)]
    [InlineData(Finish.SourceRunsOut)]
    [InlineData(Finish.NewSample)]
    public async Task AFinishedRun_KeepsItsResultAndItsReplay(Finish how)
    {
        // What it keeps is what Done, Stats and Restart read: unchanged by the
        // releases.
        var slot = new SampleSlot();
        var c = Make(slot);
        HoldASample(slot);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.DepthFirst, ProblemOrder.AsRead);
        await c.SubmitPlayAsync(BestPlay());
        await c.NextAsync(); // the second presented, then left unanswered
        await FinishAsync(c, how);
        slot.Current = null;
        CollectEverythingUnreachable();

        Assert.True(c.IsFinished);
        Assert.Equal(PlayRanking.DepthFirst, c.Ranking);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted);
        Assert.Equal(1, c.SkippedCount); // the unanswered second, converted however it finished
        Assert.Equal(2, c.PresentedCount);
        Assert.Equal("Fake", c.Name);
        Assert.Null(c.LastComposition); // a blank mix composes nothing
        Assert.Null(c.Current);
        Assert.Equal(2, c.ProblemCount); // the total the run established, stable for it (§5)

        // The replay: a Restart replays the run's filters over the sample held now.
        HoldASample(slot);
        Assert.Equal(QuizStartOutcome.Started, await c.RestartAsync(PlayRanking.Equity, ProblemOrder.AsRead));
        Assert.False(c.IsFinished);
    }

    [Fact]
    public async Task AWeightedRun_Finished_KeepsItsCompositionTelemetry()
    {
        var slot = new SampleSlot();
        var sink = new FakeProblemStatsSink { CanWeightMix = true, CurrentDocument = ProblemStatsDocument.Empty };
        var c = Make(slot, sink: sink);
        HoldASample(slot);
        await c.StartAsync(
            new FilterConfig(), new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)]),
            PlayRanking.Equity, ProblemOrder.AsRead);
        var composition = c.LastComposition;

        await c.EndQuizAsync();

        Assert.NotNull(composition);
        Assert.Same(composition, c.LastComposition);
    }

    /// <summary>
    /// The real factory's parse-once layer is where the heavy references are:
    /// the parsed decisions and the buffered files (halheinrich/backgammon#407).
    /// A run finished over a real pick must let both go once the holder has.
    /// </summary>
    [Fact]
    public void EndQuiz_OverARealPick_ReleasesItsParsedDecisionsAndBufferedFiles()
    {
        var picked = new PickedProblemFolder();
        QuizController c = null!;
        WeakReference file = null!, parse = null!;
        SingleThreadedRunner.Run(async () =>
        {
            c = new QuizController(
                PickedFolderSourceFactory.Create(picked, NullLoggerFactory.Instance, TimeProvider.System),
                new FakeProblemStatsSink(), new FakeQuizLiveMarker(), TimeProvider.System, NullLogger<QuizController>.Instance);
            (file, parse) = await StartOverARealPickAsync(picked, c);

            await c.EndQuizAsync();
        });
        picked.Clear(); // the holder lets go of its sample and its parse cache
        CollectEverythingUnreachable();

        Assert.True(c.IsFinished);
        Assert.False(file.IsAlive, "The finished run still holds the buffered files of its sample.");
        Assert.False(parse.IsAlive, "The finished run still holds its source's parsed decisions.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference File, WeakReference Parse)> StartOverARealPickAsync(
        PickedProblemFolder picked, QuizController c)
    {
        var file = TestFixtures.ReadableXg();
        picked.Set("Corpus", FolderWriteCapability.BrowserUnsupported, TestFixtures.Sample([file]));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);
        Assert.NotNull(c.Current);
        return (new WeakReference(file), new WeakReference(picked.Parsed));
    }

    // -----------------------------------------------------------------------
    //  A new sample ends the run in progress (§9)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ANewSample_EndsTheRunInProgress_AsEndQuizEndsIt()
    {
        var slot = new SampleSlot();
        var marker = new FakeQuizLiveMarker();
        var c = Make(slot, marker);
        HoldASample(slot);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);
        await c.SubmitPlayAsync(BestPlay()); // the first answered, in review

        await c.EndForNewSampleAsync();

        Assert.True(c.IsFinished);
        Assert.False(c.IsBusy);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted); // its answer stands
        Assert.Equal(0, c.SkippedCount);                  // the second was never presented
        Assert.Equal(1, c.PresentedCount);
        Assert.NotNull(c.EndedByNewSample);               // what Home says, and links on
        Assert.False(marker.IsLive);                      // a reload now has no live quiz to lose
    }

    [Fact]
    public async Task ANewSample_ConvertsUnresolvedProblemsToSkipsOfRecord()
    {
        var slot = new SampleSlot();
        var c = Make(slot);
        HoldASample(slot);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);
        await c.NextAsync(); // the first deferred, the second on screen unanswered

        await c.EndForNewSampleAsync();

        Assert.Equal(2, c.SkippedCount);
        Assert.Equal(2, c.PresentedCount);
    }

    [Fact]
    public async Task ANewSample_WithNoRun_OrAFinishedOne_ChangesNothing()
    {
        var slot = new SampleSlot();
        var marker = new FakeQuizLiveMarker();
        var c = Make(slot, marker);

        await c.EndForNewSampleAsync(); // no run
        Assert.False(c.HasStarted);
        Assert.Empty(marker.Writes);

        HoldASample(slot);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);
        await c.EndQuizAsync();
        var writes = marker.Writes.Count;

        await c.EndForNewSampleAsync(); // a run End quiz finished
        Assert.True(c.IsFinished);
        Assert.Null(c.EndedByNewSample); // it was not the new sample that finished it
        Assert.Equal(writes, marker.Writes.Count);
    }

    [Fact]
    public async Task ARunFinishedOtherwise_IsNotSaidToBeEndedByANewSample()
    {
        var slot = new SampleSlot();
        var c = Make(slot);
        HoldASample(slot);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);

        await c.EndQuizAsync();

        Assert.True(c.IsFinished);
        Assert.Null(c.EndedByNewSample);
    }

    /// <summary>
    /// A new sample's end is not a gesture, so the transition gate does not
    /// drop it: with a Submit's write to the lifetime record in flight it
    /// waits, and ends the run once the write has landed.
    /// </summary>
    [Fact]
    public async Task ANewSample_WaitsForATransitionInFlight_ThenEndsTheRun()
    {
        var slot = new SampleSlot();
        var sink = new FakeProblemStatsSink();
        var c = Make(slot, sink: sink);
        HoldASample(slot);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);
        var write = new TaskCompletionSource();
        sink.RecordGate = write.Task;

        var submit = c.SubmitPlayAsync(BestPlay()); // holds the gate until the write lands
        Assert.True(c.IsBusy);
        var ending = c.EndForNewSampleAsync();
        await Task.Delay(50);
        Assert.False(ending.IsCompleted); // waiting, not dropped
        Assert.False(c.IsFinished);

        write.SetResult();
        await submit;
        await ending;

        Assert.True(c.IsFinished);
        Assert.NotNull(c.EndedByNewSample);
        Assert.Equal(1, c.Score.PlayDecisions.Submitted); // the answer written first, and kept
    }

    // -----------------------------------------------------------------------
    //  The live-quiz marker: the controller is its one writer (§9)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(Finish.EndQuiz)]
    [InlineData(Finish.SourceRunsOut)]
    [InlineData(Finish.NewSample)]
    public async Task TheMarker_IsSetWhenARunGoesLive_AndClearedWhereverItFinishes(Finish how)
    {
        var slot = new SampleSlot();
        var marker = new FakeQuizLiveMarker();
        var c = Make(slot, marker);
        HoldASample(slot);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);
        Assert.Equal(["live"], marker.Writes);
        await c.NextAsync();

        await FinishAsync(c, how);

        Assert.Equal(["live", "clear"], marker.Writes);
        Assert.False(marker.IsLive);
    }

    [Fact]
    public async Task TheMarker_IsCleared_WhenAnAttemptCommitsARunThatEndedAtOnce()
    {
        // A quiz live, then a Start whose stream holds nothing to present: the
        // committed run has already ended, so no quiz is live any more.
        var slot = new SampleSlot();
        var marker = new FakeQuizLiveMarker();
        var c = Make(slot, marker);
        HoldASample(slot);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);
        slot.Current = new FakeProblemSetSource([TestFixtures.PassDecision()]);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);

        Assert.True(c.IsFinished);
        Assert.Equal(["live", "clear"], marker.Writes);
    }

    [Fact]
    public async Task TheMarker_IsUntouched_ByAnAttemptThatIsRefusedOrFaults()
    {
        var slot = new SampleSlot();
        var marker = new FakeQuizLiveMarker();
        var sink = new FakeProblemStatsSink();
        var c = Make(slot, marker, sink: sink);
        HoldASample(slot);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);

        var refused = await c.StartAsync(
            new FilterConfig(), new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)]),
            PlayRanking.Equity, ProblemOrder.AsRead); // no stats: refused
        slot.Current = null; // the factory faults
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead));

        Assert.Equal(QuizStartOutcome.MixRequiresStats, refused);
        Assert.Equal(["live"], marker.Writes);
    }

    // -----------------------------------------------------------------------
    //  Releasing never misreports (SPEC-quiz-history.md §8, §9)
    // -----------------------------------------------------------------------

    /// <summary>
    /// However the run finishes — the source running out included — a release
    /// of its stream that fails undoes nothing: the run is finished, its
    /// result kept, the live-quiz marker cleared, and the failure logged where
    /// the release happened (<c>SPEC-quiz-history.md</c> §8, "Releasing never
    /// misreports"). A stream that has run out reports its end, so the finish
    /// comes; its release's failure arrives only at the release.
    /// </summary>
    [Theory]
    [InlineData(Finish.EndQuiz)]
    [InlineData(Finish.SourceRunsOut)]
    [InlineData(Finish.NewSample)]
    public async Task AFinishWhoseReleaseFails_StillFinishes_AndSaysSoInTheLog(Finish how)
    {
        var log = new StreamLog();
        var logger = new RecordingLogger<QuizController>();
        var marker = new FakeQuizLiveMarker();
        var c = new QuizController(
            (_, _) => TestFixtures.Composed(new RecordingProblemSetSource(
                "A", log, [Problem("a1.xgp"), Problem("a2.xgp")], releaseFaults: true)),
            new FakeProblemStatsSink(), marker, TimeProvider.System, logger);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);
        await c.NextAsync(); // both problems presented: the next ▶ finds the source run out

        await FinishAsync(c, how); // the release throws; the finish is not undone, and not reported as failed

        Assert.True(c.IsFinished);
        Assert.False(c.IsBusy);
        Assert.Equal(2, c.PresentedCount); // the result is kept
        Assert.False(marker.IsLive); // and the marker is cleared all the same
        Assert.Equal(1, log.Stream("A/1").ReleaseCount);
        var logged = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.Equal(RecordingProblemSetSource.ReleaseFaultMessage, logged.Exception?.Message);
    }
}
