using BgDataTypes_Lib;
using BgFolderAccess_Razor;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The pool-composition guard (<c>../SPEC-stats-identity.md</c> §2, amended
/// 2026-08-24; issue <c>halheinrich/backgammon#142</c>): a money record that
/// does not state its Jacoby rule fails the folder load, naming the file,
/// rather than quizzing on a silent no-key rung where nothing it teaches is
/// ever recorded.
///
/// <para>
/// <b>Every malformed record here is built in this file, deliberately.</b>
/// <see cref="TestFixtureContractTests"/> pins that every fixture in
/// <see cref="TestFixtures"/> is a real position with a derivable
/// <c>ProblemKey</c>, and says in as many words that tests meaning to exercise
/// the no-key rung build their malformed record where they use it — because a
/// keyless record loose in the shared fixture library is exactly how a whole
/// suite comes to run against problems the app has quietly stopped keying.
/// <see cref="MoneyCube"/> takes the stamp as a parameter, so the stamped and
/// unstamped records differ in that one fact and nothing else, and
/// <see cref="MoneyCube_DiffersFromItsStampedTwin_OnlyInTheKeyItLoses"/>
/// asserts that premise rather than assuming it.
/// </para>
/// </summary>
public class JacobyStampedProblemSetSourceTests
{
    // -----------------------------------------------------------------------
    //  Ad-hoc records — see the class remarks for why they live here
    // -----------------------------------------------------------------------

    /// <summary>
    /// A money cube decision from <paramref name="sourceFile"/>, carrying
    /// <paramref name="isJacoby"/> as its Jacoby fact — <see langword="null"/>
    /// for the shape this guard exists to catch. Real in every other respect:
    /// standard board, money score (<c>0</c>-away/<c>0</c>-away), a decided
    /// cube analysis.
    /// </summary>
    private static BgDecisionData MoneyCube(string sourceFile, bool? isJacoby) => new()
    {
        Id = new XgDecisionId(sourceFile, Game: 1, MoveNumber: 4, IsCube: true),
        Position = new PositionData
        {
            Mop = TestFixtures.StandardMop(),
            OnRollNeeds = 0,
            OpponentNeeds = 0,
            IsJacoby = isJacoby,
        },
        Decision = new DecisionData
        {
            IsCube = true,
            NoDoubleEquity = 0.5,
            DoubleTakeEquity = 0.7,
        },
        Descriptive = new DescriptiveData
        {
            OnRollName = "Alice",
            OpponentName = "Bob",
            SourceFile = sourceFile,
            Game = 1,
            MoveNumber = 4,
        },
    };

    /// <summary>The guard over a fixed pool, as the factory wires it over the parse.</summary>
    private static JacobyStampedProblemSetSource Guarding(params BgDecisionData[] pool) =>
        new(new FakeProblemSetSource(pool));

    private static async Task<List<BgDecisionData>> DrainAsync(IProblemSetSource source)
    {
        var drained = new List<BgDecisionData>();
        await foreach (var decision in source.EnumerateAsync())
        {
            drained.Add(decision);
        }

        return drained;
    }

    // -----------------------------------------------------------------------
    //  The premise the whole suite rests on
    // -----------------------------------------------------------------------

    [Fact]
    public void MoneyCube_DiffersFromItsStampedTwin_OnlyInTheKeyItLoses()
    {
        // Non-vacuity for every pin below: the unstamped record is not merely
        // malformed somehow — it is a position the app would key and quiz
        // happily the moment the Jacoby fact is supplied, and loses its key for
        // that reason alone. Without this, a typo in the board or the cube
        // analysis could make every throw below fire for the wrong cause.
        Assert.True(ProblemKey.TryDerive(MoneyCube("stamped.xg", isJacoby: true), out _));
        Assert.False(ProblemKey.TryDerive(MoneyCube("stamped.xg", isJacoby: null), out _));
    }

    // -----------------------------------------------------------------------
    //  The guard
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("money-session.xg")]
    [InlineData("another session (1).xg")]
    public async Task UnstampedMoneyRecord_FailsTheLoad_NamingItsOwnFile(string sourceFile)
    {
        // Two names through one code path: the message carries whichever file
        // the record came from, so the name is rendered from the record rather
        // than spelled into the copy. Equality against the register is what
        // pins where the wording lives — a copy edit lands in one place or this
        // fails.
        var source = Guarding(MoneyCube(sourceFile, isJacoby: null));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DrainAsync(source));

        Assert.Contains(sourceFile, error.Message, StringComparison.Ordinal);
        Assert.Equal(
            FolderPickDisplay.MalformedForQuizzing(sourceFile, otherFileCount: 0),
            error.Message);
    }

    [Fact]
    public async Task TwoOffendingFiles_NameTheFirstAndCountTheOther()
    {
        // The multi-violation shape at its singular boundary: one name, one
        // other file. First is first in pool order, which is pick order.
        var source = Guarding(
            MoneyCube("clean.xg", isJacoby: true),
            MoneyCube("first-bad.xg", isJacoby: null),
            MoneyCube("second-bad.xg", isJacoby: null));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DrainAsync(source));

        Assert.Equal(
            FolderPickDisplay.MalformedForQuizzing("first-bad.xg", otherFileCount: 1),
            error.Message);
        Assert.Contains("1 other file", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("second-bad.xg", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManyOffendingRecords_CountFilesNotRecords()
    {
        // The count is of FILES, which is the unit the copy tells the user to
        // act on: five offending records drawn from three files report two
        // others, not four. A .xg match contributes as many records as it holds
        // decisions, so counting records would report a number nothing in the
        // folder matches.
        var source = Guarding(
            MoneyCube("first-bad.xg", isJacoby: null),
            MoneyCube("first-bad.xg", isJacoby: null),
            MoneyCube("second-bad.xg", isJacoby: null),
            MoneyCube("third-bad.xg", isJacoby: null),
            MoneyCube("third-bad.xg", isJacoby: null));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DrainAsync(source));

        Assert.Equal(
            FolderPickDisplay.MalformedForQuizzing("first-bad.xg", otherFileCount: 2),
            error.Message);
        Assert.Contains("2 other files", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StampedMoneyAndMatchRecords_PassThroughUntouched()
    {
        // The regression half, and the reason the guard is boundary-only: a
        // well-formed pool reaches the quiz exactly as it did before — same
        // records, same order, nothing dropped or reordered. The match record
        // is the load-bearing one: it carries NO Jacoby stamp, because off
        // money the fact is meaningless, and a guard that read money off
        // anything but the away scores would refuse this folder.
        var pool = new[]
        {
            MoneyCube("money.xg", isJacoby: true),
            TestFixtures.CubeDecision(away: 3),
            TestFixtures.TwoChoiceDecision(
                Play.Create(new(8, 5)), Play.Create(new(13, 10))),
        };
        Assert.Null(pool[1].Position.IsJacoby); // the premise, asserted
        var source = Guarding(pool);

        var drained = await DrainAsync(source);

        Assert.Equal(pool, drained);
    }

    [Fact]
    public async Task ReIterable_LikeEveryOtherSource()
    {
        // The interface's re-iterability contract, which Restart depends on:
        // the guard buffers a pool to check it, and a buffer is exactly the
        // shape that can be consumed once by accident.
        var source = Guarding(
            MoneyCube("money.xg", isJacoby: true),
            TestFixtures.CubeDecision(away: 3));

        Assert.Equal(await DrainAsync(source), await DrainAsync(source));
    }

    [Fact]
    public void NullInner_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new JacobyStampedProblemSetSource(null!));

    [Fact]
    public void NameAndCount_ForwardTheInnerSource()
    {
        var inner = new FakeProblemSetSource([TestFixtures.CubeDecision()], name: "Picked folder");
        var source = new JacobyStampedProblemSetSource(inner);

        Assert.Equal(inner.Name, source.Name);
        Assert.Equal(inner.Count, source.Count);
    }

    // -----------------------------------------------------------------------
    //  In the stack Program.cs registers
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TheProductionComposition_CarriesTheGuard()
    {
        // The wiring pin: the guard is only enforcement if it is IN the stack,
        // and a decorator tested alone cannot say that it is. This drives
        // PickedFolderSourceFactory.Create — the single statement of the layer
        // order — over synthesized records, by seeding the holder's parse cache
        // through its own StoreParsed so the parse-once layer adopts them
        // instead of reading the picked bytes. That is the only way to put a
        // record the converter cannot emit into the real composition: the wire
        // side has no fixture for a shape no producer writes.
        var composed = CompositionOver(MoneyCube("money-session.xg", isJacoby: null));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DrainAsync(composed.Source));

        Assert.Equal(
            FolderPickDisplay.MalformedForQuizzing("money-session.xg", otherFileCount: 0),
            error.Message);
    }

    [Fact]
    public async Task TheProductionComposition_LeavesAStampedFolderAlone()
    {
        // The same wire with the one fact supplied: the folder loads and the
        // stack serves it. Without this the pin above would pass just as well
        // against a composition that refused every folder.
        var composed = CompositionOver(MoneyCube("money-session.xg", isJacoby: true));

        Assert.Single(await DrainAsync(composed.Source));
    }

    /// <summary>
    /// The production stack over <paramref name="pool"/> — a landed pick whose
    /// parse cache has been seeded, so the parse-once layer adopts these
    /// records and the picked bytes are never read.
    /// </summary>
    private static ComposedProblemSource CompositionOver(params BgDecisionData[] pool)
    {
        var picked = new PickedProblemFolder();
        picked.Set(
            "corpus",
            [new PickedFile("money-session.xg", [1, 2, 3])],
            FolderWriteCapability.BrowserUnsupported,
            []);
        picked.StoreParsed(picked.PickGeneration, pool);

        var factory = PickedFolderSourceFactory.Create(
            picked, new ShuffleOption(), NullLoggerFactory.Instance, TimeProvider.System);
        return factory(new DecisionFilterSet(), QuizMix.Empty);
    }

    // -----------------------------------------------------------------------
    //  The copy
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0, "that file is malformed")]
    [InlineData(1, "1 other file")]
    [InlineData(4, "4 other files")]
    public void TheCopy_SaysWhatIsWrong_AndNamesTheFile(int otherFileCount, string expectedPhrase)
    {
        var copy = FolderPickDisplay.MalformedForQuizzing("money-session.xg", otherFileCount);

        Assert.Contains("money-session.xg", copy, StringComparison.Ordinal);
        Assert.Contains("Jacoby", copy, StringComparison.Ordinal);
        Assert.Contains(expectedPhrase, copy, StringComparison.Ordinal);
        // Home's banner supplies its own "Could not start quiz:" lead, so the
        // copy must open on the cause rather than on a second headline.
        Assert.DoesNotContain("Could not", copy, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCopy_RejectsAnUnnamedFile() =>
        Assert.Throws<ArgumentException>(
            () => FolderPickDisplay.MalformedForQuizzing("  ", otherFileCount: 0));

    [Fact]
    public void TheCopy_RejectsANegativeCount() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FolderPickDisplay.MalformedForQuizzing("money-session.xg", otherFileCount: -1));
}
