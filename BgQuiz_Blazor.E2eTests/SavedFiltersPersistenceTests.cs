using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The File System Access saved-filters path, end to end: pick → save a named
/// filter → <c>xg-filters.json</c> written into the picked folder → re-pick
/// reloads it through the real read+parse path — plus the corrupt-file degrade
/// rung and the legacy-name fallback (an existing <c>bgquiz-filters.json</c>
/// still loads when no canonical file exists, and the next save writes the
/// canonical name only). Rides the fake-<c>showDirectoryPicker</c> seam of
/// <see cref="FsAccessFakeTestBase"/> (the canonical filters slot the base
/// exposes is stateful, so a write persists for a later re-pick within the same
/// page load; the legacy slot is deliberately read-only). The saved-filters
/// filenames are deliberately hardcoded in the base — this suite is the
/// consumer-side pin of that contract (the e2e project references no app
/// assembly by design).
/// </summary>
public sealed class SavedFiltersPersistenceTests : FsAccessFakeTestBase
{
    public SavedFiltersPersistenceTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private ILocator SaveNameInput => Page.Locator("#saveFilterName");

    // The save-as button by its id, not its accessible name: every listed
    // filter's row carries its own "Save" (the per-row overwrite), so a
    // name-based locator is ambiguous the moment the list is non-empty.
    private ILocator SaveFilterButton => Page.Locator("#saveFilterButton");

    [Fact]
    public async Task FsAccessPick_SaveFilter_WritesAndReloadsAcrossRePick()
    {
        await BootHomeAsync();
        await PickFakeFolderAsync();

        // The saved-filters panel is offered for an FS-Access pick, and starts
        // empty (a fresh folder — no saved-filters file under either name yet).
        await Expect(Page.GetByText("No saved filters yet.")).ToBeVisibleAsync();

        // Save the current (default) filter configuration under a name.
        await SaveNameInput.FillAsync("MyRace");
        await SaveFilterButton.ClickAsync();

        // The row appears, and exactly one saved-filters write reached the folder
        // — in the collection's own wire format (schemaVersion 1).
        await Expect(Page.GetByText("MyRace")).ToBeVisibleAsync();
        var payload = Assert.Single(await CapturedFilterWritesAsync());
        using (var doc = JsonDocument.Parse(payload))
        {
            Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        }

        // Clear the pick, then re-pick the same folder — whose persisted
        // xg-filters.json is now populated. The saved filter reloads through
        // the real picked-slot read + NamedFilterCollection parse: the round-trip.
        // Exact: the filter panel's own "Clear filters" button is on screen too,
        // and Playwright's default accessible-name match is a substring.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Clear", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("MyRace")).ToHaveCountAsync(0);

        await PickFakeFolderAsync();
        await Expect(Page.GetByText("MyRace")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task FsAccessPick_CorruptFiltersFile_PoliteNoticeNoPanelNoWrites()
    {
        // An existing xg-filters.json the collection can't parse: the panel
        // degrades to a polite notice naming the file it actually failed on —
        // the canonical one — and the file is NEVER written (the zero-writes
        // preservation guarantee, which also rules out the corrupt-falls-back
        // resurrection: a present-but-unparseable canonical file must not be
        // silently replaced by stale legacy data).
        await Page.AddInitScriptAsync("window.__statsFake.filtersJson = 'not a valid filters doc';");

        await BootHomeAsync();
        await PickFakeFolderAsync();

        await Expect(Page.GetByText("couldn't be read")).ToBeVisibleAsync();
        await Expect(Page.GetByText(CanonicalFiltersFileName)).ToBeVisibleAsync();
        // The panel itself is replaced by the notice — its save-name input is gone.
        await Expect(SaveNameInput).ToHaveCountAsync(0);
        Assert.Empty(await CapturedFilterWritesAsync());
    }

    [Fact]
    public async Task FsAccessPick_LegacyFiltersFile_LoadsAndNextSaveWritesCanonical()
    {
        // The tester-migration contract, real-wire (its only real-wire proof):
        // a folder holding filters under the legacy name — and no canonical
        // file — still loads them on pick, and the next save writes the
        // canonical name while the legacy file is left untouched. The legacy
        // content is produced by the app itself (save → capture → move to the
        // legacy slot), so this scenario never hand-writes the document format.
        await BootHomeAsync();
        await PickFakeFolderAsync();

        await SaveNameInput.FillAsync("Mine");
        await SaveFilterButton.ClickAsync();
        await Expect(Page.GetByText("Mine")).ToBeVisibleAsync();

        // Re-home the document under the legacy name, exactly as a tester's
        // folder from before the rename holds it — once the save it re-homes has
        // actually reached the fake. The chip appearing says the app accepted the
        // save; only the captured document says the write crossed
        // folderAccess.js, and moving a null would set this scenario up to test
        // nothing (halheinrich/backgammon#127).
        await Page.WaitForFunctionAsync("() => window.__statsFake.filtersJson !== null");
        await Page.EvaluateAsync(
            "() => { const c = window.__statsFake; c.legacyFiltersJson = c.filtersJson; c.filtersJson = null; }");

        // End the setup, then re-pick: the canonical read finds nothing, the
        // legacy fallback finds the document, and the filter is offered again.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Clear", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Mine")).ToHaveCountAsync(0);
        await PickFakeFolderAsync();
        await Expect(Page.GetByText("Mine")).ToBeVisibleAsync();

        // Save another filter: the write lands on the canonical slot (the
        // legacy handle can't even be written — the fake gives it no writer),
        // carries both filters, and the legacy content is unchanged.
        await SaveNameInput.FillAsync("Second");
        await SaveFilterButton.ClickAsync();
        await Expect(Page.GetByText("Second")).ToBeVisibleAsync();

        // Both writes captured before anything is read off the fake: the counting
        // assertion below is the one that would go quiet on a slow runner, since
        // a second write still in flight reads as "one write" rather than as a
        // failure to write (halheinrich/backgammon#127). The wait is on "at
        // least", never on the exact number the assertion makes — a wait
        // spelling out the count itself would leave Assert.Equal nothing to
        // catch, and a third write is exactly what it is there to catch.
        await Page.WaitForFunctionAsync("() => window.__statsFake.filtersWrites.length >= 2");

        var state = await Page.EvaluateAsync<System.Text.Json.JsonElement>(
            "() => ({ canonical: window.__statsFake.filtersJson, legacy: window.__statsFake.legacyFiltersJson })");
        var canonical = state.GetProperty("canonical").GetString();
        Assert.NotNull(canonical);
        Assert.Contains("Mine", canonical);
        Assert.Contains("Second", canonical);
        var writes = await CapturedFilterWritesAsync();
        Assert.Equal(2, writes.Length); // the seeding save + the post-fallback save
        var legacy = state.GetProperty("legacy").GetString();
        Assert.NotNull(legacy);
        Assert.Contains("Mine", legacy);
        Assert.DoesNotContain("Second", legacy);
    }
}
