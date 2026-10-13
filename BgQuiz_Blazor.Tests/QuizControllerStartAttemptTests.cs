using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Start and Restart as <c>SPEC-quiz-history.md</c> §8 rules them: an
/// <i>attempt</i> prepares — the refusal checks, the stats bind, building the
/// source, opening its stream, and drawing to the first problem with a choice
/// or the end — and only then commits, in one step that cannot fail. So an
/// attempt that faults anywhere in prepare keeps the current run, whatever
/// its state, and releases what it opened; one that commits switches to the
/// new run and only then releases the previous run's stream, once.
///
/// <para>
/// <b>Which stream a draw came from is the question</b>, so every source here
/// is a <see cref="RecordingProblemSetSource"/> over one shared
/// <see cref="StreamLog"/>, and the factory hands out, call by call, the
/// sources a test lined up. The refused attempt, the empty one and the busy
/// one are pinned where they always were, in <c>QuizControllerTests</c> and
/// <c>QuizControllerOverlapTests</c>; the stats bind's place is pinned here
/// only as far as a fault reaches it.
/// </para>
/// </summary>
public class QuizControllerStartAttemptTests
{
    /// <summary>Where in prepare an attempt faults (§8: "everything that can be refused or fail").</summary>
    public enum PrepareFault
    {
        /// <summary>The source factory throws.</summary>
        Factory,

        /// <summary>The source is built, and opening its stream throws.</summary>
        OpeningTheStream,

        /// <summary>The stream opens, and its first draw throws.</summary>
        FirstDraw,

        /// <summary>The first draw is a position with no choice, auto-skipped, and the draw after it throws.</summary>
        DrawPastAnAutoSkip,
    }

    /// <summary>The state of the current run an attempt is made over (§8: "none, active or finished").</summary>
    public enum RunState
    {
        None,
        Active,
        Finished,
    }

    /// <summary>Which of the two gestures makes the attempt.</summary>
    public enum Attempt
    {
        Start,
        Restart,
    }

    private static Play BestPlay() => TestFixtures.OpeningBest();
    private static Play AltPlay() => TestFixtures.OpeningAlternative();

