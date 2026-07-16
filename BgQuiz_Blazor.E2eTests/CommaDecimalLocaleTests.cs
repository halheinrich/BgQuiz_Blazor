using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Guards the board's hit-overlay markup against the comma-decimal-locale bug
/// class. Under a comma-decimal browser culture (Norway's <c>nb-NO</c> — the
/// production report) the WASM runtime once formatted the overlay's fractional
/// SVG geometry with comma decimals (<c>"30,8"</c>), emitting invalid attributes
/// that collapsed the board's LHS points and bar into zero-size non-targets:
/// unclickable.
///
/// <para>
/// This scenario pins the context locale to nb-NO — Playwright sets
/// <c>navigator.languages</c> from it, which is where the browser-wasm runtime
/// derives its culture when globalization is not otherwise pinned — and asserts
/// at two layers:
/// </para>
/// <list type="number">
///   <item><b>Wire</b>: the rendered <c>viewBox</c> and every hit rect's geometry
///   carry no comma, with a nonzero-count guard so the sweep cannot pass
///   vacuously.</item>
///   <item><b>Behavioral</b>: the previously-dead regions actually receive clicks
///   — the bar rect has real, clickable geometry (Playwright's click actionability
///   is the assertion), and the board-point clicks drive a full play the app
///   scores.</item>
/// </list>
///
/// <para>
/// <b>Honest about what this can and cannot catch.</b> It is not a red→green
/// witness for the change it ships beside: two defenses already make nb-NO work —
/// the upstream component fix (BgDiag_Razor <c>a635202</c>, invariant overlay
/// formatting) and this app's <c>InvariantGlobalization</c> pin. Its job is to
/// hold the class at the wire going forward: it would have been red before
/// <c>a635202</c>, and it fails again if the pin is removed and a future markup
/// regression lands. To sensitivity-check the assertions against a genuinely
/// broken build, point the suite at a pre-fix deployed URL via
/// <see cref="PublishedAppFixture.BaseUrlVariable"/>.
/// </para>
///
/// <para>
/// <b>Scope note.</b> The bar is exercised as a click <i>target</i>, not a
/// bar-entry <i>move</i>: neither committed fixture has a checker on the bar, and
/// the <c>.xgp</c> format is binary XG, so an analyzed bar-entry position cannot
/// be fabricated here. A full bar-entry behavioral case (click bar → checker
/// enters → build play → score) is a later, append-only fixture arc if wanted;
/// it is deliberately out of scope for this one, and unnecessary for the bug this
/// guards — the failure mode was "the click never reached the rect", which the
/// actionability check below covers exactly.
/// </para>
/// </summary>
public sealed class CommaDecimalLocaleTests : E2eTestBase
{
    public CommaDecimalLocaleTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>
    /// Pin the browser locale to Norwegian Bokmål — a comma-decimal culture — for
    /// every context this class opens. This is the whole point of the scenario:
    /// it is the culture under which the bug reproduced.
    /// </summary>
    protected override BrowserNewContextOptions ContextOptions => new() { Locale = "nb-NO" };

    [Fact]
    public async Task HitOverlayMarkup_UnderCommaDecimalLocale_IsCultureClean()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CheckerFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        // The overlay only exists once the play-entry board has rendered.
        await Expect(HitOverlaySvg).ToBeVisibleAsync();

        // viewBox — the diagram's coordinate system, itself fractional.
        string? viewBox = await HitOverlaySvg.GetAttributeAsync("viewBox");
        Assert.False(string.IsNullOrEmpty(viewBox), "The hit overlay must carry a viewBox.");
        Assert.DoesNotContain(",", viewBox);

        // Nonzero-count guard: 24 points + the bar are always emitted, so a real
        // overlay has at least 25 rects. A zero/low count would let the geometry
        // sweep below pass vacuously.
        int rectCount = await HitRects.CountAsync();
        Assert.True(rectCount >= 25,
            $"Expected at least 25 hit rects (24 points + bar); found {rectCount}.");

        // Every rect's four geometry attributes, labelled for a legible failure.
        var geometry = await HitRects.EvaluateAllAsync<string[]>(
            "els => els.flatMap((e, i) => ['x', 'y', 'width', 'height']" +
            ".map(a => `rect[${i}].${a}=${e.getAttribute(a)}`))");
        Assert.Equal(rectCount * 4, geometry.Length);
        foreach (string attr in geometry)
        {
            // A missing attribute serialises as "...=null" — itself malformed markup.
            Assert.DoesNotContain("null", attr);
            Assert.DoesNotContain(",", attr);
        }
    }

    [Fact]
    public async Task BoardHitRegions_UnderCommaDecimalLocale_AreClickable()
    {
        await BootHomeAsync();
        await PickFixtureAsync(CheckerFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();

        await Expect(VerdictBand).ToContainTextAsync("Click the board to build your play, then Submit.");

        // --- Bar: the production repro region. -------------------------------
        // Playwright's click actionability IS the behavioral assertion here:
        // ClickAsync requires the element to be visible with a nonzero size and to
        // be the topmost pointer target at the click point — exactly the
        // conditions the "30,8"-collapsed rect violated. A completed click proves
        // the bar has real, hittable geometry under the comma-decimal locale. The
        // explicit bounding-box floor is belt-and-suspenders with a clearer
        // message; it is deliberately loose (nonzero, not pinned to the 30.8
        // viewBox width) so it survives viewport-scale and layout changes.
        var barBox = await BarHitRect.BoundingBoxAsync();
        Assert.NotNull(barBox);
        Assert.True(barBox!.Width > 5,
            $"The bar hit rect collapsed to width {barBox.Width}px — the comma-decimal geometry bug.");
        await BarHitRect.ClickAsync();

        // The opening position has no checker on the bar, so entering from it is
        // an Illegal no-op — the correct app behavior. What matters is that the
        // click dispatched and the app stayed put: no error, no navigation, and no
        // play assembled, so Submit is still gated.
        await ExpectUrlAsync("/quiz");
        await Expect(SubmitButton).ToBeDisabledAsync();

        // --- Board points: full app-reaction through fractional-coordinate rects.
        // The fixture's best play is 24/13 — 24/18 on the 6, then 18/13 on the 5,
        // via the one-click source-advance model (see ClickBoardPointAsync).
        await ClickBoardPointAsync(24);
        await ClickBoardPointAsync(18);

        await Expect(SubmitButton).ToBeEnabledAsync();
        await SubmitButton.ClickAsync();
        await Expect(VerdictBand).ToContainTextAsync("Correct — you found the best play.");
    }
}
