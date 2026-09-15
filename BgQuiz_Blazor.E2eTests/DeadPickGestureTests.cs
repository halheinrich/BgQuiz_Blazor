using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The dead-capability half of the pair: a browser with <b>no</b>
/// <c>showDirectoryPicker</c>, where the hidden <c>webkitdirectory</c> input is
/// the only pick mechanism left and whether it is honored cannot be
/// feature-detected. That is the configuration a WebView-wrapping browser
/// presented on a real tablet, where Choose folder raised no chooser of any kind
/// (<c>halheinrich/backgammon#108</c>), and it is the one Home owes an account
/// of before the gesture — no code path can report it afterwards.
///
/// <para>
/// The capability is removed at the same <see cref="E2eTestBase.ContextInitScript"/>
/// seam <see cref="FsAccessFakeTestBase"/> uses to <i>install</i> it, so the two
/// halves of this pair differ in exactly one fact about the browser and nothing
/// else. Removing rather than emulating is the honest fake here: BgQuiz's probe
/// is <c>typeof window.showDirectoryPicker === 'function'</c>, so absence is the
/// whole of what the dead-gesture browser has in common with this context.
/// </para>
/// </summary>
public sealed class DeadPickGestureTests : E2eTestBase
{
    public DeadPickGestureTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    // Shadowing the prototype's operation is what the app's own probe sees;
    // `delete window.showDirectoryPicker` would not, since the operation lives
    // on Window.prototype rather than as an own property of the global.
    protected override string? ContextInitScript =>
        "window.showDirectoryPicker = undefined;";

    [Fact]
    public async Task Home_WithoutADirectoryPicker_AccountsForAGestureThatDoesNothing()
    {
        await BootHomeAsync();

        // The account is up from load, before any gesture — the only moment it
        // can reach this reader.
        await Expect(Page.GetByText(ExpectedText.SilentPickGestureAccount)).ToBeVisibleAsync();

        // …and its FS-Access-only sibling is not, which is what proves the
        // capability was actually removed rather than the notice being ungated.
        await Expect(Page.GetByText(ExpectedText.BrowserWillAskAboutTheFolder))
            .ToBeHiddenAsync();

        // The gesture itself survives the account: clicking only opens the
        // hidden input's picker (Playwright leaves it unanswered, which is the
        // dead browser's shape), and the reader who sees nothing happen must
        // still find the explanation standing.
        await PickFolderButton.ClickAsync();
        await Expect(Page.GetByText(ExpectedText.SilentPickGestureAccount)).ToBeVisibleAsync();
    }
}

/// <summary>
/// The live-capability half: <see cref="FsAccessFakeTestBase"/>'s fake
/// <c>showDirectoryPicker</c> is installed, so the pick is served by a mechanism
/// that cannot die silently — it opens a chooser, aborts, or throws, and every
/// one of those has a code path that reports it. The account would be noise, and
/// its absence here is what makes the presence above mean something.
/// </summary>
public sealed class LivePickGestureTests : FsAccessFakeTestBase
{
    public LivePickGestureTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    [Fact]
    public async Task Home_WithADirectoryPicker_OmitsTheSilentGestureAccount()
    {
        await BootHomeAsync();

        // Wait on the sibling the same probe gates rather than asserting the
        // absence straight away: both land on the render pass after the probe
        // resolves, so a bare DoesNotContain would pass before the probe had
        // even reported — vacuously, for the wrong reason.
        await Expect(Page.GetByText(ExpectedText.BrowserWillAskAboutTheFolder))
            .ToBeVisibleAsync();
        await Expect(Page.GetByText(ExpectedText.SilentPickGestureAccount)).ToBeHiddenAsync();

        // And it stays absent across the gesture, where a real folder lands.
        await PickFakeFolderAsync();
        await Expect(Page.GetByText(ExpectedText.SilentPickGestureAccount)).ToBeHiddenAsync();
    }
}
