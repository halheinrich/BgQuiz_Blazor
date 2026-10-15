namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Diagnostic evidence gathered after something went wrong, one labelled line
/// per item — the boot's failure evidence (<see cref="E2eTestBase.BootHomeAsync"/>,
/// halheinrich/backgammon#383) and the remount report's state at its count
/// check (halheinrich/backgammon#372).
///
/// <para>
/// <b>Best-effort and time-bounded, so it never decides a test's outcome.</b>
/// Evidence is gathered while a failure is already in hand, often from a page
/// that is the reason for it. So <see cref="ReadAsync"/> never throws, and
/// never waits longer than <see cref="ReadBound"/> for one read: a read that
/// fails, or does not answer in time, is recorded as not collected, and says
/// why, in its own line. A failure being reported is therefore never masked,
/// replaced or held up indefinitely by the evidence about it.
/// </para>
/// </summary>
internal sealed class EvidenceSheet
{
    /// <summary>
    /// The longest one read is waited for. Long enough for a live page to
    /// answer a DOM or script read many times over; short enough that a page
    /// which has stopped answering costs a reported failure seconds, not the
    /// whole test's timeout.
    /// </summary>
    public static readonly TimeSpan ReadBound = TimeSpan.FromSeconds(5);

    private readonly List<string> _lines = [];

    /// <summary>The evidence gathered so far, one labelled line (or block) per item.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Record an item already in hand.</summary>
    /// <param name="what">The item's label.</param>
    /// <param name="value">What it was.</param>
    public void Add(string what, string value) => _lines.Add($"{what}: {value}");

    /// <summary>Record a list already in hand, one entry per line beneath its label.</summary>
    /// <param name="what">The list's label.</param>
    /// <param name="entries">Its entries.</param>
    public void AddList(string what, IReadOnlyList<string> entries) =>
        _lines.Add($"{what} ({entries.Count}):"
            + string.Concat(entries.Select(e => Environment.NewLine + "  " + e.ReplaceLineEndings(Environment.NewLine + "  "))));

    /// <summary>
    /// Read one item and record it; if the read throws, or has not answered
    /// within <see cref="ReadBound"/>, record that it was not collected and
    /// why. Never throws. A read abandoned for its time is left to finish on
    /// its own, its fault (if any) observed, so it cannot surface later as an
    /// unobserved task exception.
    /// </summary>
    /// <param name="what">The item's label.</param>
    /// <param name="read">The read.</param>
    /// <returns>A task that completes when the item is recorded either way.</returns>
    public async Task ReadAsync(string what, Func<Task<string>> read)
    {
        Task<string> pending;
        try
        {
            pending = read();
        }
        catch (Exception e)
        {
            _lines.Add(NotCollected(what, $"{e.GetType().Name}: {e.Message}"));
            return;
        }

        try
        {
            _lines.Add($"{what}: {await pending.WaitAsync(ReadBound)}");
        }
        catch (TimeoutException) when (!pending.IsCompleted)
        {
            _ = pending.ContinueWith(
                static abandoned => _ = abandoned.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            _lines.Add(NotCollected(what, $"no answer within {ReadBound.TotalSeconds:0} s"));
        }
        catch (Exception e)
        {
            _lines.Add(NotCollected(what, $"{e.GetType().Name}: {e.Message}"));
        }
    }

    private static string NotCollected(string what, string why) => $"{what}: (not collected — {why})";
}
