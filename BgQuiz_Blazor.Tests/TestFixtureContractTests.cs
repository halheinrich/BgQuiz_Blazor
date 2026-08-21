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