    private static CheckerPlayDecision Problem(string file) =>
        TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId(file));

    // The first run's three problems, and the attempt's two. Distinct
    // instances, so Assert.Same says which run a problem on screen is from.
    private static readonly CheckerPlayDecision A1 = Problem("a1.xgp");
    private static readonly CheckerPlayDecision A2 = Problem("a2.xgp");
    private static readonly CheckerPlayDecision A3 = Problem("a3.xgp");
    private static readonly CheckerPlayDecision B1 = Problem("b1.xgp");
    private static readonly CheckerPlayDecision B2 = Problem("b2.xgp");

    /// <summary>
    /// The mix the faulted Start hands in — run passthrough by its
    /// <c>ignoreMix</c>, so the attempt is not refused, while a Start that
    /// stored it would leave a Restart that is refused under the harness's
    /// stats-less sink. That refusal is how a stored mix is observed here.
    /// </summary>
    private static QuizMix NeverSeenMix() =>
        new([new QuizMixEntry(QuizCategory.NeverSeen, 100)], quizLength: null, randomOrder: false);

    /// <summary>
    /// A controller whose source factory hands out the sources a test lined up,
    /// one per call, recording what each call was handed: the filter set (its
    /// instance, which is how a Restart's replay of a run's own filters is
    /// told from a later Start's) and the effective mix.
    /// </summary>
    private sealed class Harness
    {
        private readonly Queue<Func<IProblemSetSource>> _lined = new();

        public Harness()
        {
            Controller = NewController(
                (filters, _, mix) =>
                {
                    FiltersHanded.Add(filters);
                    MixesHanded.Add(mix);
                    return TestFixtures.Composed(_lined.Dequeue()());
                },
                Sink,
                Logger);
        }

        /// <summary>The controller's log: where a release that throws is reported, since it is not reported as a failed attempt.</summary>
        public RecordingLogger<QuizController> Logger { get; } = new();

        public StreamLog Log { get; } = new();

        public FakeProblemStatsSink Sink { get; } = new();

        public List<DecisionFilterSet> FiltersHanded { get; } = [];

        public List<QuizMix> MixesHanded { get; } = [];

        public QuizController Controller { get; }

        /// <summary>The next factory call builds a recording source named <paramref name="name"/>.</summary>
        public void LineUp(
            string name, BgDecisionData[] items,
            bool openFaults = false, int? faultAtDraw = null, bool releaseFaults = false) =>
            _lined.Enqueue(() => new RecordingProblemSetSource(
                name, Log, items, openFaults, faultAtDraw, releaseFaults));

        /// <summary>The next factory call throws.</summary>
        public void LineUpAFactoryFault() =>
            _lined.Enqueue(() => throw new InvalidOperationException(RecordingProblemSetSource.FaultMessage));

        /// <summary>The next factory call faults the attempt at <paramref name="fault"/>, over a source named "B".</summary>
        public void LineUp(PrepareFault fault)
        {
            switch (fault)
            {
                case PrepareFault.Factory:
                    LineUpAFactoryFault();
                    break;
                case PrepareFault.OpeningTheStream:
                    LineUp("B", [B1, B2], openFaults: true);
                    break;
                case PrepareFault.FirstDraw:
                    LineUp("B", [B1, B2], faultAtDraw: 1);
                    break;
                case PrepareFault.DrawPastAnAutoSkip:
                    LineUp("B", [TestFixtures.PassDecision(), B1], faultAtDraw: 2);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
            }
        }

        /// <summary>
        /// The first run, "A", brought to <paramref name="state"/>. Active: the
        /// first problem answered, and ▶ has brought the second, which is on
        /// screen unresolved on the frontier — so the run has a score, a
        /// presented sequence, a place in its stream and a third problem still
        /// to come. Finished: that run, ended by End quiz, which releases its
        /// stream.
        /// </summary>
        public async Task BringRunToAsync(RunState state)
        {
            if (state == RunState.None) return;

            LineUp("A", [A1, A2, A3]);
            await Controller.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
            await Controller.SubmitPlayAsync(BestPlay());
            await Controller.NextAsync();
            Assert.Same(A2, Controller.Current);

            if (state == RunState.Finished) await Controller.EndQuizAsync();
        }

        /// <summary>The attempt <paramref name="attempt"/>, under the ranking the first run was not started with.</summary>
        public Task<QuizStartOutcome> MakeAsync(Attempt attempt) => attempt switch
        {
            Attempt.Start => Controller.StartAsync(
                new FilterConfig(), NeverSeenMix(), PlayRanking.DepthFirst, ignoreMix: true),
            Attempt.Restart => Controller.RestartAsync(PlayRanking.DepthFirst),
            _ => throw new ArgumentOutOfRangeException(nameof(attempt), attempt, null),
        };
    }

    /// <summary>The controller under test, as every test here constructs it.</summary>
    private static QuizController NewController(
        ProblemSetSourceFactory factory, IProblemStatsSink sink, ILogger<QuizController>? logger = null) =>
        new(factory, sink, TimeProvider.System, logger ?? NullLogger<QuizController>.Instance);

    /// <summary>
    /// Everything a page can read about the current run — §8's "kept" list,
    /// read through the controller: the problem on screen and its review, the
    /// presented sequence and where the cursor is in it, the totals, the
    /// ranking, the telemetry and the name.
    /// </summary>
    private sealed record Observed(
        bool HasStarted, bool IsFinished, BgDecisionData? Current, ProblemReview? Review,
        int ProblemNumber, int? ProblemCount, int PresentedCount, QuizScore Score, int SkippedCount,
        bool CanGoBack, bool CanGoToLast, PlayRanking? Ranking, string? Name, MixComposition? Composition);

    private static Observed Observe(QuizController c) => new(
        c.HasStarted, c.IsFinished, c.Current, c.Review,
        c.ProblemNumber, c.ProblemCount, c.PresentedCount, c.Score, c.SkippedCount,
        c.CanGoBack, c.CanGoToLast, c.HasStarted ? c.Ranking : null, c.Name, c.LastComposition);

    /// <summary>How many times the stream "A/1" — the first run's — has been released: once if End quiz ended it.</summary>
    private static int FirstRunsReleases(RunState state) => state == RunState.Finished ? 1 : 0;

    public static TheoryData<RunState, PrepareFault> EveryRunStateAndFault()
    {
        var data = new TheoryData<RunState, PrepareFault>();
        foreach (var state in Enum.GetValues<RunState>())
            foreach (var fault in Enum.GetValues<PrepareFault>())
                data.Add(state, fault);
        return data;
    }

    public static TheoryData<RunState, PrepareFault> EveryRunToRestartAndFault()
    {
        var data = new TheoryData<RunState, PrepareFault>();
        foreach (var state in new[] { RunState.Active, RunState.Finished })
            foreach (var fault in Enum.GetValues<PrepareFault>())
                data.Add(state, fault);
        return data;
    }

    // -----------------------------------------------------------------------
    //  Outcomes 1 and 4: a faulted attempt keeps the current run, and
    //  releases what it opened
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EveryRunStateAndFault))]
    public async Task AFaultedStart_KeepsTheCurrentRun_AndReleasesWhatItOpened(RunState state, PrepareFault fault)
    {
        var h = new Harness();
        await h.BringRunToAsync(state);
        var before = Observe(h.Controller);
        h.LineUp(fault);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => h.MakeAsync(Attempt.Start));

        Assert.Equal(RecordingProblemSetSource.FaultMessage, thrown.Message);
        Assert.False(h.Controller.IsBusy);
        Assert.Equal(before, Observe(h.Controller));
        AssertReleases(h, state, fault);
        await AssertRestartReplaysTheKeptRunAsync(h, state);
    }

    /// <summary>
    /// The last of §8's "kept" list: the filters and mix the kept run's Restart
    /// replays. With no current run there is still none, so Restart is still
    /// the never-started caller bug; otherwise it replays the first run's own
    /// filter set and its blank mix — which, unlike the faulted Start's
    /// weighted one, the stats-less sink does not refuse.
    /// </summary>
    private static async Task AssertRestartReplaysTheKeptRunAsync(Harness h, RunState state)
    {
        if (state == RunState.None)
        {
            var neverStarted = await Assert.ThrowsAsync<InvalidOperationException>(
                () => h.Controller.RestartAsync(PlayRanking.Equity));
            Assert.Contains("requires a prior successful StartAsync", neverStarted.Message);
            return;
        }

        h.LineUp("A-again", [A1, A2, A3]);
        Assert.Equal(QuizStartOutcome.Started, await h.Controller.RestartAsync(PlayRanking.Equity));
        Assert.Same(h.FiltersHanded[0], h.FiltersHanded[^1]);
        Assert.Same(QuizMix.Empty, h.MixesHanded[^1]);
        Assert.Same(A1, h.Controller.Current);
    }

    [Theory]
    [MemberData(nameof(EveryRunToRestartAndFault))]
    public async Task AFaultedRestart_KeepsTheCurrentRun_AndReleasesWhatItOpened(RunState state, PrepareFault fault)
    {
        var h = new Harness();
        await h.BringRunToAsync(state);
        var before = Observe(h.Controller);
        h.LineUp(fault);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => h.MakeAsync(Attempt.Restart));

        Assert.Equal(RecordingProblemSetSource.FaultMessage, thrown.Message);
        Assert.False(h.Controller.IsBusy);
        Assert.Equal(before, Observe(h.Controller));
        AssertReleases(h, state, fault);
        await AssertRestartReplaysTheKeptRunAsync(h, state);
    }

    /// <summary>
    /// After a faulted attempt: the first run's stream is released only if End
    /// quiz released it, and the stream the attempt opened — if it got as far
    /// as opening one — has been released exactly once.
    /// </summary>
    private static void AssertReleases(Harness h, RunState state, PrepareFault fault)
    {
        if (state != RunState.None)
            Assert.Equal(FirstRunsReleases(state), h.Log.Stream("A/1").ReleaseCount);

        var opened = fault is PrepareFault.FirstDraw or PrepareFault.DrawPastAnAutoSkip;
        if (opened)
            Assert.Equal(1, h.Log.Stream("B/1").ReleaseCount);
        else
            Assert.DoesNotContain(h.Log.Opened, s => s.Label.StartsWith('B'));
    }

    // -----------------------------------------------------------------------
    //  Outcome 2: after a faulted Start, the active run goes on as itself
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(PrepareFault.Factory)]
    [InlineData(PrepareFault.OpeningTheStream)]
    [InlineData(PrepareFault.FirstDraw)]
    [InlineData(PrepareFault.DrawPastAnAutoSkip)]
    public async Task AfterAFaultedStart_TheActiveRunIsAnsweredNavigatedAndAdvancedFromItsOwnStream_ThenEndsAndRestartsAsItself(
        PrepareFault fault)
    {
        var h = new Harness();
        await h.BringRunToAsync(RunState.Active);
        var c = h.Controller;
        h.LineUp(fault);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.MakeAsync(Attempt.Start));

        // Navigated: back over the answered problem, and forward to the frontier.
        c.GoBack();
        Assert.Same(A1, c.Current);
        c.GoToLast();
        Assert.Same(A2, c.Current);

        // Answered: live, on its own ranking, and into its own score.
        await c.SubmitPlayAsync(BestPlay());
        Assert.Equal(2, c.Score.Total.Submitted);
        Assert.Equal(PlayRanking.Equity, c.Ranking);

        // ▶ on the frontier brings the problem it would have brought had the
        // attempt never been made: the third, from the run's own stream.
        await c.NextAsync();
        Assert.Same(A3, c.Current);
        Assert.Equal(("A/1", (BgDecisionData)A3), h.Log.Draws[^1]);
        Assert.Equal(3, c.ProblemNumber);
        Assert.Equal(3, c.ProblemCount);

        // End quiz reaches the run's own summary: two answered, the third
        // abandoned and counted as a skip.
        await c.EndQuizAsync();
        Assert.True(c.IsFinished);
        Assert.Equal(2, c.Score.Total.Submitted);
        Assert.Equal(1, c.SkippedCount);
        Assert.Equal(3, c.PresentedCount);
        Assert.Equal(1, h.Log.Stream("A/1").ReleaseCount);

        // Restart replays the run's own filters and mix — the blank mix, which
        // is not refused, where the faulted Start's weighted one would have been.
        h.LineUp("A-again", [A1, A2, A3]);
        Assert.Equal(QuizStartOutcome.Started, await c.RestartAsync(PlayRanking.Equity));
        Assert.Same(h.FiltersHanded[0], h.FiltersHanded[^1]);
        Assert.Same(QuizMix.Empty, h.MixesHanded[^1]);
        Assert.Same(A1, c.Current);
    }

    [Theory]
    [InlineData(PrepareFault.Factory)]
    [InlineData(PrepareFault.FirstDraw)]
    public async Task AfterAFaultedRestart_TheFinishedRunsSummaryStands_AndRestartCanBePressedAgain(PrepareFault fault)
    {
        var h = new Harness();
        await h.BringRunToAsync(RunState.Finished);
        var c = h.Controller;
        var summary = (c.Score, c.SkippedCount, c.PresentedCount, c.Name);
        h.LineUp(fault);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.MakeAsync(Attempt.Restart));

        Assert.True(c.IsFinished);
        Assert.Equal(summary, (c.Score, c.SkippedCount, c.PresentedCount, c.Name));

        h.LineUp("A-again", [A1, A2, A3]);
        Assert.Equal(QuizStartOutcome.Started, await c.RestartAsync(PlayRanking.Equity));
        Assert.Same(A1, c.Current);
        Assert.Same(h.FiltersHanded[0], h.FiltersHanded[^1]);
    }

    // -----------------------------------------------------------------------
    //  Outcome 3: a committed attempt switches, then releases the previous
    //  stream once
    // -----------------------------------------------------------------------

    public static TheoryData<RunState, Attempt> EveryRunAndAttempt() => new()
    {
        { RunState.None, Attempt.Start },
        { RunState.Active, Attempt.Start },
        { RunState.Finished, Attempt.Start },
        { RunState.Active, Attempt.Restart },
        { RunState.Finished, Attempt.Restart },
    };

    [Theory]
    [MemberData(nameof(EveryRunAndAttempt))]
    public async Task ACommittedAttempt_MakesTheNewRunCurrent_ThenReleasesThePreviousStreamOnce(RunState state, Attempt attempt)
    {
        var h = new Harness();
        await h.BringRunToAsync(state);
        var c = h.Controller;
        h.LineUp("B", [B1, B2]);

        // What the controller shows at each release: the switch must already
        // have happened when the previous stream is released.
        var atRelease = new List<(string Stream, string? Name, BgDecisionData? Current)>();
        h.Log.OnRelease = s => atRelease.Add((s.Label, c.Name, c.Current));
        var releasesBefore = state == RunState.None ? 0 : h.Log.Stream("A/1").ReleaseCount;

        Assert.Equal(QuizStartOutcome.Started, await h.MakeAsync(attempt));

        // The new run is current, its first problem on screen, and everything
        // it holds is its own.
        Assert.Same(B1, c.Current);
        Assert.Null(c.Review);
        Assert.Equal(1, c.ProblemNumber);
        Assert.Equal(2, c.ProblemCount);
        Assert.Equal(1, c.PresentedCount);
        Assert.Equal(QuizScore.Empty, c.Score);
        Assert.Equal(0, c.SkippedCount);
        Assert.Equal(PlayRanking.DepthFirst, c.Ranking);
        Assert.Equal("B", c.Name);
        Assert.Null(c.LastComposition);
        Assert.All(h.Log.Draws.SkipWhile(d => d.Stream != "B/1"), d => Assert.Equal("B/1", d.Stream));

        // The previous stream: released exactly once in all, and — where the
        // switch is what released it — only once the new run was current.
        if (state != RunState.None)
        {
            Assert.Equal(1, h.Log.Stream("A/1").ReleaseCount);
            if (releasesBefore == 0)
                Assert.Equal(("A/1", "B", (BgDecisionData?)B1), Assert.Single(atRelease));
            else
                Assert.Empty(atRelease);
        }
        Assert.Equal(0, h.Log.Stream("B/1").ReleaseCount);

        // The stored filters and mix are the new run's: a Restart now replays them.
        h.LineUp("B-again", [B1, B2]);
        var newRunsFilters = h.FiltersHanded[^1];
        var newRunsMix = attempt == Attempt.Start ? NeverSeenMix() : QuizMix.Empty;
        h.Sink.CanWeightMix = false;
        var replay = await c.RestartAsync(PlayRanking.Equity);
        Assert.Same(newRunsFilters, h.FiltersHanded[^1]);
        if (attempt == Attempt.Start)
        {
            // A Start stored the weighted mix it ran passthrough, so the plain
            // Restart re-attempts it and, without stats, is refused.
            Assert.Equal(QuizStartOutcome.MixRequiresStats, replay);
            Assert.Same(B1, c.Current);
        }
        else
        {
            Assert.Equal(QuizStartOutcome.Started, replay);
            Assert.Same(newRunsMix, h.MixesHanded[^1]);
        }
    }

    [Fact]
    public async Task ACommittedAttempt_OverARunTheSourceFinished_ReleasesItsStreamAtTheSwitch()
    {
        // A run that finished because its source ran out still holds its
        // stream open; the switch is what releases it, once.
        var h = new Harness();
        h.LineUp("A", [A1]);
        var c = h.Controller;
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        await c.NextAsync();
        Assert.True(c.IsFinished);
        Assert.Equal(0, h.Log.Stream("A/1").ReleaseCount);
        var atRelease = new List<string?>();
        h.Log.OnRelease = _ => atRelease.Add(c.Name);
        h.LineUp("B", [B1]);

        Assert.Equal(QuizStartOutcome.Started, await c.RestartAsync(PlayRanking.Equity));

        Assert.Equal(1, h.Log.Stream("A/1").ReleaseCount);
        Assert.Equal("B", Assert.Single(atRelease));
    }

    [Fact]
    public async Task AnAttemptWhoseStreamHoldsNoChoice_CommitsARunAlreadyEnded_AndReleasesThePrevious()
    {
        // Outcome 7's empty attempt, as today: the new run is current and has
        // already ended with nothing presented.
        var h = new Harness();
        await h.BringRunToAsync(RunState.Active);
        var c = h.Controller;
        h.LineUp("B", [TestFixtures.PassDecision()]);

        Assert.Equal(QuizStartOutcome.Started, await h.MakeAsync(Attempt.Start));

        Assert.True(c.IsFinished);
        Assert.Null(c.Current);
        Assert.Equal(0, c.PresentedCount);
        Assert.Equal("B", c.Name);
        Assert.Equal(1, h.Log.Stream("A/1").ReleaseCount);
    }

    // -----------------------------------------------------------------------
    //  Outcome 5: releasing never misreports
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(Attempt.Start)]
    [InlineData(Attempt.Restart)]
    public async Task AFailedReleaseOfThePreviousStream_DoesNotUndoTheSwitch_AndIsNotAFailedAttempt(Attempt attempt)
    {
        var h = new Harness();
        h.LineUp("A", [A1, A2, A3], releaseFaults: true);
        var c = h.Controller;
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        h.LineUp("B", [B1, B2]);

        var outcome = await h.MakeAsync(attempt);

        Assert.Equal(QuizStartOutcome.Started, outcome);
        Assert.False(c.IsBusy);
        Assert.Same(B1, c.Current);
        Assert.Equal("B", c.Name);
        Assert.Equal(1, h.Log.Stream("A/1").ReleaseCount);

        // Said in the log, where a failure nobody can act on belongs.
        var logged = Assert.Single(h.Logger.Entries);
        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.Equal(RecordingProblemSetSource.ReleaseFaultMessage, logged.Exception?.Message);
    }

    [Theory]
    [InlineData(PrepareFault.FirstDraw)]
    [InlineData(PrepareFault.DrawPastAnAutoSkip)]
    public async Task AFailedReleaseOfWhatAFaultedAttemptOpened_NeverReplacesTheAttemptsError(PrepareFault fault)
    {
        var h = new Harness();
        await h.BringRunToAsync(RunState.Active);
        var before = Observe(h.Controller);
        var faultAtDraw = fault == PrepareFault.FirstDraw ? 1 : 2;
        BgDecisionData[] items = fault == PrepareFault.FirstDraw ? [B1] : [TestFixtures.PassDecision(), B1];
        h.LineUp("B", items, faultAtDraw: faultAtDraw, releaseFaults: true);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => h.MakeAsync(Attempt.Start));

        Assert.Equal(RecordingProblemSetSource.FaultMessage, thrown.Message);
        Assert.Equal(1, h.Log.Stream("B/1").ReleaseCount);
        Assert.Equal(before, Observe(h.Controller));

        // The release's own failure is not lost: it is logged, naming the
        // error that was reported in its place.
        var logged = Assert.Single(h.Logger.Entries);
        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.Equal(RecordingProblemSetSource.ReleaseFaultMessage, logged.Exception?.Message);
        Assert.Contains(RecordingProblemSetSource.FaultMessage, logged.Message);
    }

    // -----------------------------------------------------------------------
    //  While an attempt prepares, the current run stays current
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WhileAnAttemptPrepares_TheCurrentRunStaysCurrent_ShownBusy()
    {
        // §8: a page may show the current run during prepare — Home's "Back to
        // quiz" stays live through a long parse — and it shows it busy. There
        // is no moment at which the current run is one with nothing on screen.
        var gated = new GatedProblemSetSource([B1]);
        var lined = new Queue<IProblemSetSource>();
        var c = NewController((_, _, _) => TestFixtures.Composed(lined.Dequeue()), new FakeProblemStatsSink());
        lined.Enqueue(new FakeProblemSetSource([A1, A2]));
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        lined.Enqueue(gated);

        var start = c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.DepthFirst);
        gated.WaitForDrawRequest(1);

        Assert.True(c.IsBusy);
        Assert.Same(A1, c.Current);
        Assert.Equal(PlayRanking.Equity, c.Ranking);
        Assert.Equal("Fake", c.Name);

        gated.ReleaseNext();
        Assert.Equal(QuizStartOutcome.Started, await start);
        Assert.Same(B1, c.Current);
    }

    // -----------------------------------------------------------------------
    //  Outcome 7: the stats link is the one thing a kept run does not keep
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(PrepareFault.Factory)]
    [InlineData(PrepareFault.FirstDraw)]
    public async Task AFaultedAttempt_HasStillBoundTheStatsContext(PrepareFault fault)
    {
        // The bind is part of prepare, so a faulted attempt has made it
        // (SPEC-quiz-history.md §8; the limitation halheinrich/backgammon#404
        // books). Pinned as it stands, so a change to it is a decision.
        var h = new Harness();
        await h.BringRunToAsync(RunState.Active);
        Assert.Equal(1, h.Sink.BeginQuizCallCount);
        h.LineUp(fault);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.MakeAsync(Attempt.Start));

        Assert.Equal(2, h.Sink.BeginQuizCallCount);
    }
}
