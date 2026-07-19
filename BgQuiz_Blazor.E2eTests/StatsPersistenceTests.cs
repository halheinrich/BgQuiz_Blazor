using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The File System Access stats path, end to end: pick → grant → quiz →
/// Continue → <c>bgquiz-stats.json</c> written into the picked folder — plus
/// its degrade rungs (corrupt existing file, denied write permission).
///
/// <para>
/// Playwright cannot drive the native directory picker or its permission
/// prompts, so these scenarios inject a <b>fake <c>window.showDirectoryPicker</c></b>
/// through the <see cref="E2eTestBase.ContextInitScript"/> seam: a scripted
/// directory handle (async <c>values()</c> enumeration, <c>getFileHandle</c>,
/// <c>createWritable</c> capturing writes, scripted permissions) over the real
/// committed fixture's bytes. The faking stops at the browser-API boundary —
/// the app ships no test seams, and everything from the app's own
/// <c>folderAccess.js</c> module inward runs for real. If the module's use of
/// the File System Access surface ever drifts from what the fake mirrors, the
/// pick fails visibly and these scenarios fail loudly — they cannot skip.
/// </para>
///
/// <para>
/// Per-scenario variation (corrupt stats file, denied permission) rides on
/// page-level init scripts registered <i>after</i> the context script: both run
/// at document start on boot, context first, so a page-level override of
/// <c>window.__statsFake</c> wins. The stats filename and wire property names
/// are deliberately hardcoded here — this suite is the consumer-side pin of
/// those contracts (the e2e project references no app assembly by design).
/// </para>
/// </summary>
public sealed class StatsPersistenceTests : E2eTestBase
{
    public StatsPersistenceTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>The on-disk stats filename the app must use — the consumer-side pin.</summary>
    private const string StatsFileName = "bgquiz-stats.json";

    protected override string? ContextInitScript => $$"""
        (() => {
          // Scenario config + captured writes. Defaults: write granted, no
          // existing stats file. Page-level init scripts override per scenario.
          window.__statsFake = { permission: 'granted', statsJson: null, writes: [] };
          const cfg = window.__statsFake;
          const notFound = () => new DOMException('not found', 'NotFoundError');

          const fixtureName = '{{CubeFixture}}';
          const fixtureBytes = Uint8Array.from(atob('{{FixtureBase64(CubeFixture)}}'),
                                               c => c.charCodeAt(0));
          const fixtureEntry = {
            kind: 'file', name: fixtureName,
            getFile: async () => new File([fixtureBytes], fixtureName),
          };

          const statsHandle = {
            kind: 'file', name: '{{StatsFileName}}',
            getFile: async () => {
              if (cfg.statsJson === null) throw notFound();
              return new File([cfg.statsJson], '{{StatsFileName}}');
            },
            createWritable: async () => {
              let buf = '';
              return {
                write: async d => { buf += d; },
                close: async () => { cfg.writes.push(buf); },
              };
            },
          };

          const dir = {
            kind: 'directory', name: 'FakeCorpus',
            queryPermission: async () => cfg.permission,
            requestPermission: async () => cfg.permission,
            values: async function* () { yield fixtureEntry; },
            getFileHandle: async (name, opts) => {
              if (name === '{{StatsFileName}}') {
                if (cfg.statsJson === null && !(opts && opts.create)) throw notFound();
                return statsHandle;
              }
              throw notFound();
            },
          };

          window.showDirectoryPicker = async () => dir;
        })();
        """;

    private static string FixtureBase64(string fixtureFileName) =>
        Convert.ToBase64String(File.ReadAllBytes(FixturePath(fixtureFileName)));

    /// <summary>
    /// Click "Choose folder…" (the fake picker resolves instantly, no native
    /// dialog) and wait for the holder summary — the FS-Access analog of the
    /// base class's fallback-input pick.
    /// </summary>
    private async Task PickFakeFolderAsync()
    {
        await PickFolderButton.ClickAsync();
        await Expect(Page.GetByText("1 problem file")).ToBeVisibleAsync();
    }

    private Task<string[]> CapturedWritesAsync() =>
        Page.EvaluateAsync<string[]>("() => window.__statsFake.writes");

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
        // schemaVersion 1, one decision record, a fully-correct cube submission
        // tallied as TWO decisions (one per half), indented output.
        var writes = await CapturedWritesAsync();
        var payload = Assert.Single(writes);
        Assert.Contains('\n', payload);

        using var doc = JsonDocument.Parse(payload);
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var decisions = doc.RootElement.GetProperty("decisions");
        Assert.Equal(1, decisions.GetArrayLength());
        var tally = decisions[0].GetProperty("tally");
        Assert.Equal(2, tally.GetProperty("submitted").GetInt32());
        Assert.Equal(2, tally.GetProperty("correct").GetInt32());
    }

    [Fact]
    public async Task FsAccessPick_CorruptStatsFile_PoliteNoticeAndNoWrites()
    {
        // An existing stats file the converter must reject: the quiz runs
        // without stats behind a polite notice, and the file is NEVER written.
        await Page.AddInitScriptAsync("window.__statsFake.statsJson = 'not json at all';");

        await BootHomeAsync();
        await PickFakeFolderAsync();
        await Expect(Page.GetByText("stats will be saved")).ToBeVisibleAsync();

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
        await Expect(Page.GetByText("declined write access")).ToBeVisibleAsync();

        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();

        Assert.Empty(await CapturedWritesAsync());
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

        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await Expect(Page.GetByText("Total problems shown: 1")).ToBeVisibleAsync();
    }
}
