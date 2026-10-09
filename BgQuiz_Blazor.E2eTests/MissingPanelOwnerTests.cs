using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// A page whose navigation-panel owner is missing — navFold.js failed to load,
/// came back empty, or threw (<see cref="PanelOwnerAbsence"/>) — keeps the
/// quiz row in its pending presentation, and the "Keep the navigation panel
/// folded" setting neither crashes nor loses the user's choice there
/// (halheinrich/backgammon#8, leg 4c, Hal's approval of 2026-10-03). A missing
/// owner is not the phone layout: there the owner is present and answers
/// null, and the row's fit completes.
/// </summary>
/// <remarks>
/// Each scenario first checks the owner really is missing (or, for the phone,
/// present), and waits for the row-fit module to have acted — its fit
/// completed, or its word on the console that the owner is missing — before
/// reading the row, so no pin passes by reading too early.
/// </remarks>
public sealed class MissingPanelOwnerTests : E2eTestBase
{
    public MissingPanelOwnerTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>How the quiz page is entered.</summary>
    public enum Entry
    {
        /// <summary>The quiz's start, on a cold load: every module fetched.</summary>
        FirstLoad,

        /// <summary>Back from Show stats, which re-creates the page from the modules already loaded.</summary>
        ReturnFromStats,
    }

    /// <summary>What actionRowFit.js says, once per row it observes, when the owner is missing.</summary>
    private const string RowFitOwnerMissing = "actionRowFit.js: the navigation panel's owner";

    /// <summary>
    /// What QuizSettings logs when the applier cannot be told the choice (here,
    /// for want of the owner, so its first call fails) — saved or not, the
    /// warning opens the same way.
    /// </summary>
    private const string SettingsApplierCallFailed =
        "The navigation panel's applier could not be told the choice (bgquizNavFold.prefer failed)";

    private ILocator Captions => Page.Locator(".action-row .bg-cube-actions label");

    private ILocator Rail => Page.GetByRole(
        AriaRole.Checkbox, new() { Name = ExpectedText.HideNavigationPanelCheckbox });

    private ILocator KeepFolded => Page.GetByRole(
        AriaRole.Checkbox, new() { Name = ExpectedText.KeepNavigationPanelFoldedSetting });

    private async Task StartTheMatchAsync()
    {
        await PickSynthesizedFileAsync(SyntheticXgMatch.StagedFileName, SyntheticXgMatch.Bytes());
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();
        await Expect(Captions).ToHaveCountAsync(4);
    }

    private async Task<int> ConsoleCountAsync(string type, string text) =>
        (await Page.ConsoleMessagesAsync()).Count(m => m.Type == type && m.Text.Contains(text, StringComparison.Ordinal));

    /// <summary>The board's laid-out box, rounded to a tenth of a pixel.</summary>
    private async Task<(double X, double Y, double W, double H)> BoardAsync()
    {
        var b = await LaidOutBoxAsync(Page.Locator(".board-container .bg-diagram"), "the board");
        return (Math.Round(b.X, 1), Math.Round(b.Y, 1), Math.Round(b.Width, 1), Math.Round(b.Height, 1));
    }

    /// <summary>
    /// Wait until the row-fit module has acted on the current row: its fit
    /// completed (the row no longer pending), or it said, once for each of the
    /// <paramref name="rowsObserved"/> rows, that the owner is missing. Either
    /// way, so a page that conflates the two fails the assertions after this,
    /// not a wait.
    /// </summary>
    private Task RowFitHasActedAsync(int rowsObserved) =>
        ExpectToPassAsync(async () => Assert.True(
            !(await ActionRowGeometry.PresentationAsync(Page)).Pending
                || await ConsoleCountAsync("warning", RowFitOwnerMissing) == rowsObserved,
            "the row-fit module has not acted on the row yet"));

