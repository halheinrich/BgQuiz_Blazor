using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using BgMoveGen;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// What <see cref="TestFixtures"/> asserts about itself, pinned so it cannot
/// lapse silently.
///
/// <para>
/// <b>Every play a fixture holds is legal from its position.</b> The records
/// hold every candidate to the play rule — a play that cannot be played from
/// the position is refused where the record is built — but that rule leaves
/// the dice to the move generator (<c>BoardState.IsSamePlay</c>'s remarks), so
/// a fixture could still list a play no roll produces. The fixtures once did
/// (<c>8/5 8/5</c> on a 3-1), and a play matched them only because plays were
/// compared by encoding. Plays compare by the position they reach now, and a
/// fixture is only a scenario if a board could really be played into it — so
/// each candidate here must be one of the plays the generator makes for the
/// fixture's roll.
/// </para>
///
/// <para>
/// <b>What is no longer pinned here, and why.</b> This class used to pin that
/// every fixture had a derivable <see cref="ProblemKey"/>, because the no-key
/// rung was silent and a fixture could fall off it unnoticed (the
/// Jacoby-in-money-keys change once did exactly that). Every record has a key
/// now, by the producer's guarantee (SPEC-stats-identity.md §2, amended
/// 2026-09-27): the rung is unreachable, so its pins went with it.
/// </para>
/// </summary>
public class TestFixtureContractTests
{
    public static TheoryData<string, CheckerPlayDecision> EveryCheckerPlayFixture() => new()
    {
        { "money play", TestFixtures.TwoChoiceDecision(TestFixtures.OpeningBest(), TestFixtures.OpeningAlternative()) },
        { "match play", TestFixtures.TwoChoiceDecision(TestFixtures.OpeningBest(), TestFixtures.OpeningAlternative(), away: 3) },
        { "depth split", TestFixtures.DepthSplitDecision() },
        { "one-click play", TestFixtures.OneClickPlayDecision() },
        { "forced play", TestFixtures.ForcedPlayDecision() },
        { "forced double", TestFixtures.ForcedDoubleDecision() },
        { "forced bear-off", TestFixtures.ForcedBearOffDecision() },
        { "pass position", TestFixtures.PassDecision() },
    };

    [Theory]
    [MemberData(nameof(EveryCheckerPlayFixture))]
    public void EveryCandidate_IsALegalPlayOfItsRoll(string which, CheckerPlayDecision fixture)
    {
        var start = new BoardState(fixture.Board);
        var legal = MoveGenerator.GeneratePlays(start, fixture.Dice.High, fixture.Dice.Low);

        foreach (var candidate in fixture.Decision.Plays)
        {
            Assert.True(
                start.IndexOfSamePlay(candidate.Play, legal) >= 0,
                $"The '{which}' fixture lists {candidate.Notation}, which no {fixture.Dice} makes from its position.");
        }
    }

    [Fact]
    public void TheOpeningHelpers_AreLegalAndNotTheSamePlay()
    {
        // The three plays the controller and page suites submit: each is legal
        // for the opening 3-1, and no two are the same play — so a submission of
        // one can never match another's candidate, whichever way it is encoded.
        var start = new BoardState(BoardPosition.Standard);
        var legal = MoveGenerator.GeneratePlays(start, 3, 1);
        Play[] helpers =
        [
            TestFixtures.OpeningBest(),
            TestFixtures.OpeningAlternative(),
            TestFixtures.OpeningUnlisted(),
            TestFixtures.DepthSplitThirdPlay(),
        ];

        foreach (var play in helpers)
            Assert.True(start.IndexOfSamePlay(play, legal) >= 0, $"{play.ToNotation()} is not legal for 3-1.");

        for (int i = 0; i < helpers.Length; i++)
            for (int j = i + 1; j < helpers.Length; j++)
                Assert.False(start.IsSamePlay(helpers[i], helpers[j]),
                    $"{helpers[i].ToNotation()} and {helpers[j].ToNotation()} are one play.");
    }

    [Fact]
    public void MoneyFixtures_DifferingOnlyInTheJacobyRule_AreDifferentProblems()
    {
        // The Jacoby rule reaches identity: two money records alike in every
        // other fact are two different problems, pinned without restating the
        // producer's key grammar — the claim is that the fact separates them,
        // not how it is spelled.
        var jacobyOn = TestFixtures.CubeDecision();
        var jacobyOff = TestRecords.Cube(
            id: jacobyOn.Id,
            position: TestRecords.Position(
                cubeSize: jacobyOn.Position.CubeSize,
                cubeOwner: jacobyOn.Position.CubeOwner,
                session: TestRecords.MoneySession(isJacoby: false)),
            decision: jacobyOn.Decision,
            descriptive: jacobyOn.Descriptive);

        Assert.NotEqual(ProblemKey.From(jacobyOn), ProblemKey.From(jacobyOff));
    }

    [Fact]
    public void FixturesDifferingOnlyInWhereTheyCameFrom_AreTheSameProblem()
    {
        // The locator's whole safety claim, proved rather than asserted
        // (SPEC-quiz-view.md §4, issue halheinrich/backgammon#115): the file
        // name and the game/move coordinates it displays are display facts, and
        // display facts are not identity. Two records alike in every position
        // and decision fact but drawn from different files, games and moves are
        // ONE problem — which is what lets the chip name a file while
        // SPEC-stats-identity.md goes on keying by content, and what makes the
        // dedupe still collapse the same position met twice under two names.
        //
        // The counterpart above (MoneyFixtures_DifferingOnlyInTheJacobyRule…)
        // is the same shape with the opposite verdict, so neither can pass by
        // the key having stopped discriminating anything at all.
        var here = TestFixtures.CubeDecision(
            location: TestFixtures.SourceLocation.InMatch("first-match.xg", 1, 4));
        var there = TestFixtures.CubeDecision(
            location: TestFixtures.SourceLocation.InMatch("another-match.xg", 7, 31));

        // The premise: they really do differ on all three, so the equality
        // below is about the key ignoring them, not about them being alike.
        Assert.NotEqual(here.SourceFile, there.SourceFile);
        Assert.NotEqual(here.Game, there.Game);
        Assert.NotEqual(here.MoveNumber, there.MoveNumber);

        Assert.Equal(ProblemKey.From(here), ProblemKey.From(there));
    }

    [Fact]
    public void AnXgpAndAnXgRecordOfTheSamePosition_AreTheSameProblem()
    {
        // The other axis of the same claim: the same position exported as a
        // standalone .xgp and met inside its match carries two different
        // DecisionIds — that asymmetry is by design (see DecisionId), and it is
        // what the locator shows — and remains one problem to the stats
        // document, so answering it in one form counts against the other.
        var standalone = TestFixtures.CubeDecision(
            location: TestFixtures.SourceLocation.OnePosition("position.xgp"));
        var inMatch = TestFixtures.CubeDecision(
            location: TestFixtures.SourceLocation.InMatch("match.xg", 2, 37));

        // The premise, again asserted: the identities really are different
        // shapes, so the equality below is the key ignoring the id entirely.
        Assert.IsType<XgpDecisionId>(standalone.Id);
        Assert.IsType<XgDecisionId>(inMatch.Id);

        Assert.Equal(ProblemKey.From(standalone), ProblemKey.From(inMatch));
    }
}
