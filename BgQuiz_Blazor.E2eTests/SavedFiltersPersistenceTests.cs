using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The File System Access saved-filters path, end to end: pick → save a named
/// filter → <c>bgquiz-filters.json</c> written into the picked folder → re-pick
/// reloads it through the real read+parse path — plus the corrupt-file degrade
/// rung. Rides the fake-<c>showDirectoryPicker</c> seam of
/// <see cref="FsAccessFakeTestBase"/> (the filters slot the base exposes is
/// stateful, so a write persists for a later re-pick within the same page load).
/// The saved-filters filename is deliberately hardcoded here — this suite is the
/// consumer-side pin of that contract (the e2e project references no app
/// assembly by design).
/// </summary>
public sealed class SavedFiltersPersistenceTests : FsAccessFakeTestBase
{
    public SavedFiltersPersistenceTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private ILocator SaveNameInput => Page.Locator("#saveFilterName");
    private ILocator SaveFilterButton => Page.GetByRole(AriaRole.Button, new() { Name = "Save" });

    [Fact]
    public async Task FsAccessPick_SaveFilter_WritesAndReloadsAcrossRePick()
    {
        await BootHomeAsync();
        await PickFakeFolderAsync();

        // The saved-filters panel is offered for an FS-Access pick, and starts
        // empty (a fresh folder — no bgquiz-filters.json yet).
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
        // bgquiz-filters.json is now populated. The saved filter reloads through
        // the real picked-slot read + NamedFilterCollection parse: the round-trip.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Clear" }).ClickAsync();
        await Expect(Page.GetByText("MyRace")).ToHaveCountAsync(0);

        await PickFakeFolderAsync();
        await Expect(Page.GetByText("MyRace")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task FsAccessPick_CorruptFiltersFile_PoliteNoticeNoPanelNoWrites()
    {
        // An existing bgquiz-filters.json the collection can't parse: the panel
        // degrades to a polite notice naming the file, and the file is NEVER
        // written (the zero-writes preservation guarantee).
        await Page.AddInitScriptAsync("window.__statsFake.filtersJson = 'not a valid filters doc';");

        await BootHomeAsync();
        await PickFakeFolderAsync();

        await Expect(Page.GetByText("couldn't be read")).ToBeVisibleAsync();
        await Expect(Page.GetByText(FiltersFileName)).ToBeVisibleAsync();
        // The panel itself is replaced by the notice — its save-name input is gone.
        await Expect(SaveNameInput).ToHaveCountAsync(0);
        Assert.Empty(await CapturedFilterWritesAsync());
    }
}
