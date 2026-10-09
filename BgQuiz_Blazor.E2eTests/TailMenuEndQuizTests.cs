using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// End quiz in the tail's "⋯" list ends the quiz as its button does, busy
/// behaviour included (SPEC-quiz-view.md §4, halheinrich/backgammon#264's
/// widened fourth: "choosing one does what that control does"). The busy
/// window that matters is a Submit's write to the lifetime record, so the
/// scenario runs on the FS-Access fake, whose stats writes it holds open.
/// </summary>
public sealed class TailMenuEndQuizTests : FsAccessFakeTestBase
{
    public TailMenuEndQuizTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    private ILocator More =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.MoreButton, Exact = true });

    private ILocator Item(string name) =>
        Page.GetByRole(AriaRole.Menuitem, new() { Name = name, Exact = true });

    private ILocator EndQuizButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.EndQuizButton, Exact = true });

    private Task SettleAsync() =>
        Page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");

    [Fact]
    public async Task EndQuiz_IsUnavailableWhileTheQuizIsBusy_AsItsButtonIs_AndThenEndsTheQuizAsItsButtonDoes()
    {
        await Page.SetViewportSizeAsync(1280, 768);
        await BootHomeAsync();
        await PickFakeFolderAsync();
        await ExpectFilterInEffectAsync();
        await StartQuizAsync();

        // A Submit whose write is held: the review is up and the quiz is busy.
        // The button, at a width that shows it, is disabled.
        await HoldWritesAsync();
        await AnswerCubeNoDoubleAsync();
        await Expect(Page.Locator(".board-page.app-busy")).ToHaveCountAsync(1);
        await Expect(EndQuizButton).ToBeDisabledAsync();

        // The same window at 641 px, where the tail is behind the "⋯": the
        // item is disabled with it, and Show stats, which only navigates, is not.
        await Page.SetViewportSizeAsync(641, 768);
        await SettleAsync();
        await More.ClickAsync();
        await Expect(Item(ExpectedText.EndQuizButton)).ToBeDisabledAsync();
        await Expect(Item(ExpectedText.ShowStatsButton)).ToBeEnabledAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.GetByRole(AriaRole.Menu)).ToHaveCountAsync(0);

        // The write lands: no longer busy, and the item is available.
        await ReleaseWritesAsync();
        await Expect(Page.Locator(".board-page.app-busy")).ToHaveCountAsync(0);
        await More.ClickAsync();
        await Expect(Item(ExpectedText.EndQuizButton)).ToBeEnabledAsync();

        // Chosen, it ends the quiz as the button does: one click, no
        // confirmation, the summary, the answer kept.
        await Item(ExpectedText.EndQuizButton).ClickAsync();
        await ExpectUrlAsync(AppRoute.Done);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = ExpectedText.DoneHeading })).ToBeVisibleAsync();
        var body = Page.Locator("body");
        await Expect(body).ToContainTextAsync(ExpectedText.Submitted(1));
        await Expect(body).ToContainTextAsync(ExpectedText.Skipped(0));
    }
}
