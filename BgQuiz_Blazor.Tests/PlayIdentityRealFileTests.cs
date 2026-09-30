using System.Collections.Immutable;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// halheinrich/backgammon#273's own case, from the real file: in the
/// Shimodaira–Suzuki 2010 Japan Open Final match, game 4, move 33, a 5-4, a
/// tester made the 3-point on a blot and marked the hit on the checker from
/// the 8 (<c>8/3* 7/3</c>), while XG recorded the same play with the hit on
/// the checker from the 7 (<c>8/3 7/3*</c>). Compared by encoding, the app
/// scored his best play as an off-list skip. Compared by the position each
/// reaches — the producer's play identity, which this app now scores through —
/// they are one play.
///
/// <para>
/// <b>The real wire, end to end.</b> The file's bytes go through the app's own
/// in-browser parse (<see cref="WasmUploadedProblemSetSource"/>), and the
/// record it yields is answered through the controller's real scoring path.
/// <c>QuizControllerTests.SubmitPlay_TheHitMarkedOnTheOtherChecker_IsTheCandidate</c>
/// pins the same identity on a synthetic board, and runs everywhere; this one
/// holds it to the record XG actually wrote.
/// </para>
///
/// <para>
/// <b>Why it may name a file.</b> <c>TestData/FixtureFiles</c> is append-only
/// precisely so tests may name a file in it, and the umbrella's
/// <c>TestData</c> is gitignored, so this carries the
/// <c>RequiresFixtureFiles</c> trait CI filters on. Where the file is absent
/// the test fails loudly rather than skipping, as
/// <see cref="PositionDedupeTests"/> does: a repro that skips is a repro that
/// has stopped existing.
/// </para>
/// </summary>
[Trait("Category", "RequiresFixtureFiles")]
public class PlayIdentityRealFileTests
{
    /// <summary>
    /// The start of the match file's name: specific enough to name one match
    /// export, and short of the export's own numbering suffix, which the
    /// umbrella's reference check would read as a bare issue reference. The
    /// fixture is found by it (<see cref="FixturePath"/>), not spelled here.
    /// </summary>
    private const string FixtureNamePrefix = "Shimodaira-Suzuki 2010 Japan Open Final";

    /// <summary>The match file's extension: the fixture is an XG match, not a position.</summary>
    private const string FixtureExtension = ".xg";

    private static string FixtureDirectory =>
        Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "TestData", "FixtureFiles"));

    /// <summary>
    /// The one <c>.xg</c> in <see cref="FixtureDirectory"/> whose name starts
    /// with <see cref="FixtureNamePrefix"/>. Exactly one must match: none means
    /// the fixture is missing, and more than one means the prefix no longer
    /// names the file this case was reported against — both fail loudly.
    /// </summary>
    private static string FixturePath
    {
        get
        {
            var matches = Directory.Exists(FixtureDirectory)
                ? Directory.EnumerateFiles(FixtureDirectory, FixtureNamePrefix + "*")
                    .Where(path => string.Equals(Path.GetExtension(path), FixtureExtension, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : [];

            if (matches.Count == 0)
            {
                throw new FileNotFoundException(
                    $"The halheinrich/backgammon#273 real-file case needs the FixtureFiles match file whose name " +
                    $"starts '{FixtureNamePrefix}'. TestData/FixtureFiles is append-only so pinned tests may " +
                    "name files in it; this test fails loudly rather than skipping, because a repro that " +
                    "skips has stopped existing.",
                    FixtureDirectory);
            }

            return Assert.Single(matches);
        }
    }

    /// <summary>The reported decision: game 4, move 33, the checker play.</summary>
    private static async Task<CheckerPlayDecision> ReportedDecisionAsync()
    {
        var path = FixturePath;
        ImmutableArray<PickedFile> files = [new PickedFile(Path.GetFileName(path), [.. File.ReadAllBytes(path)])];
        var source = new WasmUploadedProblemSetSource(
            files, new DecisionFilterSet(), PlayRanking.Equity, NullLoggerFactory.Instance, TimeProvider.System);

        var decisions = new List<BgDecisionData>();
        await foreach (var decision in source.EnumerateAsync())
            decisions.Add(decision);

        return Assert.Single(
            decisions.OfType<CheckerPlayDecision>(),
            d => d.Id is XgDecisionId { Game: 4, MoveNumber: 33, IsCube: false });
    }

    [Fact]
    public async Task ThePlayWithTheHitOnTheOtherChecker_ScoresAsTheCandidate_NotAsAnOffListSkip()
    {
        var decision = await ReportedDecisionAsync();

        // The premise, read off the record: the roll is the 5-4, and XG's best
        // candidate carries the hit mark on the checker from the 7 — 8/3 7/3*.
        Assert.Equal(new DiceRoll(5, 4), decision.Dice);
        var recorded = decision.Decision.Plays[0].Play;
        var recordedMoves = Moves(recorded);
        Assert.Contains(new Move(7, -3), recordedMoves);
        Assert.Contains(new Move(8, 3), recordedMoves);

        // The play as the tester entered it: the hit on the checker from the 8.
        var entered = Play.Create(new(8, -3), new(7, 3));
        Assert.False(entered.IsSameEncoding(recorded)); // two encodings, so matching them is the claim

        var controller = new QuizController(
            (_, _, _) => TestFixtures.Composed(new FakeProblemSetSource([decision])),
            new FakeProblemStatsSink(), TimeProvider.System);
        await controller.StartAsync(new FilterConfig(), QuizMix.Empty, PlayRanking.Equity);
        Assert.Same(decision, controller.Current);

        controller.SubmitPlay(entered);

        Assert.Equal(0, controller.SkippedCount);                       // not an off-list skip
        var review = Assert.IsType<ProblemReview.Play>(controller.Review);
        Assert.Equal(PlaySubmissionKind.Scored, review.Submission.Kind);
        Assert.True(review.Submission.TryGetScored(out var submitted));
        Assert.Equal(0, submitted.MatchedCandidateIndex);               // the candidate XG recorded
        Assert.True(submitted.IsCorrect);                               // and it was the best play
        Assert.Equal(1, controller.Score.PlayDecisions.Correct);        // and it is what the score counts
    }

    private static List<Move> Moves(Play play)
    {
        var moves = new List<Move>();
        foreach (var move in play)
            moves.Add(move);
        return moves;
    }
}
