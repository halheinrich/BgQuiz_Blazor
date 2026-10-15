using BgDataTypes_Lib;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The run's order (<c>SPEC-filtering.md</c> §7) as the controller wires it:
/// every run's source is the producer's <see cref="MixedProblemSetSource"/>
/// over the factory's pool, with the Order handed to Start or Restart, the
/// run's ranking, the effective mix and the stats provider — or none when
/// there is no stats document — and the library decides which application
/// the order gets (§5: the mix selects, the order presents). What each order
/// <i>means</i> is the producer's to pin; what is pinned here is that the
/// order the caller hands over is the one the run presents in, under a blank
/// mix and under an active one, and that a Restart takes the one handed to it.
/// </summary>
public class QuizControllerOrderTests
{
    private static Play BestPlay() => TestFixtures.OpeningBest();
    private static Play AltPlay() => TestFixtures.OpeningAlternative();

    private static QuizController Make(out FakeProblemStatsSink sink, params BgDecisionData[] items)
    {
        var source = new FakeProblemSetSource(items);
        sink = new FakeProblemStatsSink();
        return new QuizController(
            (_, _) => TestFixtures.Composed(source), sink, TimeProvider.System, NullLogger<QuizController>.Instance);
    }

    /// <summary>
    /// Drain the run, skipping, and return the problems it presented in the
    /// order it presented them; fails rather than looping if the run would
    /// present more than <paramref name="bound"/>.
    /// </summary>
    private static async Task<List<BgDecisionData>> PresentedAsync(QuizController c, int bound)
    {
        var presented = new List<BgDecisionData>();
        while (c.Current is { } current)
        {
            Assert.True(presented.Count < bound, "The run presented more problems than its source holds.");
            presented.Add(current);
            await c.NextAsync();
        }
        return presented;
    }

    /// <summary>Six problems of one match whose move numbers run against file order: 6, 5, …, 1.</summary>
    private static CheckerPlayDecision[] AgainstFileOrder() =>
        [.. Enumerable.Range(0, 6).Select(i => TestFixtures.TwoChoiceDecision(
            BestPlay(), AltPlay(), away: i + 1,
            location: TestFixtures.SourceLocation.InMatch("match.xg", game: 1, moveNumber: 6 - i)))];

    // -----------------------------------------------------------------------
    //  The four orders, under a blank mix
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AsRead_PresentsInFileOrder()
    {
        var items = AgainstFileOrder();
        var c = Make(out _, items);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);

