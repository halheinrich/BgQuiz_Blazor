using Microsoft.Playwright;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// How a page comes to lack navFold.js's navigation-panel owner
/// (<c>window.bgquizNavFold</c>) while Blazor still starts — the only ways it
/// can: Blazor starts at <c>DOMContentLoaded</c>, which the parser-blocking
/// script holds until it has run or failed, so holding it back only holds the
/// whole page (measured 2026-10-03, halheinrich/backgammon#8, leg 4c).
/// </summary>
public enum PanelOwnerAbsence
{
    /// <summary>The script's request fails at the network.</summary>
    RequestFailed,

    /// <summary>A 200 with an empty body: the shape MapStaticAssets serves for an asset missing on disk.</summary>
    EmptyBody,

    /// <summary>The script loads and throws before its assignment.</summary>
    ScriptThrew,
}

/// <summary>Withholds and restores the panel's owner on a page, by routing navFold.js's requests.</summary>
internal static class PanelOwner
{
    private const string Script = "**/js/navFold*.js";

    /// <summary>The message of the error <see cref="PanelOwnerAbsence.ScriptThrew"/>'s script throws.</summary>
    internal const string Thrown = "navFold.js threw before its assignment";

    /// <summary>
    /// From the page's next load on, every navFold.js request is answered as
    /// <paramref name="absence"/> says, until <see cref="RestoreAsync"/>.
    /// </summary>
    internal static Task WithholdAsync(IPage page, PanelOwnerAbsence absence) =>
        page.RouteAsync(Script, route => absence switch
        {
            PanelOwnerAbsence.RequestFailed => route.AbortAsync("failed"),
            PanelOwnerAbsence.EmptyBody => route.FulfillAsync(new() { Status = 200, Body = "" }),
            PanelOwnerAbsence.ScriptThrew => route.FulfillAsync(new()
            {
                Status = 200, ContentType = "text/javascript", Body = $"throw new Error('{Thrown}');",
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(absence), absence, null),
        });

    /// <summary>From the page's next load on, navFold.js is served as published.</summary>
    internal static Task RestoreAsync(IPage page) => page.UnrouteAsync(Script);

    /// <summary>Whether the page has its owner now — the precondition every absence scenario checks first.</summary>
    internal static Task<bool> PresentAsync(IPage page) =>
        page.EvaluateAsync<bool>("() => window.bgquizNavFold !== undefined");
}
