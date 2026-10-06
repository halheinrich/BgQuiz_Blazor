using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The decision's notes without their placement module
/// (<c>wwwroot/js/decisionNotes.js</c>, halheinrich/backgammon#344): a module
/// fetch can fail — halheinrich/backgammon#342's
/// <c>net::ERR_NO_BUFFER_SPACE</c> took out a script exactly that way — and
/// the notes must still be the notes. Their placement is lost with it, so they
/// open where the stylesheet centres them and do not move; they still open and
/// close, and the page shows no error.
/// </summary>
public sealed class NotesPlacementModuleFailureTests : NotesPlacementTestBase
{
    public NotesPlacementModuleFailureTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright)
    {
    }

    [Fact]
    public async Task WithoutItsModule_TheNotesOpenCentred_DoNotMove_AndStillClose()
    {
        int refused = 0;
        await Page.RouteAsync("**/decisionNotes*.js", async route =>
        {
            Interlocked.Increment(ref refused);
            await route.AbortAsync();
        });
        await StartAtTheCubeReviewAsync();

        await NotesButton.ClickAsync();
        await Expect(NotesDialog).ToBeVisibleAsync();
        await ExpectToPassAsync(() =>
        {
            Assert.True(Volatile.Read(ref refused) > 0, "the module was asked for, and refused");
            return Task.CompletedTask;
        });
        await ExpectOverlayCentredAsync("without the placement module");
        await Expect(NotesDialog).Not.ToHaveAttributeAsync("data-placed", string.Empty);

        // The title bar moves nothing, and nothing is written.
        var start = await OverlayBoxAsync();
        await DragTitleBarAsync(-150, -60);
        await ExpectOverlayAtAsync(start.X, start.Y, "after a drag without the module");
        Assert.Null(await StoredPlacementAsync());

        // Esc still closes them, and the control opens them again.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(NotesDialog).ToHaveCountAsync(0);
        await NotesButton.ClickAsync();
        await Expect(NotesDialog).ToBeVisibleAsync();
        await CloseNotesButton.ClickAsync();
        await Expect(NotesDialog).ToHaveCountAsync(0);

        await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }
}
