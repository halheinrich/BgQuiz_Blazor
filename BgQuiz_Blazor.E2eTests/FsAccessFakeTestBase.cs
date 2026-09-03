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

    /// <summary>
    /// The name a retired schema-<b>v3</b> stats document must be set aside
    /// under — the consumer-side pin of the clean break's preservation promise
    /// (SPEC-stats-identity.md §3). Every version below the current one retires,
    /// each under a name carrying its own version, so this is the one v3 earns
    /// and not a name every retirement shares; the fake serves this name alone,
    /// which is what makes it a pin rather than a wildcard. v3 because it is
    /// the format every current tester holds (the shipping format through
    /// v1.9.x, retired by halheinrich/backgammon#86's v4 break): the scenario
    /// that crosses the real <c>folderAccess.js</c> stages the file the deploy
    /// will actually meet, and which name each other version earns is the
    /// store suite's pin.
    /// </summary>
    protected const string RetiredStatsFileName = "bgquiz-stats.v3.json";

    /// <summary>
    /// The canonical on-disk saved-filters filename the app must read first and
    /// write — the consumer-side pin of the producer's document identity.
    /// </summary>
    protected const string CanonicalFiltersFileName = "xg-filters.json";

    /// <summary>
    /// The legacy saved-filters filename the app must still <i>read</i> (only
    /// when the canonical file is absent) and never write or delete — the
    /// consumer-side pin of the tester-migration contract.
    /// </summary>
    protected const string LegacyFiltersFileName = "bgquiz-filters.json";

    protected override string? ContextInitScript => $$"""
        (() => {
          // Scenario config + captured writes. Defaults: write granted, no
          // existing stats or saved-filters file. Page-level init scripts
          // override per scenario. The canonical saved-filters slot
          // (filtersJson → '{{CanonicalFiltersFileName}}') is stateful — a
          // write updates it so a later re-pick reads it back (the round-trip
          // the persistence scenario proves), while filtersWrites records every
          // write for assertion. The legacy slot (legacyFiltersJson →
          // '{{LegacyFiltersFileName}}') is read-only by construction: its
          // handle exposes no createWritable, so an app write to the legacy
          // name — a contract violation — fails the gesture loudly instead of
          // passing as a captured write. (Stats deliberately isn't stateful:
          // each quiz re-reads the scenario-configured statsJson.)
          // scanGate: null by default (the enumeration resolves immediately).
          // A test may set it to a promise before picking, which suspends the
          // directory enumeration — the app's own post-prompt work — for as
          // long as it likes. That is the only way to observe the busy
          // affordance in a real browser: unheld, the fake's one-file scan is
          // over in milliseconds and nothing could be asserted about it.
          // permissionError: null by default. A scenario may set it to a
          // DOMException *name*, which makes requestPermission THROW that
          // instead of resolving — the browser refusing to ask, as distinct
          // from a user who answers no (cfg.permission). Only the real
          // refusal name is meant to degrade; see the pair of scenarios in
          // StatsPersistenceTests.
          window.__statsFake = {
            permission: 'granted', permissionError: null, statsJson: null,
            filtersJson: null, legacyFiltersJson: null,
            writes: [], retiredWrites: [], filtersWrites: [], scanGate: null,
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

          // The set-aside slot for a retired stats document. Write-only in
          // practice — nothing in the app ever reads it back — so this captures
          // writes and serves no content: a read attempt is a contract
          // violation and fails the gesture loudly rather than passing.
          const retiredStatsHandle = {
            kind: 'file', name: '{{RetiredStatsFileName}}',
            createWritable: async () => {
              let buf = '';
              return {
                write: async d => { buf += d; },
                close: async () => { cfg.retiredWrites.push(buf); },
              };
            },
          };

          // Canonical saved-filters handle: reads filtersJson (setup-time,
          // picked slot), and a write updates filtersJson (round-trip) as well
          // as recording it.
          const filtersHandle = {
            kind: 'file', name: '{{CanonicalFiltersFileName}}',
            getFile: async () => {
              if (cfg.filtersJson === null) throw notFound();
              return new File([cfg.filtersJson], '{{CanonicalFiltersFileName}}');
            },
            createWritable: async () => {
              let buf = '';
              return {
                write: async d => { buf += d; },
                close: async () => { cfg.filtersJson = buf; cfg.filtersWrites.push(buf); },
              };
            },
          };

          // Legacy saved-filters handle: readable only. The app's contract is
          // read-when-canonical-absent, write-canonical-only — so no
          // createWritable here, and a write attempt fails loudly.
          const legacyFiltersHandle = {
            kind: 'file', name: '{{LegacyFiltersFileName}}',
            getFile: async () => {
              if (cfg.legacyFiltersJson === null) throw notFound();
              return new File([cfg.legacyFiltersJson], '{{LegacyFiltersFileName}}');
            },
          };

          const dir = {
            kind: 'directory', name: 'FakeCorpus',
            queryPermission: async () => cfg.permission,
            requestPermission: async () => {
              // Chromium's own wording for the no-transient-activation refusal,
              // so the scenario reproduces the observed Android arc exactly
              // (halheinrich/backgammon#109) rather than an invented message.
              if (cfg.permissionError !== null) {
                throw new DOMException(
                  'User activation is required to request permissions.',
                  cfg.permissionError);
              }
              return cfg.permission;
            },
            values: async function* () {
              if (cfg.scanGate !== null) await cfg.scanGate;
              yield fixtureEntry;
            },
            getFileHandle: async (name, opts) => {
              if (name === '{{StatsFileName}}') {
                if (cfg.statsJson === null && !(opts && opts.create)) throw notFound();
                return statsHandle;
              }
              if (name === '{{RetiredStatsFileName}}') {
                if (!(opts && opts.create)) throw notFound();
                return retiredStatsHandle;
              }
              if (name === '{{CanonicalFiltersFileName}}') {
                if (cfg.filtersJson === null && !(opts && opts.create)) throw notFound();
                return filtersHandle;
              }
              if (name === '{{LegacyFiltersFileName}}') {
                if (cfg.legacyFiltersJson === null && !(opts && opts.create)) throw notFound();
                return legacyFiltersHandle;
              }
              throw notFound();
            },
          };

          window.showDirectoryPicker = async () => dir;
        })();
        """;

    private static string FixtureBase64(string fixtureFileName) =>
        Convert.ToBase64String(FixtureBytes(fixtureFileName));

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

    /// <summary>
    /// Leave the fake folder holding a real lifetime-stats record, so the
    /// weighted mix is offered at all — the precondition every mix scenario
    /// needs since issue <c>halheinrich/backgammon#87</c>, where a folder with
    /// no stats history offers no mix panel.
    ///
    /// <para>
    /// The record is produced by <b>running a quiz</b> and feeding the app's own
    /// captured write back as the folder's pre-existing file — the suite's
    /// standing trick, so no scenario ever hand-crafts the stats wire format or
    /// the decision-id encoding. It is also the honest shape of the behavior: a
    /// folder acquires a stats history exactly by being quizzed from.
    /// </para>
    ///
    /// <para>
    /// It ends by <b>re-picking</b>, which leaves the setup in the state these
    /// scenarios assume: a folder held, its stats now readable (the pick
    /// re-probes), and no filter in effect for the current pick — the pick
    /// generation bumped past the key the seeding quiz's Apply left its config
    /// under. The
    /// wait is on the Apply-Mix gate hint, which is the one thing true only
    /// after the re-pick lands: it needs the panel mounted (so the probe found
    /// the seeded record) <i>and</i> nothing in effect (so the new pick is the
    /// one being set up). Waiting on the folder summary instead would race — the
    /// outgoing pick's summary reads identically.
    /// </para>
    /// </summary>
    protected async Task SeedStatsHistoryAsync()
    {
        await PickFakeFolderAsync();
        await ApplyFilterAsync();
        await StartQuizAsync();
        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();

        await StageFirstWriteAsTheFoldersStatsFileAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Back to setup" }).ClickAsync();
        await ExpectUrlAsync("/");

        await PickFolderButton.ClickAsync();
        await Expect(Page.GetByText("the mix draws its problems from the filtered pool"))
            .ToBeVisibleAsync();
    }

    /// <summary>
    /// Make the quiz just finished the folder's <i>pre-existing</i> stats file:
    /// the app's own first write-back becomes what the next bind reads. The one
    /// honest way to seed a history — no scenario hand-writes the wire format —
    /// and the reason every mix scenario runs a throwaway quiz first.
    ///
    /// <para>
    /// <b>It waits for that write.</b> Reading <c>writes[0]</c> straight after
    /// the run reaches Done is a one-shot read of something the app does on its
    /// own schedule (<c>halheinrich/backgammon#127</c>): if the write has not
    /// landed yet the seed is silently <c>undefined</c>, and what fails is a mix
    /// assertion several steps later, saying nothing about why. The wait is on
    /// the fake's captured write — the observable consequence of the act being
    /// waited for — in the same spirit as <c>SettingsTests</c>' wait on the
    /// stored settings entry.
    /// </para>
    /// </summary>
    protected async Task StageFirstWriteAsTheFoldersStatsFileAsync()
    {
        await Page.WaitForFunctionAsync("() => window.__statsFake.writes.length > 0");
        await Page.EvaluateAsync(
            "() => { window.__statsFake.statsJson = window.__statsFake.writes[0]; }");
    }

    /// <summary>
    /// Hold the fake directory's enumeration open, so the stretch between the
    /// browser's prompts and the pick summary can be observed. Call before the
    /// pick gesture; release with <see cref="ReleaseScanAsync"/>.
    /// </summary>
    protected Task HoldScanAsync() => Page.EvaluateAsync(
        "() => { window.__statsFake.scanGate = new Promise(r => { window.__releaseScan = r; }); }");

    /// <summary>Let a held enumeration proceed (see <see cref="HoldScanAsync"/>).</summary>
    protected Task ReleaseScanAsync() => Page.EvaluateAsync(
        "() => { window.__statsFake.scanGate = null; window.__releaseScan(); }");

    /// <summary>
    /// Every stats write-back the fake writable captured, in order.
    ///
    /// <para>
    /// A single read, and correct as one at every call site
    /// (<c>halheinrich/backgammon#127</c>): each waits first on the app's own
    /// account of what it did — the Done page's total, the stats notice, the
    /// set-aside report — and the write precedes the state that produces those.
    /// The counting assertions are exact (<c>Single</c>, <c>Equal(2, …)</c>) and
    /// the rest are <c>Empty</c>, so an early read cannot make one of them
    /// quietly true: too few writes fails the first kind, and the second kind is
    /// a negative that a retrying form could not strengthen. The one place that
    /// genuinely raced is <see cref="StageFirstWriteAsTheFoldersStatsFileAsync"/>,
    /// which waits.
    /// </para>
    /// </summary>
    protected Task<string[]> CapturedWritesAsync() =>
        Page.EvaluateAsync<string[]>("() => window.__statsFake.writes");

    /// <summary>Every write the fake made to the set-aside retired-stats name, in order.</summary>
    protected Task<string[]> CapturedRetiredWritesAsync() =>
        Page.EvaluateAsync<string[]>("() => window.__statsFake.retiredWrites");

    /// <summary>Every saved-filters write-back the fake writable captured, in order.</summary>
    protected Task<string[]> CapturedFilterWritesAsync() =>
        Page.EvaluateAsync<string[]>("() => window.__statsFake.filtersWrites");
}
