using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The controller's releases outside a run's transitions — the match count's
/// drain and the controller's own disposal — under <c>SPEC-quiz-history.md</c>
/// §8's "Releasing never misreports": what the draw came to stands (the
/// count, or the draw's fault), and a release that fails is logged where it
/// happened. The run's releases — a switch, a finish, a faulted attempt — are
/// pinned with the transitions they belong to
/// (<c>QuizControllerStartAttemptTests</c>, <c>QuizControllerFinishedRunTests</c>).
/// </summary>
public class QuizControllerReleaseTests
{
    private static Play BestPlay() => TestFixtures.OpeningBest();
    private static Play AltPlay() => TestFixtures.OpeningAlternative();

    private static CheckerPlayDecision Problem(string file) =>
        TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), id: new XgpDecisionId(file));

    private static (QuizController Controller, StreamLog Log, RecordingLogger<QuizController> Logger) Make(
        int? faultAtDraw = null)
    {
        var log = new StreamLog();
        var logger = new RecordingLogger<QuizController>();
        var controller = new QuizController(
            (_, _) => TestFixtures.Composed(new RecordingProblemSetSource(
                "A", log, [Problem("a1.xgp"), Problem("a2.xgp")], faultAtDraw: faultAtDraw, releaseFaults: true)),
            new FakeProblemStatsSink(), new FakeQuizLiveMarker(), TimeProvider.System, logger);
        return (controller, log, logger);
    }

    [Fact]
    public async Task TheCountsDrain_WhoseReleaseFails_KeepsItsCount_AndLogsTheRelease()
    {
        var (c, log, logger) = Make();

        var summary = await c.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity);

        Assert.Equal(2, summary.AnswerTypes.Total);
        Assert.Equal(1, log.Stream("A/1").ReleaseCount);
        var logged = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.Equal(RecordingProblemSetSource.ReleaseFaultMessage, logged.Exception?.Message);
    }

    [Fact]
    public async Task TheCountsDrain_WhoseDrawFaults_ReportsTheFault_NotTheFailedRelease()
    {
        var (c, log, logger) = Make(faultAtDraw: 2);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => c.SummarizeMatchesAsync(new FilterConfig(), PlayRanking.Equity));

        Assert.Equal(RecordingProblemSetSource.FaultMessage, thrown.Message);
        Assert.Equal(1, log.Stream("A/1").ReleaseCount);
        var logged = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.Equal(RecordingProblemSetSource.ReleaseFaultMessage, logged.Exception?.Message);
        Assert.Contains(RecordingProblemSetSource.FaultMessage, logged.Message);
    }

    [Fact]
    public async Task DisposingTheController_WhoseRunsReleaseFails_Throws_Nothing_AndLogsTheRelease()
    {
        var (c, log, logger) = Make();
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);

        await c.DisposeAsync();

        Assert.Equal(1, log.Stream("A/1").ReleaseCount);
        var logged = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.Equal(RecordingProblemSetSource.ReleaseFaultMessage, logged.Exception?.Message);
    }
}
