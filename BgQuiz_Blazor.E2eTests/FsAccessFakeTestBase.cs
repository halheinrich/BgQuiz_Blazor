using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Base for scenarios that exercise the File System Access path: injects a
/// <b>fake <c>window.showDirectoryPicker</c></b> through the
/// <see cref="E2eTestBase.ContextInitScript"/> seam, because Playwright cannot
/// drive the native directory picker or its permission prompts. The fake is a
/// scripted directory handle (async <c>values()</c> enumeration,
/// <c>getFileHandle</c>, <c>createWritable</c> capturing writes, scripted
/// permissions) over the real committed cube fixture's bytes.
///
/// <para>
/// The faking stops at the browser-API boundary — the app ships no test seams,
/// and everything from the app's own <c>folderAccess.js</c> module inward runs
/// for real. If the module's use of the File System Access surface ever drifts
/// from what the fake mirrors, the pick fails visibly and the scenarios fail
/// loudly — they cannot skip.
/// </para>
///
/// <para>
/// Per-scenario variation (corrupt stats file, denied permission, a
/// pre-existing stats document) rides on <c>window.__statsFake</c>: page-level
/// init scripts registered after the context script override its config at
/// boot (context runs first, so the page-level write wins), and a mid-test
/// <c>EvaluateAsync</c> can mutate it between quizzes (the app re-reads the
/// stats file at every Start's re-bind).
/// </para>
/// </summary>
public abstract class FsAccessFakeTestBase : E2eTestBase
{
    protected FsAccessFakeTestBase(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>The on-disk stats filename the app must use — the consumer-side pin.</summary>
    protected const string StatsFileName = "bgquiz-stats.json";

    /// <summary>The on-disk saved-filters filename the app must use — the consumer-side pin.</summary>
    protected const string FiltersFileName = "bgquiz-filters.json";

    protected override string? ContextInitScript => $$"""
        (() => {
          // Scenario config + captured writes. Defaults: write granted, no
          // existing stats or saved-filters file. Page-level init scripts
          // override per scenario. The saved-filters slot is stateful — a write
          // updates filtersJson so a later re-pick reads it back (the round-trip
          // the persistence scenario proves), while filtersWrites records every
          // write for assertion. (Stats deliberately isn't: each quiz re-reads
          // the scenario-configured statsJson.)
          window.__statsFake = {
            permission: 'granted', statsJson: null, filtersJson: null,
            writes: [], filtersWrites: [],
          };
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

          // Saved-filters handle: reads filtersJson (setup-time, picked slot),
          // and a write updates filtersJson (round-trip) as well as recording it.
          const filtersHandle = {
            kind: 'file', name: '{{FiltersFileName}}',
            getFile: async () => {
              if (cfg.filtersJson === null) throw notFound();
              return new File([cfg.filtersJson], '{{FiltersFileName}}');
            },
            createWritable: async () => {
              let buf = '';
              return {
                write: async d => { buf += d; },
                close: async () => { cfg.filtersJson = buf; cfg.filtersWrites.push(buf); },
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
              if (name === '{{FiltersFileName}}') {
                if (cfg.filtersJson === null && !(opts && opts.create)) throw notFound();
                return filtersHandle;
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
    protected async Task PickFakeFolderAsync()
    {
        await PickFolderButton.ClickAsync();
        await Expect(Page.GetByText("1 problem file")).ToBeVisibleAsync();
    }

    /// <summary>Every stats write-back the fake writable captured, in order.</summary>
    protected Task<string[]> CapturedWritesAsync() =>
        Page.EvaluateAsync<string[]>("() => window.__statsFake.writes");

    /// <summary>Every saved-filters write-back the fake writable captured, in order.</summary>
    protected Task<string[]> CapturedFilterWritesAsync() =>
        Page.EvaluateAsync<string[]>("() => window.__statsFake.filtersWrites");
}
