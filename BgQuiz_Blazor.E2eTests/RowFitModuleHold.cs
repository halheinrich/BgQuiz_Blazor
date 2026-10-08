using Microsoft.Playwright;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Holds the quiz page's row-fit module (<c>actionRowFit.js</c>) back until
/// the scenario under it reaches a checkpoint, then lets it through: the
/// condition under which a scenario's step after Start meets a page with no
/// action row yet (halheinrich/backgammon#333). The quiz page renders its
/// board and row only once both of its modules are imported
/// (<c>Quiz.RowFitReady</c>), so while the module is held there is no row on
/// the page at all, and the first row can appear only after the release.
///
/// <para>
/// <b>Why a checkpoint, and not a duration.</b> A hold released some time
/// after the module was requested makes the scenario's step and the row's
/// arrival overlap only if the step comes soon enough, and a slow runner can
/// take longer than any duration chosen. Released at a checkpoint the scenario
/// reports, the overlap holds on any runner: the step has begun before the
/// module goes through, so before any row can exist.
/// </para>
///
/// <para>
/// <b>The release is the hold's, not the scenario's.</b> The scenario only
/// reports that it has begun its step (<see cref="Checkpoint"/>), which
/// returns at once; the module goes through on the hold's own continuation,
/// whatever the scenario is doing by then. The scenario neither waits for the
/// release nor orders it after any later step of its own, so a scenario that
/// waits for the row cannot deadlock on the hold, and one that does not is
/// not rescued by it.
/// </para>
///
/// <para>
/// <b>The scenario's wait is watched too</b> (halheinrich/backgammon#350,
/// the check <see cref="RowFitFirstFitHold"/> has). Until the module goes
/// through no row can exist, so no problem can land; a wait for one found
/// complete at the release is not a wait for it, and <see cref="Unmet"/> says
/// so. The release follows the checkpoint at once, so a step that does not
/// wait can also complete only after the release and pass by winning the race
/// with the row: that case is not seen here. Issued well before the page asks
/// for the module, such a step loses that race, which is why the proof under
/// this hold starts its quiz on the URL alone (<c>QuizFlowTests</c>).
/// </para>
///
/// <para>
/// A Playwright route turns the page's HTTP cache off while it is active, so
/// every request for the module, cold or not, comes through the hold. The
/// module is served unchanged: the hold delays the request and alters
/// nothing, which the page's integrity check on the module requires.
/// </para>
/// </summary>
internal sealed class RowFitModuleHold
{
    /// <summary>The module's URL, fingerprinted or not.</summary>
    private const string Module = "**/js/actionRowFit*.js";

    private readonly TaskCompletionSource _checkpoint = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private int _seen;
    private Seen? _requested;
    private Seen? _checkpointed;
    private Seen? _released;
    private string? _step;
    private Task? _wait;
    private bool _waitCompletedWhileHeld;

    private RowFitModuleHold()
    {
    }

    /// <summary>
    /// One thing the hold saw: its place in the order the hold saw things in,
    /// which is what <see cref="Unmet"/> judges by, and its instant on the
    /// test's clock, for the timeline a scenario writes (<see cref="HoldEvents"/>).
    /// </summary>
    private readonly record struct Seen(int Order, DateTimeOffset At);

    /// <summary>
    /// Begin holding the module on <paramref name="page"/>: every request for
    /// it from now on waits for the <see cref="Checkpoint"/>. Install it before
    /// the step that loads the quiz page.
    /// </summary>
    internal static async Task<RowFitModuleHold> InstallAsync(IPage page)
    {
        var hold = new RowFitModuleHold();
        await page.RouteAsync(Module, hold.HoldAsync);
        return hold;
    }

    /// <summary>
    /// The scenario has begun <paramref name="step"/>, the step the hold is
    /// for, by issuing <paramref name="wait"/>: from here the module goes
    /// through, on the hold's own continuation, and the wait is looked at as
    /// it goes. Only the first call counts.
    /// </summary>
    internal void Checkpoint(string step, Task wait)
    {
        lock (_gate)
        {
            if (_checkpointed is not null) return;
            _checkpointed = Next();
            _step = step;
            _wait = wait;
        }
        _checkpoint.TrySetResult();
    }

    /// <summary>
    /// What kept the hold from doing its work, or null where it did: the
    /// module was requested and held, the scenario reached its checkpoint,
    /// its wait had not completed when the module was let through, and the
    /// module was let through after the checkpoint, never before it.
    /// </summary>
    internal string? Unmet()
    {
        lock (_gate)
        {
            if (_requested is null)
                return "the hold never saw a request for actionRowFit.js, so nothing was held";
            if (_checkpointed is not { } checkpointed)
                return "the scenario never reached its checkpoint, so the module was never let through";
            if (_released is not { } released)
                return "the module was not let through after the checkpoint";
            if (_waitCompletedWhileHeld)
                return "the scenario's wait had completed before the module was let through, so it does not wait for the problem to land";
            return released.Order > checkpointed.Order
                ? null
                : "the module was let through before the scenario's checkpoint, so nothing was held across it";
        }
    }

    /// <summary>
    /// What the hold saw, in the order it saw it, each at its instant on the
    /// test's clock. The page can ask for the module before the scenario's
    /// checkpoint or after it; either way the module goes through only after.
    /// </summary>
    internal IReadOnlyList<(DateTimeOffset At, string What)> Events()
    {
        lock (_gate)
        {
            var events = new List<(Seen Seen, string What)>(3);
            if (_requested is { } requested)
                events.Add((requested, "row-fit module hold: the page requested actionRowFit.js; the request is held"));
            if (_checkpointed is { } checkpointed)
                events.Add((checkpointed, $"row-fit module hold: the scenario's checkpoint, \"{_step}\"; the hold lets the module through from here"));
            if (_released is { } released)
            {
                events.Add((released, "row-fit module hold: actionRowFit.js let through"
                    + (_waitCompletedWhileHeld ? "; the scenario's wait had already completed" : "; the scenario's wait still waiting")));
            }
            return [.. events.OrderBy(e => e.Seen.Order).Select(e => (e.Seen.At, e.What))];
        }
    }

    private async Task HoldAsync(IRoute route)
    {
        lock (_gate) _requested ??= Next();
        await _checkpoint.Task;
        lock (_gate)
        {
            if (_released is null)
            {
                _waitCompletedWhileHeld = _wait?.IsCompleted == true;
                _released = Next();
            }
        }
        await route.ContinueAsync();
    }

    /// <summary>The next thing seen; called under the lock.</summary>
    private Seen Next() => new(++_seen, DateTimeOffset.UtcNow);
}
