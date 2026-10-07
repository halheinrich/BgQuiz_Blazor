using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// A picked folder holding a file the quiz cannot read
/// (halheinrich/backgammon#368; Hal's ruling on halheinrich/backgammon#367).
/// Until this arc a damaged file vanished silently: the parse skipped it, the
/// count described an incomplete selection as whole, and a folder of nothing
/// but damaged files read as "0 decisions match your filters". Now Home names
/// the file and says the selection is incomplete, the quiz runs over the
/// readable files, and a folder whose every file was refused gets its own
/// box with Start dark for that reason.
///
/// <para>
/// <b>Why these scenarios are owed a browser.</b> The record is a product of
/// the real parse of real bytes through the real pick path — the fallback
/// upload, the buffering, the one-time parse the count warms — and the two
/// boxes are the page's response to what that parse found. The damaged file
/// is a truncation of the suite's synthesized match (<see cref="SyntheticXgMatch"/>),
/// regenerated per run: an <c>.xg</c> is a compressed container, so its first
/// bytes are not a shorter match but a payload the converter refuses, and no
/// corrupt file is committed.
/// </para>
///
/// <para>
/// The sentences are pinned here as independent literals, per this suite's
/// copy-pin split; the wiring — that the facts live with the parse, travel in
/// the summary and survive what the summary does not — is the unit suite's.
/// </para>
/// </summary>
public sealed class RejectedFilesTests : E2eTestBase
{
    public RejectedFilesTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>The damaged file's staged name — what the record must show, extension and all.</summary>
    private const string DamagedFileName = "damaged-match-2026-04-12.xg";

    /// <summary>The synthesized match's first 200 bytes: a payload the converter refuses to read.</summary>
    private static byte[] DamagedBytes() => SyntheticXgMatch.Bytes()[..200];

    [Fact]
    public async Task ADamagedFileBesideAReadableOne_IsNamedOnHome_AndTheQuizRunsOverTheReadableOne()
    {
        await BootHomeAsync();
        await PickSynthesizedFilesAsync("damaged-beside-readable",
            (SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes()),
            (DamagedFileName, DamagedBytes()));
        await ApplyFilterAsync();

        // The record beside the count: the headline, then the file by the name
        // it was picked under. A non-dismissible warning — nothing closes it.
        var record = Page.Locator("#rejectedFilesNotice");
        await Expect(record).ToBeVisibleAsync();
        await Expect(record).ToContainTextAsync("1 of 2 files could not be read, so this selection is incomplete");
        await Expect(record.Locator("li")).ToHaveCountAsync(1);
        await Expect(record.Locator("li")).ToContainTextAsync(DamagedFileName);
        await Expect(record).ToHaveClassAsync(new Regex(@"\balert-warning\b"));
        await Expect(record.GetByRole(AriaRole.Button)).ToHaveCountAsync(0);

        // The count is the readable file's: the synthesized match carries two
        // analysed decisions (its own documented construction), and nothing
        // from the damaged file. Neither zero-count box is on the page.
        await Expect(Page.GetByText(ExpectedText.DecisionsMatchYourFilters(2))).ToBeVisibleAsync();
        await Expect(Page.Locator("#allRejectedNotice")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#noMatchNotice")).ToHaveCountAsync(0);

        // The quiz runs over the readable file, as before: its first problem
        // is the match's cube decision, located in that file — the damaged
        // file contributed none. Read off the location chip rather than the
        // score panel's problem number, which the default maximize mode hides
        // while answering.
        await StartQuizAsync();
        await Expect(CubeAnswers).ToHaveCountAsync(4);
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "Problem location" }))
            .ToContainTextAsync(SyntheticXgMatch.StagedFileName);
    }

    [Fact]
    public async Task AFolderOfOneDamagedFile_ShowsTheAllRejectedBox_AndStartStaysDark()
    {
        await BootHomeAsync();
        await PickSynthesizedFileAsync(DamagedFileName, DamagedBytes());
        await ApplyFilterAsync();

        // Not "0 decisions match your filters": no filter was applied to any
        // decision, because no file could be read. The box names the file.
        var box = Page.Locator("#allRejectedNotice");
        await Expect(box).ToBeVisibleAsync();
        await Expect(box).ToContainTextAsync("None of the selected problem files could be read, so there are no decisions to count");
        await Expect(box.Locator("li")).ToHaveCountAsync(1);
        await Expect(box.Locator("li")).ToContainTextAsync(DamagedFileName);
        await Expect(box).ToHaveClassAsync(new Regex(@"\balert-warning\b"));
        await Expect(box.GetByRole(AriaRole.Button)).ToHaveCountAsync(0); // nothing closes it
        await Expect(Page.Locator("#noMatchNotice")).ToHaveCountAsync(0);
        await Expect(Page.GetByText(ExpectedText.DecisionsMatchYourFilters(0))).ToHaveCountAsync(0);
        await Expect(Page.Locator("#rejectedFilesNotice")).ToHaveCountAsync(0);

        // Start is dark for that reason, and the hint says so.
        await Expect(StartButton).ToBeDisabledAsync();
        await Expect(Page.GetByText("No selected problem file could be read — pick a folder with readable problem files to enable Start."))
            .ToBeVisibleAsync();
    }
}
