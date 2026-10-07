using BgDataTypes_Lib;
using BgQuiz_Blazor.Client.Quiz;
using BgQuiz_Blazor.E2eTests;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="ParsedProblemSet"/> — the value the parse-once cache
/// stores: decisions and the report of the walk that produced them, as one
/// thing (halheinrich/backgammon#368). What the constructor refuses is the
/// whole contract: a parse result is a <i>completed</i> walk's, so no partial
/// report can ever be stored, retained or read as if it were whole.
/// </summary>
public class ParsedProblemSetTests
{
    [Fact]
    public void Ctor_PairsTheDecisionsWithTheirReport()
    {
        var report = TestFixtures.WalkedReport();
        var decision = TestFixtures.CubeDecision();

        var parsed = new ParsedProblemSet([decision], report);

        Assert.Same(report, parsed.Report);
        Assert.Same(decision, Assert.Single(parsed.Decisions));
    }

    [Fact]
    public void Ctor_DefaultDecisions_AreRefused()
    {
        // A default ImmutableArray holds nothing at all — "none" is an empty
        // one — the same refusal the holder's own arrays make.
        var refused = Assert.Throws<ArgumentException>(
            () => new ParsedProblemSet(default, TestFixtures.WalkedReport()));
        Assert.Equal("decisions", refused.ParamName);
    }

    [Fact]
    public void Ctor_NullReport_IsRefused()
    {
        var refused = Assert.Throws<ArgumentNullException>(
            () => new ParsedProblemSet([], null!));
        Assert.Equal("report", refused.ParamName);
    }

    [Fact]
    public void Ctor_ReportNobodyWalked_IsRefused()
    {
        // A fresh report belongs to no walk and has concluded nothing; storing
        // it beside decisions would claim a complete selection nobody read.
        var refused = Assert.Throws<ArgumentException>(
            () => new ParsedProblemSet([], new SourceReport()));
        Assert.Equal("report", refused.ParamName);
    }

    [Fact]
    public void Ctor_ReportOfAWalkStoppedEarly_IsRefused()
    {
        // The real shape of a partial walk: an enumeration abandoned after its
        // first decision, over a readable file. The producer leaves the report
        // incomplete — it reached a source but not the end — and that is
        // exactly the report this type must never accept, because AllRejected
        // and the rejection list say nothing about the files never reached.
        var report = new SourceReport();
        var iterator = new FilteredDecisionIterator(
            new DecisionFilterSet(), PlayRanking.Equity, NullLogger<FilteredDecisionIterator>.Instance);
        var streams = new[] { new XgFileStream("match.xg", new MemoryStream(SyntheticXgMatch.Bytes())) };
        var first = iterator.IterateXgStreamDiagrams(streams, report).First();
        Assert.NotNull(first); // the walk did start — this is a partial report, not an unclaimed one

        Assert.False(report.IsComplete);
        var refused = Assert.Throws<ArgumentException>(() => new ParsedProblemSet([first], report));
        Assert.Equal("report", refused.ParamName);
    }
}
