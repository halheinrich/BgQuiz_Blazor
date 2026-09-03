using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The File System Access stats path, end to end: pick → grant → quiz →
/// Continue → <c>bgquiz-stats.json</c> written into the picked folder — plus
/// its degrade rungs (corrupt existing file, denied write permission). Rides
/// the fake-<c>showDirectoryPicker</c> seam of
/// <see cref="FsAccessFakeTestBase"/> (shared with the mix-weighting suite).
/// The stats filename and wire property names are deliberately hardcoded in
/// these assertions — this suite is the consumer-side pin of those contracts
/// (the e2e project references no app assembly by design).
/// </summary>
public sealed class StatsPersistenceTests : FsAccessFakeTestBase
{
    public StatsPersistenceTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task FsAccessPick_AnswerAndContinue_WritesStatsJson()
    {
        await BootHomeAsync();
        await PickFakeFolderAsync();

        // Pick-time status: the stats-enabled notice, naming the file.
        await Expect(Page.GetByText("stats will be saved")).ToBeVisibleAsync();
        await Expect(Page.GetByText(StatsFileName)).ToBeVisibleAsync();

        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();

        // Exactly one fold (one answered problem), one write-back — captured by
        // the fake writable. Pin the wire contract from the consumer side:
        // schemaVersion 4, one problem record keyed by content and nested
        // under its answer-kind token ("cubePair" — the v4 shape,
        // halheinrich/backgammon#86), a fully-correct cube submission tallied
        // as TWO decisions (one per half), indented.
        var writes = await CapturedWritesAsync();
        var payload = Assert.Single(writes);
        Assert.Contains('\n', payload);

        using var doc = JsonDocument.Parse(payload);
        Assert.Equal(4, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var problems = doc.RootElement.GetProperty("problems");
        var record = Assert.Single(problems.EnumerateObject());
        var kind = Assert.Single(record.Value.EnumerateObject());
        Assert.Equal("cubePair", kind.Name);
        var tally = kind.Value.GetProperty("tally");
        Assert.Equal(2, tally.GetProperty("submitted").GetInt32());
        Assert.Equal(2, tally.GetProperty("correct").GetInt32());

        // The key is content, not provenance: a fixture filename appearing in it
        // would mean the #95 fragmentation had survived the re-key. A cube key
        // additionally carries no dice field (the kind discriminant).
        Assert.DoesNotContain(CubeFixture, record.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, record.Name.Count(c => c == '/'));
    }

    [Fact]
    public async Task FsAccessPick_RetiredV3StatsFile_SetsItAsideAndStartsFresh()
    {
        // The clean break's one act of recognition, through the real
        // folderAccess.js (SPEC-stats-identity.md §3): a genuine retired file
        // must not surface as the polite "couldn't be read" degrade, which
        // would strand an existing tester with stats silently dead. Its bytes
        // go to the sidecar name its own version earns, a fresh current-version
        // document takes the standard name, and the run says so and records
        // normally. Staged as v3 — the format every current tester holds, which
        // halheinrich/backgammon#86's v4 break retires — so the scenario meets
        // the file the deploy will. Which set-aside name each retired version
        // gets is pinned by the store suite, over all of them; what this
        // scenario adds is that the whole act crosses the real folderAccess.js.
        await Page.AddInitScriptAsync(
            $"window.__statsFake.statsJson = {JsonSerializer.Serialize(V3StatsJson)};");

        await BootHomeAsync();
        await PickFakeFolderAsync();

        // Forecast first, on Home, before the pick has been acted on
        // (halheinrich/backgammon#146): the same event in future tense, naming
        // the same file, while the old stats are still there — which is what
        // makes "nothing is deleted" worth saying. The act has not happened
        // yet, so the report's past tense must not be on this page.
        await Expect(Page.GetByText("will be set aside as").First).ToBeVisibleAsync();
        await Expect(Page.GetByText(RetiredStatsFileName).First).ToBeVisibleAsync();
        await Expect(Page.GetByText("nothing is deleted").First).ToBeVisibleAsync();
        await Expect(Page.GetByText("has been set aside as")).ToBeHiddenAsync();

        await ApplyFilterAsync();
        await StartQuizAsync();

        await Expect(Page.GetByText("has been set aside as").First).ToBeVisibleAsync();
        await Expect(Page.GetByText(RetiredStatsFileName).First).ToBeVisibleAsync();
        await Expect(Page.GetByText("couldn't be read")).ToBeHiddenAsync();

        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();

        // The old file preserved verbatim, unparsed. Line endings normalized on
        // both sides: this literal carries the source file's, and what crossed
        // the browser is whatever the fake's File gave back — the claim is about
        // the content, not about which newline a round trip settled on.
        Assert.Equal(
            V3StatsJson.ReplaceLineEndings("\n"),
            Assert.Single(await CapturedRetiredWritesAsync()).ReplaceLineEndings("\n"));

        // …and the standard name written twice: the fresh empty v4 seed at
        // bind, then the answered problem folded into it in v4's shape.
        var writes = await CapturedWritesAsync();
        Assert.Equal(2, writes.Length);
        using var seeded = JsonDocument.Parse(writes[0]);
        Assert.Equal(4, seeded.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Empty(seeded.RootElement.GetProperty("problems").EnumerateObject());
        using var folded = JsonDocument.Parse(writes[1]);
        var record = Assert.Single(folded.RootElement.GetProperty("problems").EnumerateObject());
        Assert.Equal("cubePair", Assert.Single(record.Value.EnumerateObject()).Name);
    }

    /// <summary>
    /// A genuine retired (schema v3) stats document — the shipping format
    /// through v1.9.x: a content-keyed <c>problems</c> map whose values are
    /// bare tally-plus-date objects, no answer-kind token. Hand-written because
    /// nothing can produce one any more — the format has no writer left — and
    /// hardcoded here for the same reason the wire assertions above are: this
    /// suite is the consumer-side pin, and references no app assembly.
    /// </summary>
    private const string V3StatsJson = """
        {
          "schemaVersion": 3,
          "problems": {
            "0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/0a0j/1c/31": {
              "tally": { "submitted": 3, "correct": 2, "totalEquityLoss": 0.125 },
              "lastQuizzed": "2026-08-30T09:15:00+00:00"
            }
          }
        }
        """;

    [Fact]
    public async Task FsAccessPick_CorruptStatsFile_PoliteNoticeAndNoWrites()
    {
        // An existing stats file the converter must reject: the quiz runs
        // without stats behind a polite notice, and the file is NEVER written.
        await Page.AddInitScriptAsync("window.__statsFake.statsJson = 'not json at all';");

        await BootHomeAsync();
        await PickFakeFolderAsync();
        await Expect(Page.GetByText("stats will be saved")).ToBeVisibleAsync();
        // The other half of the retirement forecast's pin
        // (halheinrich/backgammon#146): an unreadable file is not a retired one.
        // It will never be set aside, so nothing on Home may promise that.
        await Expect(Page.GetByText("will be set aside as")).ToBeHiddenAsync();

        await ApplyFilterAsync();
        await StartQuizAsync();

        // The load happens at the Start-time bind, so the notice lives on the
        // quiz page (and Done), not on Home at pick time.
        await Expect(Page.GetByText("couldn't be read")).ToBeVisibleAsync();

        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await Expect(Page.GetByText("couldn't be read")).ToBeVisibleAsync();

        Assert.Empty(await CapturedWritesAsync());
    }

    [Fact]
    public async Task FsAccessPick_WritePermissionDenied_DeniedNoticeQuizRuns()
    {
        // The user declines write access: pick succeeds read-only, the denied
        // notice shows at pick time, the quiz completes, nothing is written.
        await Page.AddInitScriptAsync("window.__statsFake.permission = 'denied';");

        await BootHomeAsync();
        await PickFakeFolderAsync();
        // Target the stats notice specifically: under PermissionDenied the
        // saved-filters panel opens with the same "write access wasn't granted"
        // premise (load-only), so that phrase matches two elements. The
        // consequence clause is unique to the stats notice — finding (AA)'s
        // wording for what the missing grant costs, not "stats won't be saved".
        // Pinned to the distinctive fragment only: the shorter the discriminating
        // substring, the less a copy polish breaks it spuriously.
        await Expect(Page.GetByText("which problems give you difficulty")).ToBeVisibleAsync();

        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();

        Assert.Empty(await CapturedWritesAsync());
    }

    [Fact]
    public async Task FsAccessPick_WriteRequestRefusedWithoutActivation_DegradesLikeADecline()
    {
        // The Chrome-for-Android arc (halheinrich/backgammon#109). The picker
        // resolves normally and then requestPermission THROWS SecurityError,
        // because no transient user activation survives the pick there. Before
        // the fix that throw escaped folderAccess.js as a JSException and
        // destroyed the WHOLE pick — the folder whose files the user had
        // already granted read access to was lost behind "Could not read the
        // folder", which is why the device could not use the app at all.
        //
        // It must land exactly where a user's own "no" lands: folder held,
        // files readable, quiz runnable end to end, nothing written. The
        // notice is pinned to the same distinctive consequence clause the
        // declined-write scenario above uses — the two causes share a rung and
        // must be indistinguishable on the surface.
        await Page.AddInitScriptAsync("window.__statsFake.permissionError = 'SecurityError';");

        await BootHomeAsync();
        // Waits on the holder summary, so a pick that died fails right here.
        await PickFakeFolderAsync();
        await Expect(Page.GetByText("Could not read the folder")).ToBeHiddenAsync();
        await Expect(Page.GetByText("which problems give you difficulty")).ToBeVisibleAsync();

        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();

        Assert.Empty(await CapturedWritesAsync());
    }

    [Fact]
    public async Task FsAccessPick_WriteRequestFailsUnexpectedly_StillFailsThePickLoudly()
    {
        // The other side of that catch, and the reason it is written narrowly.
        // Only the refuse-to-ask SecurityError degrades; any other failure out
        // of the write request is a genuine browser fault and must still reach
        // the pick-error banner. A blanket catch would pass the scenario above
        // and silently swallow this one into a read-only pick.
        await Page.AddInitScriptAsync("window.__statsFake.permissionError = 'InvalidStateError';");

        await BootHomeAsync();
        await PickFolderButton.ClickAsync();

        await Expect(Page.GetByText("Could not read the folder")).ToBeVisibleAsync();
    }
}

/// <summary>
/// The fallback mechanism's no-stats rung, made explicit: a
/// <c>webkitdirectory</c> pick (no <c>showDirectoryPicker</c> fake installed —
/// this class deliberately injects nothing, so the app's capability probe finds
/// the real headless-Chromium API surface and the scenario drives the hidden
/// input directly) surfaces the "can't save stats" notice and still runs the
/// quiz to Done. The seven migrated flow scenarios exercise this same pick
/// path; this one pins the notice.
/// </summary>
public sealed class FallbackPickNoticeTests : E2eTestBase
{
    public FallbackPickNoticeTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task FallbackPick_DirectoryInput_NoStatsNoticeAndQuizRuns()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);

        await Expect(Page.GetByText("can't save quiz stats")).ToBeVisibleAsync();
        // Finding (AB) re-scoped this pin. The two-step permission guidance is
        // now gated on browser *capability* (an init-time showDirectoryPicker
        // probe) and on no folder being held — not on which mechanism served the
        // pick. This scenario injects no fake, so the real Chromium underneath
        // may well report the capability and show the note on load; what it can
        // still pin is the other end of the window — a held folder hides it.
        // FS-Access-onlyness is pinned by the unit test that reports no
        // capability (PageTests.Home_NoFsAccessBrowser_ShowsNoPermissionGuidance),
        // which is the only place that condition is reachable.
        await Expect(Page.GetByText("Your browser will ask about the selected folder")).ToBeHiddenAsync();

        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();
    }
}