        Assert.Equal(items, await PresentedAsync(c, items.Length));
    }

    [Fact]
    public async Task ByMoveNumberAscending_PresentsByTheNumberAlone()
    {
        var items = AgainstFileOrder();
        var c = Make(out _, items);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.ByMoveNumberAscending);

        Assert.Equal(items.Reverse(), await PresentedAsync(c, items.Length));
    }

    [Fact]
    public async Task Shuffled_PresentsTheSameProblems_InAnotherOrder()
    {
        // Ten problems: the chance a uniform shuffle lands on file order is
        // 1 in 10!, about 3e-7, so file order here means the order was not
        // applied, never bad luck.
        var items = Enumerable.Range(0, 10)
            .Select(i => TestFixtures.TwoChoiceDecision(
                BestPlay(), AltPlay(), id: new XgpDecisionId($"p{i}.xgp"), away: i + 1))
            .ToArray();
        var c = Make(out _, items);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.Shuffled);
        var presented = await PresentedAsync(c, items.Length);

        Assert.Equal(items.Cast<BgDecisionData>().ToHashSet(), presented.ToHashSet());
        Assert.NotEqual(items, presented);
    }

    /// <summary>
    /// Three problems whose errors — XG's, for the move recorded as played —
    /// run 0.0, 0.10, 0.05 in file order; <c>X</c>, the first, is the one the
    /// player's own record says they lose most on (see the mixed-key test).
    /// </summary>
    private static (CheckerPlayDecision X, CheckerPlayDecision Y, CheckerPlayDecision Z) ThreeErrors() =>
    (
        TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.30, recordedPlayIndex: 0,
            id: new XgpDecisionId("x.xgp"), away: 1),
        TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.10, recordedPlayIndex: 1,
            id: new XgpDecisionId("y.xgp"), away: 2),
        TestFixtures.TwoChoiceDecision(BestPlay(), AltPlay(), play2Loss: 0.05, recordedPlayIndex: 1,
            id: new XgpDecisionId("z.xgp"), away: 3)
    );

    [Fact]
    public async Task ByErrorLargestFirst_WithNoMix_ReadsXgsErrorForTheMovePlayed()
    {
        var (x, y, z) = ThreeErrors();
        var c = Make(out _, x, y, z);

        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.ByErrorLargestFirst);

        Assert.Equal([y, z, x], await PresentedAsync(c, 3));
    }

    // -----------------------------------------------------------------------
    //  Under an active mix: the composer applies the order, by the mixed key
    // -----------------------------------------------------------------------

    private static QuizMix EverythingElse() => new([new QuizMixEntry(QuizCategory.EverythingElse, 100)]);

    /// <summary>
    /// With a mix in effect the by-error key is the player's lifetime loss per
    /// answer where the record holds one, and XG's error otherwise — so the
    /// same three problems present in another order than without the mix,
    /// which is what shows the order went to the composer with the stats
    /// provider rather than being applied plainly over its output.
    /// </summary>
    [Fact]
    public async Task ByErrorLargestFirst_UnderAnActiveMix_ReadsTheLifetimeLossPerAnswer()
    {
        var (x, y, z) = ThreeErrors();
        var c = Make(out var sink, x, y, z);
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty.Plus(
            TestFixtures.Scored(x, AltPlay(), PlayRanking.Equity), TimeProvider.System); // x answered at a loss of 0.30

        await c.StartAsync(new FilterConfig(), EverythingElse(), PlayRanking.Equity, ProblemOrder.ByErrorLargestFirst);

        Assert.NotNull(c.LastComposition); // composed
        Assert.Equal([x, y, z], await PresentedAsync(c, 3));
    }

    [Fact]
    public async Task ByMoveNumberAscending_UnderAnActiveMix_IsTheWeightedRunsOrder()
    {
        var items = AgainstFileOrder();
        var c = Make(out var sink, items);
        sink.CanWeightMix = true;
        sink.CurrentDocument = ProblemStatsDocument.Empty;

        await c.StartAsync(new FilterConfig(), EverythingElse(), PlayRanking.Equity, ProblemOrder.ByMoveNumberAscending);

        Assert.NotNull(c.LastComposition);
        Assert.Equal(items.Reverse(), await PresentedAsync(c, items.Length));
    }

    // -----------------------------------------------------------------------
    //  The wiring's other halves
    // -----------------------------------------------------------------------

    /// <summary>
    /// A blank mix with no stats document is handed no provider, and needs
    /// none: the run starts and presents in the order handed over.
    /// </summary>
    [Fact]
    public async Task ABlankMix_WithNoStatsDocument_StartsInTheOrderHandedOver()
    {
        var items = AgainstFileOrder();
        var c = Make(out var sink, items);
        sink.CurrentDocument = null;

        var outcome = await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.ByMoveNumberAscending);

        Assert.Equal(QuizStartOutcome.Started, outcome);
        Assert.Null(c.LastComposition);
        Assert.Equal(items.Reverse(), await PresentedAsync(c, items.Length));
    }

    /// <summary>
    /// The order is read where a Start or Restart builds the run's source
    /// (SPEC-quiz-history.md §8), never stored with the run's replay: a
    /// Restart presents in the Order handed to it.
    /// </summary>
    [Fact]
    public async Task Restart_PresentsInTheOrderChosenAtRestart()
    {
        var items = AgainstFileOrder();
        var c = Make(out _, items);
        await c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, ProblemOrder.AsRead);
        Assert.Equal(items, await PresentedAsync(c, items.Length));

        await c.RestartAsync(PlayRanking.Equity, ProblemOrder.ByMoveNumberAscending);

        Assert.Equal(items.Reverse(), await PresentedAsync(c, items.Length));
    }

    /// <summary>
    /// An undefined order is a caller bug refused at the controller's door,
    /// before anything begins — the stats context is not re-bound.
    /// </summary>
    [Fact]
    public async Task AnUndefinedOrder_IsRefusedBeforeAnythingBegins()
    {
        var c = Make(out var sink, AgainstFileOrder());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => c.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity, (ProblemOrder)99));

        Assert.Equal(0, sink.BeginQuizCallCount);
        Assert.False(c.HasStarted);
    }
}
