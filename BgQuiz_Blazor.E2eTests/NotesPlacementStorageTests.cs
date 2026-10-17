using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The notes overlay's placement when browser storage cannot be relied on
/// (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's placement is a remembered
/// preference": "storage that is missing, malformed, out of range or
/// unavailable reads as unset, a failed write keeps the preference in memory
/// for the session, and neither ever stops the notes opening, closing or
/// moving"; issue <c>halheinrich/backgammon#344</c>). The faults are staged in
/// the page before the app boots, on the placement's own key only, so the rest
/// of the app's storage behaves as it always does.
/// </summary>
public sealed class NotesPlacementStorageTests : NotesPlacementTestBase
{
    public NotesPlacementStorageTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright, output)
    {
    }

    [Fact]
    public async Task StorageThatThrows_OpensCentred_AndTheNotesStillMove_ForTheSession()
    {
        // Every read, write and removal of the placement's key throws, as a
        // browser that refuses the site storage does.
        await Page.AddInitScriptAsync($$"""
            (() => {
              for (const name of ['getItem', 'setItem', 'removeItem']) {
                const original = Storage.prototype[name];
                Storage.prototype[name] = function (key, ...rest) {
                  if (key === '{{PlacementKey}}') throw new DOMException('Storage is unavailable.', 'SecurityError');
                  return original.call(this, key, ...rest);
                };
              }
            })();
            """);
        await StartAtTheCubeReviewAsync();

        // Unreadable reads as unset.
        await OpenNotesAsync();
        await ExpectOverlayCentredAsync("with storage that throws");

        // The move still happens, and holds for the session though it cannot
        // be written: closed and reopened, and across a navigation.
        await OpenMoveControlAsync();
        await StepButton(ExpectedText.MoveRightButton).ClickAsync();
        await StepButton(ExpectedText.MoveUpButton).ClickAsync();
        await ExpectAtPositionAsync(0.625, 0.375, "moved with storage that throws");

        await CloseNotesButton.ClickAsync();
        await Expect(NotesDialog).ToHaveCountAsync(0);
        await OpenNotesAsync();
        await ExpectAtPositionAsync(0.625, 0.375, "reopened");

        await CloseNotesButton.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ShowStatsButton, Exact = true }).ClickAsync();
        await ExpectUrlAsync(AppRoute.Stats);
        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToQuizButton }).ClickAsync();
        await ExpectUrlAsync(AppRoute.Quiz);
        await OpenNotesAsync();
        await ExpectAtPositionAsync(0.625, 0.375, "after Show stats and back");

        // Reset works the same way, and none of it was ever an error.
        await OpenMoveControlAsync();
        await ResetButton.ClickAsync();
        await ExpectOverlayCentredAsync("after Reset");
        await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    /// <summary>
    /// What the browser holds under the key before the app boots: nothing at
    /// all, text that is not JSON, JSON that is not the placement's object, and
    /// positions that are out of range or not numbers.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("[0.25, 0.75]")]
    [InlineData("""{"horizontal":1.5,"vertical":0.25}""")]
    [InlineData("""{"horizontal":0.25,"vertical":-0.1}""")]
    [InlineData("""{"horizontal":"left","vertical":0.5}""")]
    public async Task UnusableStoredValue_OpensCentred_AndAMoveReplacesIt(string? stored)
    {
        if (stored is not null)
        {
            // JSON-encoded, so the stored text reaches the page as one string literal, quotes and all.
            await Page.AddInitScriptAsync(
                $"localStorage.setItem({JsonSerializer.Serialize(PlacementKey)}, {JsonSerializer.Serialize(stored)});");
        }
        await StartAtTheCubeReviewAsync();
        Assert.Equal(stored, await StoredPlacementAsync());

        await OpenNotesAsync();
        await ExpectOverlayCentredAsync($"with {stored ?? "nothing"} stored");

        await OpenMoveControlAsync();
        await StepButton(ExpectedText.MoveDownButton).ClickAsync();
        await ExpectAtPositionAsync(0.5, 0.625, "after a step down");
        var written = await StoredPlacementAsync();
        Assert.NotNull(written);
        Assert.NotEqual(stored, written);

        // What was written is what the next opening reads.
        await CloseNotesButton.ClickAsync();
        await OpenNotesAsync();
        await ExpectAtPositionAsync(0.5, 0.625, "reopened");
    }
}
