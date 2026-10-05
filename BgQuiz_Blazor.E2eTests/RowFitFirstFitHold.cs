using System.Text.Json;
using Microsoft.Playwright;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Holds the quiz row's first fit until the scenario under it reaches a
/// checkpoint, then lets it through: the condition under which a scenario's
/// step after Start meets the row present and in its pending presentation
/// (halheinrich/backgammon#333). It holds the page's animation frames
/// (<see cref="AnimationFrames"/>), in which alone the row-fit module
/// measures; the module itself arrives as usual, so the row renders, pending,
/// and stays pending, its first fit queued on a held frame. A module held
/// instead (<see cref="RowFitModuleHold"/>) means no row exists at all, which
/// is a different state.
///
/// <para>
/// <b>Why a checkpoint, and not a duration.</b> A hold released after some
/// time makes the scenario's step and the pending window overlap only if the
/// step comes soon enough, and a slow runner can take longer than any
/// duration chosen. Released at a checkpoint the scenario reports, the
/// overlap holds on any runner.
/// </para>
///
/// <para>
/// <b>The release is the hold's, not the scenario's.</b> The scenario only
/// reports that it has begun its step (<see cref="Checkpoint"/>), which
/// returns at once. On its own continuation the hold then reads the page —
/// the row present or not, pending or not, how many callbacks wait on the
/// held frames, and the rail's box — and only then releases the frames. No
/// fit can run between the checkpoint and that read, the frames being held,
/// so the read shows the state the step began in. The scenario neither waits
/// for the release nor orders it after any later step of its own, so a
/// scenario that waits for the fit cannot deadlock on the hold, and one that
/// does not is not rescued by it.
/// </para>
/// </summary>
internal sealed class RowFitFirstFitHold
{
    /// <summary>The page as the hold reads it at the checkpoint, before it releases anything.</summary>
    private const string ReadAtCheckpoint = """
        () => {
          const row = document.querySelector('.action-row');
          const rail = document.querySelector('.sidebar-toggle-checkbox');
          return JSON.stringify({
            Row: !row ? 'absent' : row.hasAttribute('data-nav-fold-pending') ? 'pending' : 'fitted',
            Queued: window.__frames.queued,
            Rail: !rail ? 'absent' : rail.checked ? 'checked' : 'unchecked',
          });
        }
        """;

    private readonly IPage _page;
    private readonly TaskCompletionSource _checkpoint = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private int _seen;
    private Seen? _checkpointed;
    private Seen? _read;
    private Seen? _released;
    private string? _step;
    private PageState? _state;
    private string? _failure;
    private Task? _wait;
    private bool _waitCompletedWhileHeld;

    private RowFitFirstFitHold(IPage page)
    {
        _page = page;
    }

    /// <summary>One thing the hold saw: its place in the order it saw things in, and its instant on the test's clock.</summary>
    private readonly record struct Seen(int Order, DateTimeOffset At);

    /// <summary>The page at the checkpoint, as <see cref="ReadAtCheckpoint"/> reads it.</summary>
    private sealed record PageState(string Row, int Queued, string Rail);

    /// <summary>
    /// Hold the frames of <paramref name="page"/>'s current document from
    /// now on, until the <see cref="Checkpoint"/>. A full load discards the
    /// hold, so call this after the scenario's last full load, before the
    /// step that loads the quiz page (an in-app navigation keeps the
    /// document).
    /// </summary>
    internal static async Task<RowFitFirstFitHold> HoldAsync(IPage page)
    {
        await page.EvaluateAsync(AnimationFrames.Script);
        await page.EvaluateAsync("() => window.__frames.hold()");
        var hold = new RowFitFirstFitHold(page);
        _ = hold.ReleaseAtTheCheckpointAsync();
        return hold;
    }

    /// <summary>
    /// The scenario has begun <paramref name="step"/>, the step the hold is
    /// for, by issuing <paramref name="wait"/>: from here the hold reads the
    /// page and releases the frames, on its own continuation. Only the first
    /// call counts.
    ///
    /// <para>
    /// The wait itself is watched too. With the frames held no fit can run,
    /// so a wait for the fit cannot complete before the release; one found
    /// complete by then does not wait for the fit, and the hold says so
    /// (<see cref="Unmet"/>). The release follows the checkpoint at once, so
    /// such a wait might also complete only after it, and then pass by
    /// winning the race with the fit: that case is not seen here.
    /// </para>
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
    /// What kept the hold from establishing its condition, or null where it
    /// did: the checkpoint was reached; at it the row was present and pending
    /// with callbacks waiting on the held frames; and the frames were released
    /// after the checkpoint, never before it.
    /// </summary>
    internal string? Unmet()
    {
        lock (_gate)
        {
            if (_checkpointed is not { } checkpointed)
                return "the scenario never reached its checkpoint, so the frames were never released";
            if (_failure is not null)
                return $"the hold could not read or release the page: {_failure}";
            if (_state is not { } state)
                return "the hold has not read the page at the checkpoint yet";
            if (state.Row != "pending")
                return $"at the checkpoint the row was {state.Row}, not present and pending";
            if (state.Queued == 0)
                return "at the checkpoint no callbacks were waiting on the held frames";
            if (_waitCompletedWhileHeld)
                return "the scenario's wait completed while the first fit was still held, so it does not wait for the fit";
            if (_released is not { } released)
                return "the frames were not released after the checkpoint";
            return released.Order > checkpointed.Order ? null : "the frames were released before the checkpoint";
        }
    }

    /// <summary>What the hold saw, each at its instant on the test's clock.</summary>
    internal IReadOnlyList<(DateTimeOffset At, string What)> Events()
    {
        lock (_gate)
        {
            var events = new List<(DateTimeOffset, string)>(3);
            if (_checkpointed is { } checkpointed)
                events.Add((checkpointed.At, $"first-fit hold: the scenario's checkpoint, \"{_step}\"; the hold reads the page, then releases the frames"));
            if (_read is { } read)
            {
                events.Add((read.At, _state is { } s
                    ? $"first-fit hold: at the checkpoint, frames held: row {s.Row} | callbacks waiting on the held frames {s.Queued} | rail {s.Rail}"
                      + (_waitCompletedWhileHeld ? " | the scenario's wait had already completed" : " | the scenario's wait still waiting")
                    : $"first-fit hold: the page could not be read at the checkpoint: {_failure}"));
            }
            if (_released is { } released)
                events.Add((released.At, "first-fit hold: frames released"));
            return events;
        }
    }

    private async Task ReleaseAtTheCheckpointAsync()
    {
        await _checkpoint.Task;
        try
        {
            var json = await _page.EvaluateAsync<string>(ReadAtCheckpoint);
            var state = JsonSerializer.Deserialize<PageState>(json);
            lock (_gate)
            {
                _read = Next();
                _state = state;
            }
            lock (_gate) _waitCompletedWhileHeld = _wait?.IsCompleted == true;
            await _page.EvaluateAsync("() => window.__frames.release()");
            lock (_gate) _released = Next();
        }
        catch (Exception ex) when (ex is PlaywrightException or JsonException)
        {
            lock (_gate)
            {
                _read ??= Next();
                _failure = $"{ex.GetType().Name}: {ex.Message.Split('\n', 2)[0].Trim()}";
            }
        }
    }

    /// <summary>The next thing seen; called under the lock.</summary>
    private Seen Next() => new(++_seen, DateTimeOffset.UtcNow);
}