    /// <summary>Show stats, from the "⋯" where the tail is folded behind it, else from the tail itself.</summary>
    private async Task ShowStatsAsync()
    {
        var more = Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.MoreButton, Exact = true });
        if (await more.CountAsync() > 0)
        {
            await more.ClickAsync();
            await Page.GetByRole(AriaRole.Menuitem, new() { Name = ExpectedText.ShowStatsButton, Exact = true }).ClickAsync();
        }
        else
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ShowStatsButton, Exact = true }).ClickAsync();
        }
    }

    [Theory]
    [InlineData(PanelOwnerAbsence.RequestFailed, Entry.FirstLoad)]
    [InlineData(PanelOwnerAbsence.RequestFailed, Entry.ReturnFromStats)]
    [InlineData(PanelOwnerAbsence.EmptyBody, Entry.FirstLoad)]
    [InlineData(PanelOwnerAbsence.EmptyBody, Entry.ReturnFromStats)]
    [InlineData(PanelOwnerAbsence.ScriptThrew, Entry.FirstLoad)]
    [InlineData(PanelOwnerAbsence.ScriptThrew, Entry.ReturnFromStats)]
    public async Task WithoutItsOwner_TheRowStaysPending_CoveringNothing_OnOneLine_WithTheBoardAtItsSize(
        PanelOwnerAbsence absence, Entry entry)
    {
        // §2's floor corner, where the measured presentation is the pending
        // one, so the board the page gives this window with its owner is the
        // board it must keep without it: measured first, as the yardstick.
        await Page.SetViewportSizeAsync(641, 768);
        await BootHomeAsync();
        await StartTheMatchAsync();
        await ExpectToPassAsync(async () => Assert.False((await ActionRowGeometry.PresentationAsync(Page)).Pending));
        var board = await BoardAsync();

        // A fresh load without the owner.
        await PanelOwner.WithholdAsync(Page, absence);
        await BootHomeAsync();
        Assert.False(await PanelOwner.PresentAsync(Page), "the owner is missing");
        await StartTheMatchAsync();
        var rowsObserved = 1;
        if (entry == Entry.ReturnFromStats)
        {
            await RowFitHasActedAsync(rowsObserved);
            await ShowStatsAsync();
            await ExpectUrlAsync(AppRoute.Stats);
            await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToQuizButton }).ClickAsync();
            await ExpectUrlAsync(AppRoute.Quiz);
            await Expect(Captions).ToHaveCountAsync(4);
            rowsObserved = 2;
        }

        await RowFitHasActedAsync(rowsObserved);

        var row = await ActionRowGeometry.PresentationAsync(Page);
        Assert.True(row.Pending, "no fit completes without the owner");
        Assert.True(row.TailFolded, "the tail stays behind its \"⋯\"");
        await Expect(Captions).ToHaveTextAsync(["ND", "D/T", "D/P", "TG"]);   // the pills stay short
        Assert.Equal(0, row.PanelWidth);                                        // the pending style hides the panel
        Assert.False(row.AutoFolded, "nothing asked the missing owner to fold");
        Assert.Equal(1, row.RowLines);
        Assert.True(await ActionRowGeometry.RowControlCountAsync(Page) > 0);
        Assert.Empty(await ActionRowGeometry.UnreachableControlsAsync(Page));
        Assert.Equal(board, await BoardAsync());
        Assert.Equal(rowsObserved, await ConsoleCountAsync("warning", RowFitOwnerMissing));   // said once per row
    }

    [Fact]
    public async Task APhoneLayout_HasItsOwner_AnsweringNull_AndItsRowIsFitted_NeverFolded()
    {
        // The widest phone layout, one pixel below §2's floor: no side panel,
        // so the owner, present, answers null, and that is a completed fit:
        // the tail takes a line of its own (halheinrich/backgammon#236), never
        // the "⋯", and nothing folds.
        await Page.SetViewportSizeAsync(640, 768);
        await BootHomeAsync();
        await StartTheMatchAsync();
        Assert.True(await PanelOwner.PresentAsync(Page), "the owner is present");
        Assert.True(await Page.EvaluateAsync<bool>("() => window.bgquizNavFold.panelWidths() === null"), "it answers null");

        await ExpectToPassAsync(async () => Assert.False((await ActionRowGeometry.PresentationAsync(Page)).Pending));

        var row = await ActionRowGeometry.PresentationAsync(Page);
        Assert.False(row.TailFolded);
        Assert.False(row.AutoFolded);
        Assert.Equal(0, await ConsoleCountAsync("warning", RowFitOwnerMissing));
    }

    [Theory]
    [InlineData(PanelOwnerAbsence.RequestFailed)]
    [InlineData(PanelOwnerAbsence.EmptyBody)]
    [InlineData(PanelOwnerAbsence.ScriptThrew)]
    public async Task TheKeepFoldedSetting_WithoutTheOwner_NeitherCrashesNorLosesTheChoice(PanelOwnerAbsence absence)
    {
        await Page.SetViewportSizeAsync(1280, 800);
        var errorUi = Page.Locator("#blazor-error-ui");

        // On, with the owner: stored.
        await Page.GotoAsync(BaseUrl + "/settings");
        await KeepFolded.CheckAsync();
        await Expect(KeepFolded).ToBeCheckedAsync();

        // Off, without it. The unfold cannot be made: the page says so on the
        // console and goes on; no error banner, no unhandled exception. The
        // panel on this page stays as the load left it — showing, since
        // nothing applied the stored fold either.
        await PanelOwner.WithholdAsync(Page, absence);
        await Page.GotoAsync(BaseUrl + "/settings");
        Assert.False(await PanelOwner.PresentAsync(Page), "the owner is missing");
        await Expect(KeepFolded).ToBeCheckedAsync();
        Assert.True(await NavigationPanelWidthAsync() > 0);
        await KeepFolded.UncheckAsync();
        await ExpectToPassAsync(async () => Assert.True(
            await errorUi.IsVisibleAsync() || await ConsoleCountAsync("warning", SettingsApplierCallFailed) == 1,
            "the setting's action has not finished"));
        Assert.False(await errorUi.IsVisibleAsync(), "no error banner");
        Assert.Equal(0, await ConsoleCountAsync("error", "Unhandled exception"));
        await Expect(KeepFolded).Not.ToBeCheckedAsync();

        // Kept: the next load with the owner starts unfolded, the setting off.
        await PanelOwner.RestoreAsync(Page);
        await Page.ReloadAsync();
        await Expect(KeepFolded).Not.ToBeCheckedAsync();
        await Expect(Rail).Not.ToBeCheckedAsync();
        Assert.True(await NavigationPanelWidthAsync() > 0);

        // On, without it: nothing to fail, and the choice is kept the same way.
        await PanelOwner.WithholdAsync(Page, absence);
        await Page.ReloadAsync();
        Assert.False(await PanelOwner.PresentAsync(Page), "the owner is missing");
        await KeepFolded.CheckAsync();
        await Expect(KeepFolded).ToBeCheckedAsync();
        Assert.False(await errorUi.IsVisibleAsync(), "no error banner");

        // ...and takes effect at the next load with the owner: folded from the start.
        await PanelOwner.RestoreAsync(Page);
        await Page.ReloadAsync();
        await Expect(KeepFolded).ToBeCheckedAsync();
        await Expect(Rail).ToBeCheckedAsync();
        Assert.Equal(0, await NavigationPanelWidthAsync());
        Assert.Equal(0, await ConsoleCountAsync("error", "Unhandled exception"));
    }
}
