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
    /// The row's controls a tap at the centre would not reach, each as
    /// "name &lt;- what is hit instead": every button in the row and every cube
    /// pill (the producer's <c>.bg-cube-action</c> label, whose radio it
    /// carries). A disabled button's attribute is lifted for the test —
    /// Chromium does not hit-test a disabled button, so one that is merely
    /// unavailable would otherwise read as covered. Only the live row's
    /// controls: the row-fit ruler beside it is a hidden copy. An open "⋯"
    /// list's items are left out: they lie over the page by design, and the
    /// list is not the row.
    /// </summary>
    internal static async Task<string[]> CoveredControlsAsync(IPage page) =>
        await page.EvaluateAsync<string[]>(@"() => {
            const row = document.querySelector('.action-row');
            const out = [];
            const name = c => c.getAttribute('aria-label')
              || c.querySelector('input')?.getAttribute('aria-label') || c.textContent.trim();
            for (const c of row.querySelectorAll('button, .bg-cube-action')) {
              if (c.closest('[role=menu]')) continue;
              const was = c.disabled; if (c instanceof HTMLButtonElement) c.disabled = false;
              const r = c.getBoundingClientRect();
              const e = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
              if (c instanceof HTMLButtonElement) c.disabled = was;
              if (!c.contains(e)) {
                out.push(name(c) + ' <- ' + (e ? (e.getAttribute('aria-label') || e.className || e.tagName) : 'nothing'));
              }
            }
            return out;
          }");

    /// <summary>
    /// How many controls <see cref="CoveredControlsAsync"/> examines — so a
    /// pin that finds none covered can say it looked at the controls it meant.
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

    /// <summary>
    /// The row-fit figures as the module computes them: the budget (the widest
    /// answer row on the ruler, the gap, the tail's floor), the full-label cube
    /// row's need (the ruler's full-form pills with Submit and the four, the
    /// gap and the floor), the row's width, whether the panel is folded by
    /// itself, whether the row shows its tail folded behind the "⋯" (TailMenu)
    /// in the tail's place, and whether the row is still in its pending
    /// presentation, unmeasured. What the showing panel would take beyond
    /// what it takes now is navFold.js's own report (panelWidths).
    /// </summary>
    internal static async Task<Fit> FitAsync(IPage page) =>
        JsonSerializer.Deserialize<Fit>(await page.EvaluateAsync<string>(@"() => {
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
              AutoFolded: document.documentElement.hasAttribute('data-nav-autofold'),
              RowLines: Math.round(row.getBoundingClientRect().height / row.querySelector('.btn-lg').getBoundingClientRect().height),
              TailFolded: row.querySelector('.action-row-tail > .tail-menu') !== null,
              Pending: row.hasAttribute('data-nav-fold-pending') });
          }"))!;

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
