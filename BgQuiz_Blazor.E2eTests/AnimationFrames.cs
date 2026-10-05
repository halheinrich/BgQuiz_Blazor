namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// A hold on the page's animation frames, on the browser's side (the app
/// ships no seam for it). The row-fit module measures only in a frame's
/// animation callbacks (<c>actionRowFit.js</c>), so holding
/// <c>requestAnimationFrame</c> holds every measurement and everything it
/// applies — the panel's fold and the report that ends the row's pending
/// presentation — not merely the report. Released, the held callbacks run in
/// the next real frame.
///
/// <para>
/// <see cref="Script"/> installs <c>window.__frames</c> in the page's main
/// world: <c>hold()</c>, <c>release()</c>, <c>queued</c> (how many callbacks
/// wait on the hold) and <c>settle()</c> (a promise for two real frames,
/// which a hold does not delay). Playwright's own waits run in an isolated
/// world with its own frames, so they keep running while the page's are
/// held. Installed once per document, however often it is run: a second run
/// finds <c>window.__frames</c> and changes nothing. One source for every
/// suite that holds frames (<c>OneBudgetTests</c>,
/// <see cref="RowFitFirstFitHold"/>).
/// </para>
/// </summary>
internal static class AnimationFrames
{
    /// <summary>The script installing <c>window.__frames</c>; usable as an init script or evaluated in a loaded document.</summary>
    internal const string Script = """
        (() => {
          if (window.__frames) return;
          // The frame hold: requestAnimationFrame queues while held, and the
          // queue runs in the next real frame on release.
          const raf = window.requestAnimationFrame.bind(window);
          const caf = window.cancelAnimationFrame.bind(window);
          const queue = new Map();
          let held = false, next = -1;
          window.requestAnimationFrame = cb => {
            if (!held) return raf(cb);
            const id = next--;
            queue.set(id, cb);
            return id;
          };
          window.cancelAnimationFrame = id => { if (!queue.delete(id)) caf(id); };
          window.__frames = {
            hold() { held = true; },
            release() {
              held = false;
              const callbacks = [...queue.values()];
              queue.clear();
              for (const cb of callbacks) raf(cb);
            },
            get queued() { return queue.size; },
            settle: () => new Promise(r => raf(() => raf(r))),
          };
        })();
        """;
}
