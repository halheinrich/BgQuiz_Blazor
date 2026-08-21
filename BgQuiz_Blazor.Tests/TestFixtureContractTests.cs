using BgDataTypes_Lib;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The one invariant <see cref="TestFixtures"/> asserts about itself, pinned so
/// it cannot lapse silently: <b>every decision fixture in that file is a real
/// position with a derivable <see cref="ProblemKey"/></b>
/// (<see cref="TestFixtures.KeyOf"/>'s documented premise). Tests that mean to
/// exercise the no-key rung build their malformed record where they use it.
///
/// <para>
/// This matters because the rung is silent by design: a fixture that loses its
/// key does not throw where it is built — dedupe passes it through unmerged and
/// stats decline to record it — so a whole suite can go on running against
/// problems the app has quietly stopped keying. That is exactly what the
/// Jacoby-in-money-keys change did here (halheinrich/backgammon#120): the money
/// fixtures fell off the key, and what surfaced it was the handful of tests that
/// happen to ask for a key, not the many that merely need one to exist.
/// </para>
/// </summary>
public class TestFixtureContractTests
{
    public static TheoryData<string, BgDecisionData> EveryDecisionFixture() => new()
    {
        { "money play", TestFixtures.TwoChoiceDecision(TestFixtures.MakePlay((8, 5)), TestFixtures.MakePlay((13, 10))) },
        { "match play", TestFixtures.TwoChoiceDecision(TestFixtures.MakePlay((8, 5)), TestFixtures.MakePlay((13, 10)), away: 3) },
        { "money cube", TestFixtures.CubeDecision() },
        { "match cube", TestFixtures.CubeDecision(away: 3) },
        { "bear-off one", TestFixtures.BearOffOneDecision() },
        { "pass position", TestFixtures.PassDecision() },
    };

    [Theory]
    [MemberData(nameof(EveryDecisionFixture))]
    public void EveryDecisionFixture_HasADerivableProblemKey(string which, BgDecisionData fixture)
    {
        Assert.True(
            ProblemKey.TryDerive(fixture, out _),
            $"The '{which}' fixture has no derivable ProblemKey.");
    }

    [Fact]
    public void MoneyFixture_WithoutItsJacobyStamp_HasNoKeyAtAll()
    {
        // Non-vacuity for the stamp: the money fixtures derive a key *because*
        // they say which Jacoby rule they mean. Strip that one fact from an
        // otherwise identical record and the key is gone — so the stamp is
        // load-bearing rather than decoration that happens to be true today.
        var stamped = TestFixtures.CubeDecision();
        Assert.NotNull(stamped.Position.IsJacoby);   // the premise, asserted
        Assert.True(ProblemKey.TryDerive(stamped, out _));

        var unstamped = Unstamped(stamped);

        Assert.False(ProblemKey.TryDerive(unstamped, out _));
    }

    [Fact]
    public void MatchFixture_CarriesNoJacobyStamp()
    {
        // The other half of the rule: off money the fact is meaningless, so a
        // match fixture asserts no answer to a question its score never poses.
        Assert.Null(TestFixtures.CubeDecision(away: 3).Position.IsJacoby);
        Assert.Null(TestFixtures
            .TwoChoiceDecision(TestFixtures.MakePlay((8, 5)), TestFixtures.MakePlay((13, 10)), away: 3)
            .Position.IsJacoby);
    }

    [Fact]
    public void MoneyFixtures_DifferingOnlyInTheJacobyRule_AreDifferentProblems()
    {
        // …and the stamp reaches identity, which is why it had to be said. Two
        // money records alike in every other fact are two different problems,
        // pinned without restating the producer's key grammar: the claim is that
        // the fact separates them, not how it is spelled.
        var jacobyOn = TestFixtures.CubeDecision();
        var jacobyOff = WithJacoby(jacobyOn, false);

        Assert.NotEqual(TestFixtures.KeyOf(jacobyOn), TestFixtures.KeyOf(jacobyOff));
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
        Assert.NotEqual(here.Descriptive.SourceFile, there.Descriptive.SourceFile);
        Assert.NotEqual(here.Descriptive.Game, there.Descriptive.Game);
        Assert.NotEqual(here.Descriptive.MoveNumber, there.Descriptive.MoveNumber);

        Assert.Equal(TestFixtures.KeyOf(here), TestFixtures.KeyOf(there));
    }

    [Fact]
    public void AnXgpAndAnXgRecordOfTheSamePosition_AreTheSameProblem()
    {
        // The other axis of the same claim, and the one the locator's .xgp
        // branch makes worth stating (SPEC-quiz-view.md §4 ruling (ii)): the
        // chip now reads the record's IDENTITY KIND to decide what to display,
        // so it is worth pinning that the kind reaches display and stops there.
        // The same position exported as a standalone .xgp and met inside its
        // match carries two different DecisionIds — that asymmetry is by design
        // (see DecisionId) — and remains one problem to the stats document, so
        // answering it in one form counts against the other.
        var standalone = TestFixtures.CubeDecision(
            location: TestFixtures.SourceLocation.OnePosition("position.xgp"));
        var inMatch = TestFixtures.CubeDecision(
            location: TestFixtures.SourceLocation.InMatch("match.xg", 2, 37));

        // The premise, again asserted: the identities really are different
        // shapes, so the equality below is the key ignoring the id entirely.
        Assert.IsType<XgpDecisionId>(standalone.Id);
        Assert.IsType<XgDecisionId>(inMatch.Id);

        Assert.Equal(TestFixtures.KeyOf(standalone), TestFixtures.KeyOf(inMatch));
    }

    private static BgDecisionData Unstamped(BgDecisionData decision) =>
        WithJacoby(decision, null);

    /// <summary>
    /// <paramref name="decision"/> with its Jacoby fact replaced and every other
    /// fact carried over — the only way to vary one fact of a fixture, since
    /// <c>PositionData</c> is init-only and not a record.
    /// </summary>
    private static BgDecisionData WithJacoby(BgDecisionData decision, bool? isJacoby) => new()
    {
        Id = decision.Id,
        Xgid = decision.Xgid,
        Position = new PositionData
        {
            Mop = decision.Position.Mop,
            OnRollNeeds = decision.Position.OnRollNeeds,
            OpponentNeeds = decision.Position.OpponentNeeds,
            IsCrawford = decision.Position.IsCrawford,
            CubeSize = decision.Position.CubeSize,
            CubeOwner = decision.Position.CubeOwner,
            IsJacoby = isJacoby,
        },
        Decision = decision.Decision,
        Descriptive = decision.Descriptive,
    };
}
