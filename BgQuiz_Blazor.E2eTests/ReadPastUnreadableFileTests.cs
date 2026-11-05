using System.Text;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// A long file read just after an unreadable one (halheinrich/backgammon#523).
/// On the AOT publish the issue found, a folder read as a readable
/// <c>.xg</c>, then a file refused at its header, then another readable
/// <c>.xg</c> killed the WASM runtime with Mono's
/// <c>* Assertion: should not be reached</c> in <c>sgen-scan-object.h</c>,
/// just after the walk logged the refused file's skip, and the load never
/// finished. The same files with the unreadable one last loaded normally, as
/// did the first order with AOT off.
///
/// <para>
/// <b>The remedy</b> is that a file whose magic number is not XG's is refused
/// without a throw, on every XG read (ruled on halheinrich/backgammon#523). The first scenario is
/// its regression: it failed on a build without it. The second is the order
/// control and is expected to pass either way.
/// </para>
///
/// <para>
/// <b>What was observed about the order.</b> These are observations from the
/// runs that made them, recorded on halheinrich/backgammon#523, which is the
/// record; none is derived, and none says why the runtime died. On the AOT
/// build those runs used, the runtime died when the file read right after the
/// refusal had 833 records or more and the load finished when it had 806 or
/// fewer. In the e2e arrangements tried, the file before the refusal made no
/// difference, and an unreadable file read first crashed too when a long one
/// followed it; the manual Chrome runs recorded on the same issue include a
/// case where the same next file crashed or loaded depending on what was read
/// before it. That the crash follows from AOT compilation is an inference
/// from the AOT-off build loading the same order; it is not proven. With the
/// unreadable file last nothing is read after it, which is why
/// <see cref="TheSameFilesWithTheUnreadableOneLast_LoadAsBefore"/> is the
/// order control.
/// </para>
///
/// <para>
/// <b>The fixture</b> is synthesized per run and nothing of it is committed:
/// the suite's match (<see cref="SyntheticXgMatch.Bytes"/>), a short text file
/// that fails at the header's magic number as the issue's file did, and
/// <see cref="SyntheticXgMatch.LongMatchBytes"/>, long enough in records to
/// reproduce, with one problem of its own. The outcome is positive: the
/// record names the unreadable file, and the quiz asks the long match's
/// problem, located in that file, after the match's two. A count alone would
/// not show that a file after the unreadable one was read.
/// </para>
///
/// <para>
/// <b>Failing fast, and for this reason only.</b> When the runtime dies the
/// pick's own wait for the folder summary (which appears once the parse is
/// done) runs to its timeout, and a timeout does not say why. So every pick
/// here is watched: the page's console is listened to from before the pick
/// to the end of the awaited call, and the assertion's text, captured there,
/// is what the scenario fails with. A pick that fails any other way fails
/// with its own error, never as this one.
/// </para>
/// </summary>
public sealed class ReadPastUnreadableFileTests : E2eTestBase
{
    public ReadPastUnreadableFileTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output) { }

    /// <summary>The unreadable file's staged name — what the record must show.</summary>
    private const string UnreadableFileName = "not-a-match.xg";

    /// <summary>
    /// The unreadable file: a line of text, so the read refuses it at the first
    /// thing it checks, the header's magic number.
    /// </summary>
    private static byte[] UnreadableBytes() =>
        Encoding.ASCII.GetBytes("Plain text, not a match file: no XG header here.");

    /// <summary>The two parts of the runtime's assertion that identify it, whatever build path and line it names.</summary>
    private const string AssertionText = "* Assertion: should not be reached";

    /// <inheritdoc cref="AssertionText"/>
    private const string AssertionSource = "sgen-scan-object.h";

    [Fact]
    public async Task AnUnreadableFileBetweenReadableOnes_IsNamedOnHome_AndTheFileAfterItIsRead()
    {
        await BootInNormalViewAsync();
        await PickWatchingTheRuntimeAsync(
            (SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes()),
            (UnreadableFileName, UnreadableBytes()),
            (SyntheticXgMatch.LongMatchStagedFileName, SyntheticXgMatch.LongMatchBytes()));

        await ExpectBothMatchesReadAsync();
    }

    [Fact]
    public async Task TheSameFilesWithTheUnreadableOneLast_LoadAsBefore()
    {
        await BootInNormalViewAsync();
        await PickWatchingTheRuntimeAsync(
            (SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes()),
            (SyntheticXgMatch.LongMatchStagedFileName, SyntheticXgMatch.LongMatchBytes()),
            (UnreadableFileName, UnreadableBytes()));

        await ExpectBothMatchesReadAsync();
    }

    /// <summary>
    /// Boot Home with maximize off, so the quiz's score panel shows the problem
    /// number each step waits on.
    /// </summary>
    private async Task BootInNormalViewAsync()
    {
        await BootHomeAsync();
        await DisableMaximizeAsync();
        await BootHomeAsync();
    }

    /// <summary>
    /// <see cref="E2eTestBase.PickFilesInOrderAsync"/>, failing with the
    /// runtime's assertion as soon as the console shows it. The listener is
    /// attached before the pick and removed once it is over, either way; the
    /// pick's own waits are its bound.
    /// </summary>
    private async Task PickWatchingTheRuntimeAsync(params (string StagedFileName, byte[] Bytes)[] files)
    {
        var assertion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnConsole(object? sender, IConsoleMessage message)
        {
            if (message.Text.Contains(AssertionText, StringComparison.Ordinal)
                && message.Text.Contains(AssertionSource, StringComparison.Ordinal))
                assertion.TrySetResult(message.Text);
        }

        Page.Console += OnConsole;
        try
        {
            var pick = PickFilesInOrderAsync("read-past-unreadable", files);
            if (await Task.WhenAny(pick, assertion.Task) == assertion.Task)
            {
                // The pick is left waiting on a page that will never answer; it ends in its own
                // timeout or when the context closes, and nothing is left to read its failure.
                _ = pick.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                Assert.Fail($"The WASM runtime died reading the folder (halheinrich/backgammon#523): {assertion.Task.Result}");
            }
            await pick;
        }
        finally
        {
            Page.Console -= OnConsole;
        }
    }

    /// <summary>
    /// The outcome of reading both matches past the unreadable file: Home names
    /// it, the count is the two matches' three problems, and the quiz asks the
    /// long match's after the match's two, located in its own file.
    /// </summary>
    private async Task ExpectBothMatchesReadAsync()
    {
        await ExpectFilterInEffectAsync();

        var record = Page.Locator("#rejectedFilesNotice");
        await Expect(record).ToContainTextAsync("1 of 3 files could not be read, so this selection is incomplete");
        await Expect(record.Locator("li")).ToHaveCountAsync(1);
        await Expect(record.Locator("li")).ToContainTextAsync(UnreadableFileName);
        await Expect(Page.GetByText(ExpectedText.DecisionsMatchYourFilters(3))).ToBeVisibleAsync();

        // As read: the match's cube, then its play, then the long match's cube. Each problem's
        // number is awaited before Skip is pressed, so no press lands on a page still leaving
        // the one before; the number's home, the score panel, shows with maximize off.
        var location = Page.GetByRole(AriaRole.Group, new() { Name = "Problem location" });
        await StartQuizAsync();
        await ExpectProblemNumberAsync(1);
        await Expect(location).ToContainTextAsync(SyntheticXgMatch.StagedFileName);

        await NavButton(ExpectedText.SkipButton).ClickAsync();
        await ExpectProblemNumberAsync(2);
        await Expect(location).ToContainTextAsync(SyntheticXgMatch.StagedFileName);

        await NavButton(ExpectedText.SkipButton).ClickAsync();
        await ExpectProblemNumberAsync(3);
        await Expect(location).ToContainTextAsync(SyntheticXgMatch.LongMatchStagedFileName);
        await Expect(location).ToContainTextAsync(
            $"Game {SyntheticXgMatch.LongMatchCubeGameNumber} · Move {SyntheticXgMatch.LongMatchCubeMoveNumber}");
        await Expect(CubeAnswers).ToHaveCountAsync(4);
    }
}
