using System.Text.Json;
using Microsoft.Playwright;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// What the quiz page's action row looks like to a finger and a ruler, read in
/// the browser: which of its controls a tap at the centre would miss, how far
/// its trailing cluster runs into the controls before it, and the row-fit
/// budget the page measures (SPEC-quiz-view.md §4, halheinrich/backgammon#264's
/// ruling of 2026-10-03). Every figure is read live from the page, never
/// restated here, so the pins that use it hold under whatever fonts render.
/// </summary>
internal static class ActionRowGeometry
{
    /// <summary>
    /// The script finding the controls of a row element a tap at the centre
    /// would not reach: every button in the row and every cube pill (the
    /// producer's <c>.bg-cube-action</c> label, whose radio it carries). Only
    /// the live row's controls: the row-fit ruler beside it is a hidden copy.
    /// An open "⋯" list's items are left out: they lie over the page by
    /// design, and the list is not the row. The one source for that check,
    /// here and in <c>OneBudgetTests</c>' coverage sampler.
    ///
    /// <para>
    /// <b>Two ways to miss, told apart, neither ignored</b>
    /// (halheinrich/backgammon#341). A centre inside the viewport that hits
    /// something else is <b>covered</b>, by what it hits. A centre outside the
    /// viewport hits nothing at all — <c>elementFromPoint</c> answers null
    /// there — and is <b>off-screen</b>: a tap cannot reach it until it is
    /// scrolled to, which is a different fault from one lying under another
    /// control, and still a fault. The viewport is the one
    /// <c>elementFromPoint</c> tests against, the document's client area,
    /// scroll bars excluded. A disabled button's attribute is lifted for the
    /// test — Chromium does not hit-test a disabled button, so one that is
    /// merely unavailable would otherwise read as covered.
    /// </para>
    ///
    /// <para>
    /// Each miss is <c>{ control, offScreen, text }</c>, the text its
    /// description: "name &lt;- covered by what" or "name &lt;- off-screen:
    /// centre (x, y) outside the w×h viewport".
    /// </para>
    /// </summary>
    internal const string UnreachableControlsOfRow = @"row => {
              const out = [];
              const name = c => c.getAttribute('aria-label')
                || c.querySelector('input')?.getAttribute('aria-label') || c.textContent.trim();
              const vw = document.documentElement.clientWidth, vh = document.documentElement.clientHeight;
              for (const c of row.querySelectorAll('button, .bg-cube-action')) {
                if (c.closest('[role=menu]')) continue;
                const was = c.disabled; if (c instanceof HTMLButtonElement) c.disabled = false;
                const r = c.getBoundingClientRect();
                const x = r.left + r.width / 2, y = r.top + r.height / 2;
                const offScreen = x < 0 || y < 0 || x > vw || y > vh;
                const e = offScreen ? null : document.elementFromPoint(x, y);
                if (c instanceof HTMLButtonElement) c.disabled = was;
                if (offScreen) {
                  out.push({ control: name(c), offScreen: true,
                    text: `${name(c)} <- off-screen: centre (${Math.round(x)}, ${Math.round(y)}) outside the ${vw}x${vh} viewport` });
                } else if (!c.contains(e)) {
                  out.push({ control: name(c), offScreen: false,
                    text: `${name(c)} <- covered by ${e ? (e.getAttribute('aria-label') || e.className || e.tagName) : 'nothing'}` });
                }
              }
              return out;
            }";

    /// <summary>
    /// A control of the row a tap at its centre would not reach, as
    /// <see cref="UnreachableControlsOfRow"/> found it: covered by something
    /// else, or off-screen (<see cref="OffScreen"/>). Its string form is the
    /// script's description of the miss.
    /// </summary>
    internal sealed record UnreachableControl(string Control, bool OffScreen, string Text)
    {
        /// <inheritdoc/>
        public override string ToString() => Text;
    }

    /// <summary>
    /// The live action row's controls a tap at the centre would not reach,
    /// covered or off-screen (<see cref="UnreachableControlsOfRow"/>).
    /// </summary>
    internal static async Task<IReadOnlyList<UnreachableControl>> UnreachableControlsAsync(IPage page) =>
        JsonSerializer.Deserialize<UnreachableControl[]>(
            await page.EvaluateAsync<string>(
                "() => JSON.stringify((" + UnreachableControlsOfRow + ")(document.querySelector('.action-row')))"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    /// <summary>
    /// How many controls <see cref="UnreachableControlsAsync"/> examines — so a
    /// pin that finds none unreachable can say it looked at the controls it meant.
    /// </summary>
    internal static Task<int> RowControlCountAsync(IPage page) =>
        page.EvaluateAsync<int>(@"() => [...document.querySelector('.action-row').querySelectorAll('button, .bg-cube-action')]
            .filter(c => !c.closest('[role=menu]')).length");

    /// <summary>
    /// How far, in CSS pixels, the trailing cluster's first control starts to
    /// the left of where the row's gap after the leading controls ends: zero or
    /// less when the row fits, positive when the cluster runs over the controls
    /// before it.
    /// </summary>
    internal static Task<double> TailOverrunAsync(IPage page) =>
        page.EvaluateAsync<double>(@"() => {
            const row = document.querySelector('.action-row');
            const tail = row.querySelector('.action-row-tail');
            const lead = [...row.children].filter(c => c !== tail);
            const leadRight = Math.max(...lead.map(c => c.getBoundingClientRect().right));
            const gap = parseFloat(getComputedStyle(row).columnGap) || 0;
            return leadRight + gap - tail.firstElementChild.getBoundingClientRect().left;
          }");

    /// <summary>The page's row-fit measurement, read off its ruler and row.</summary>
    internal sealed record Fit(
        double Budget, double FullCubeRow, double Row, double Gap, double PanelWidthIfShown, bool AutoFolded, int RowLines,
        bool TailFolded, bool Pending);

    /// <summary>How the row stands, read off the page with no help from the panel's owner.</summary>
    internal sealed record Presentation(bool AutoFolded, int RowLines, bool TailFolded, bool Pending, double PanelWidth);

    /// <summary>
    /// The script reading a row element's <see cref="Presentation"/>: whether
    /// the panel is folded by itself, the row's lines, whether its tail is
    /// folded behind the "⋯" (TailMenu), whether it is still in its pending
    /// presentation, and the panel's laid-out width. The one source for those
    /// figures in <see cref="PresentationAsync"/> and in <see cref="FitAsync"/>.
    /// </summary>
    private const string PresentationOfRow = @"row => ({
              AutoFolded: document.documentElement.hasAttribute('data-nav-autofold'),
              RowLines: Math.round(row.getBoundingClientRect().height / row.querySelector('.btn-lg').getBoundingClientRect().height),
              TailFolded: row.querySelector('.action-row-tail > .tail-menu') !== null,
              Pending: row.hasAttribute('data-nav-fold-pending'),
              PanelWidth: " + E2eTestBase.NavigationPanelWidthScript + @" })";

    /// <summary>
    /// The row's presentation (<see cref="PresentationOfRow"/>), which needs
    /// nothing of navFold.js: the one reading for a page whose panel owner is
    /// missing, where <see cref="FitAsync"/> fails by design.
    /// </summary>
    internal static async Task<Presentation> PresentationAsync(IPage page) =>
        JsonSerializer.Deserialize<Presentation>(await page.EvaluateAsync<string>(
            "() => JSON.stringify((" + PresentationOfRow + ")(document.querySelector('.action-row')))"))!;

    /// <summary>
    /// The row-fit figures as the module computes them: the budget (the widest
    /// answer row on the ruler, the gap, the tail's floor), the full-label cube
    /// row's need (the ruler's full-form pills with Submit and the four, the
    /// gap and the floor), the row's width, whether the panel is folded by
    /// itself, whether the row shows its tail folded behind the "⋯" (TailMenu)
    /// in the tail's place, and whether the row is still in its pending
    /// presentation, unmeasured. What the showing panel would take beyond
    /// what it takes now is navFold.js's own report (panelWidths), so where
    /// that report's owner is absent this fails with the page's evidence
    /// (<see cref="RequirePanelOwnerAsync"/>).
    /// </summary>
    internal static async Task<Fit> FitAsync(IPage page)
    {
        await RequirePanelOwnerAsync(page);
        return JsonSerializer.Deserialize<Fit>(await page.EvaluateAsync<string>(@"() => {
            const row = document.querySelector('.action-row');
            const ruler = document.querySelector('.action-row-ruler');
            const w = e => e.getBoundingClientRect().width;
            const gap = parseFloat(getComputedStyle(row).columnGap) || 0;
            const tail = w(ruler.querySelector('[data-ruler=""tail""]'));
            const leads = [...ruler.querySelectorAll('[data-ruler=""lead""]')].map(w);
            const full = ruler.querySelector('[data-ruler=""full-cube""]');
            return JSON.stringify({
              Budget: Math.max(...leads) + gap + tail,
              FullCubeRow: w(full) + gap + tail,
              Row: w(row),
              Gap: gap,
              PanelWidthIfShown: (p => p ? p.showing - p.inFlow : 0)(window.bgquizNavFold.panelWidths()),
              ...(" + PresentationOfRow + @")(row) });
          }"))!;
    }

    /// <summary>
    /// Fails with the page's own evidence where the navigation panel's owner —
    /// navFold.js's <c>window.bgquizNavFold</c>, whose report
    /// <see cref="FitAsync"/> reads — is absent, never with the bare
    /// <c>undefined</c> that reading it would throw. The evidence tells the
    /// ways a page can lack it apart: the URL and <c>document.readyState</c>;
    /// navFold.js's script element and its request as the page's resource
    /// timing recorded it (status 0 for a failed request, a 200 with nothing
    /// decoded for an empty body, a full body for a script that loaded and
    /// never reached its assignment); the console errors located in it (a
    /// failed load's <c>net::</c> code) and the page's uncaught errors, from
    /// Playwright's own history of the page; Blazor's state (its global,
    /// whether the WebAssembly runtime has started, its error UI); and the
    /// action row's. A late script cannot be the cause: Blazor starts at
    /// <c>DOMContentLoaded</c>, which the parser-blocking navFold.js holds
    /// until it has run or failed, so no row exists before it (measured
    /// 2026-10-03 with the script held 6 s, cold and cached:
    /// halheinrich/backgammon#8, leg 4c). <see cref="PanelOwnerEvidenceTests"/>
    /// pins the evidence.
    /// </summary>
    private static async Task RequirePanelOwnerAsync(IPage page)
    {
        var evidence = await page.EvaluateAsync<string?>(@"() => {
            if (window.bgquizNavFold) return null;
            const script = [...document.scripts].find(s => /\/js\/navFold[^/]*\.js/.test(s.src));
            const entry = script ? performance.getEntriesByName(script.src, 'resource')[0] : undefined;
            const request = !script ? 'no navFold.js script element'
              : !entry ? `script ${script.src}; no resource timing entry (not requested, or still loading)`
              : `script ${script.src}; request status ${entry.responseStatus}, ${entry.decodedBodySize} bytes decoded, ${Math.round(entry.duration)} ms`;
            const errorUi = document.getElementById('blazor-error-ui');
            const blazor = !window.Blazor ? 'no Blazor global'
              : `global present, WebAssembly runtime ${window.Blazor.runtime ? 'started' : 'not started'}, `
                + `error UI ${errorUi && getComputedStyle(errorUi).display !== 'none' ? 'showing' : 'hidden'}`;
            const row = document.querySelector('.action-row');
            return [
              `page: ${location.href}, readyState ${document.readyState}`,
              `navFold.js: ${request}`,
              `Blazor: ${blazor}`,
              `action row: ${!row ? 'absent' : row.hasAttribute('data-nav-fold-pending') ? 'present, pending' : 'present, fitted'}`,
            ].join('\n  ');
          }");
        if (evidence is null) return;

        var navFoldConsoleErrors = (await page.ConsoleMessagesAsync())
            .Where(m => m.Type == "error" && m.Location.Contains("navFold", StringComparison.Ordinal))
            .Select(m => m.Text)
            .ToList();
        var pageErrors = await page.PageErrorsAsync();
        throw new InvalidOperationException(
            "The navigation panel's owner is missing: navFold.js has not assigned window.bgquizNavFold.\n  "
            + evidence
            + $"\n  console errors from navFold.js: {(navFoldConsoleErrors.Count == 0 ? "none" : string.Join(" | ", navFoldConsoleErrors))}"
            + $"\n  page errors: {(pageErrors.Count == 0 ? "none" : $"{pageErrors.Count}, the last: {string.Join(" | ", pageErrors.TakeLast(5))}")}");
    }

    /// <summary>
    /// The viewport width below which the panel folds by itself, at the current
    /// viewport's layout band: the budget plus the chrome around the row with
    /// the panel showing (the viewport less the row the showing panel leaves).
    /// </summary>
    internal static async Task<double> FoldWidthAsync(IPage page)
    {
        var fit = await FitAsync(page);
        var viewport = await page.EvaluateAsync<double>("() => window.innerWidth");
        var rowWithPanelShowing = fit.Row - fit.PanelWidthIfShown;
        return fit.Budget + (viewport - rowWithPanelShowing);
    }

    /// <summary>
    /// The viewport width below which the tail folds behind its "⋯", at the
    /// current viewport's layout band: the same budget plus the chrome around
    /// the row with the panel folded (the viewport less the row). Read where
    /// the panel is folded by itself — below <see cref="FoldWidthAsync"/>,
    /// which the tail's switch always is, since it needs the same budget from
    /// a row the folded panel has already widened.
    /// </summary>
    internal static async Task<double> TailFoldWidthAsync(IPage page)
    {
        var fit = await FitAsync(page);
        if (!fit.AutoFolded)
            throw new InvalidOperationException("Read the tail's switch where the panel is folded by itself.");
        var viewport = await page.EvaluateAsync<double>("() => window.innerWidth");
        return fit.Budget + (viewport - fit.Row);
    }
}
