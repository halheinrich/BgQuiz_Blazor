using BgQuiz_Blazor.Client.Quiz;
using BgQuiz_Blazor.E2eTests;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The copy-drift tripwire (issue <c>halheinrich/backgammon#4</c>, deliverable
/// 2): for a few <b>load-bearing</b> phrases, the app-side constant still
/// contains the literal the browser suite pins.
///
/// <para>
/// <b>What this is, said honestly, because it reads as tautological
/// otherwise.</b> It is a <i>detector</i>, not a guarantee. Nothing stops
/// someone editing the app constant and the e2e literal together, and then
/// these tests pass while the words a user reads have changed completely —
/// which is exactly what the umbrella's copy-pin SSOT ruling wants to be
/// possible, deliberately. What it catches is the <i>one-sided</i> edit: the
/// app's wording moved and the browser suite's expectation did not. That
/// mismatch is otherwise found by a publish plus a browser run, or on CI, and
/// here it is found in the two-second unit suite with a message naming both
/// sides.
/// </para>
///
/// <para>
/// <b>Why the reference asymmetry makes this possible at all.</b> The e2e
/// project references no app assembly by design — its literals are an
/// independent oracle, and importing the constant there would make the
/// assertion pass against an emptied constant. This project references both app
/// assemblies, and compiles the e2e suite's <c>ExpectedText</c> as a linked
/// file, so it can hold the two spellings against each other without either
/// suite losing what it is for.
/// </para>
///
/// <para>
/// <b>Why only these two.</b> The issue's criterion is load-bearing copy — its
/// own example is the write-access consequence, "a data-protection message
/// behind a permission path". Two phrases meet it and have an app-side C#
/// constant to compare against, which the rest of the pinned surface does not:
/// </para>
///
/// <list type="bullet">
///   <item><description><b>In</b> — the write-access consequence
///     (<c>FolderPickDisplay.WriteAccessConsequence</c>): it tells a user what
///     is lost when the folder cannot be written, on a permission path most
///     runs never take, and understating it is how someone decides a permission
///     does not matter.</description></item>
///   <item><description><b>In</b> — the stats-unreadable refusal
///     (<c>MixDisplay.RefusalReason</c>): the one sentence that says why a
///     weighted quiz was refused, on the degrade path an unreadable stats file
///     produces. A reader who does not learn the file could not be read has no
///     way to find out.</description></item>
///   <item><description><b>Out</b> — the buttons, the nav links, the verdicts,
///     the counts, the headings: ordinary copy on paths every run takes, where
///     a reword is seen immediately and the e2e suite is the right place to
///     catch it. All 44 of them would be ceremony, which the issue says in as
///     many words.</description></item>
///   <item><description><b>Out</b> — the cube labels, the solution banner and
///     the filter panel's words: producer-owned (<c>CubeLabels</c>,
///     <c>DiagramRenderer</c>, XgFilter_Razor). A tripwire over those would
///     assert against a submodule's public API rather than an app-side
///     constant, and would turn a producer's reword into a red BgQuiz unit
///     suite before the bump that carries it is even reviewed. Those re-wordings
///     arrive through a submodule bump the umbrella reviews deliberately, which
///     is the coordination this repo should not pre-empt. (The issue flags this
///     shape as one to decide rather than assume; this is the decision.)</description></item>
///   <item><description><b>Out, and worth saying</b> — the stats file's own
///     name. It is the most consequential string in the app, but the e2e side
///     keeps it on <c>FsAccessFakeTestBase</c>, whose fake directory is built
///     from it, and that type cannot be linked here (it is a Playwright
///     fixture). <c>QuizStatsFile.FileName</c> therefore has no unit-side pin
///     from this angle; the store suite pins the document's behaviour, and the
///     browser suite pins the name.</description></item>
/// </list>
/// </summary>
public class CopyDriftTripwireTests
{
    [Fact]
    public void TheWriteAccessConsequence_StillContainsWhatTheBrowserSuitePins()
    {
        Assert.Contains(
            ExpectedText.LifetimeRecordConsequence,
            FolderPickDisplay.WriteAccessConsequence,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheStatsUnreadableRefusal_StillContainsWhatTheBrowserSuitePins()
    {
        Assert.Contains(
            ExpectedText.StatsFileUnreadable,
            MixDisplay.RefusalReason(QuizStatsStatus.LoadFailed),
            StringComparison.Ordinal);
    }
}
