using System.Collections.Immutable;
using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging.Abstractions;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The halheinrich/backgammon#273 real-file case: a play entered with the hit marked on
/// the other checker is the same play XG recorded, so it scores as that candidate and not
/// as an off-list skip. Reads the FixtureFiles match file through the app's own loader.
/// </summary>
[Trait("Category", "RequiresFixtureFiles")]
public class PlayIdentityRealFileTests
{
    private const string FixtureNamePrefix = "Shimodaira-Suzuki 2010 Japan Open Final";
    private const string FixtureExtension = ".xg";

    private static string FixtureDirectory =>
        Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "TestData", "FixtureFiles"));

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

    private static async Task<CheckerPlayDecision> ReportedDecisionAsync()
    {
        var path = FixturePath;
        ImmutableArray<PickedFile> files = [new PickedFile(Path.GetFileName(path), [.. File.ReadAllBytes(path)])];
        var loaded = await ProblemLoader.LoadAsync(
            files, NullLoggerFactory.Instance, TimeProvider.System, progress: null, CancellationToken.None);
        return Assert.Single(
            loaded.Problems.OfType<CheckerPlayDecision>(),
            d => d.Id is XgDecisionId { Game: 4, MoveNumber: 33, IsCube: false });
    }

    [Fact]
    public async Task ThePlayWithTheHitOnTheOtherChecker_ScoresAsTheCandidate_NotAsAnOffListSkip()
    {
        var decision = await ReportedDecisionAsync();
        Assert.Equal(new DiceRoll(5, 4), decision.Dice);
        var recorded = decision.Decision.Plays[0].Play;
        var recordedMoves = Moves(recorded);
        Assert.Contains(new Move(7, -3), recordedMoves);
        Assert.Contains(new Move(8, 3), recordedMoves);
        var entered = Play.Create(new(8, -3), new(7, 3));
        Assert.False(entered.IsSameEncoding(recorded)); // two encodings, so matching them is the claim

        var run = QuizRun.Begin(PlayRanking.Equity, [decision]).PresentNext(randomHomeBoardOnRight: false);
        Assert.Same(decision, run.Cursor!.Problem);
        run = run.SubmitPlay(entered, out _);

        Assert.Equal(0, run.SkippedCount);                              // not an off-list skip
        var review = Assert.IsType<ProblemReview.Play>(run.Review);
        Assert.Equal(PlaySubmissionKind.Scored, review.Submission.Kind);
        Assert.True(review.Submission.TryGetScored(out var submitted));
        Assert.Equal(0, submitted.MatchedCandidateIndex);               // the candidate XG recorded
        Assert.True(submitted.IsCorrect);                               // and it was the best play
        Assert.Equal(1, run.Score.PlayDecisions.Correct);               // and it is what the score counts
    }

    private static List<Move> Moves(Play play)
    {
        var moves = new List<Move>();
        foreach (var move in play)
            moves.Add(move);
        return moves;
    }
}
