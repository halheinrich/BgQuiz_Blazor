using System.Diagnostics;
using Microsoft.Playwright;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// What one page reports while it is recorded — its console messages, its
/// uncaught page errors and its failed requests, each in arrival order and
/// stamped with the milliseconds since recording began. The one recorder the
/// suite's diagnostics share: the boot's failure evidence
/// (<see cref="E2eTestBase.BootHomeAsync"/>, halheinrich/backgammon#383) and the
/// remount report's console record (halheinrich/backgammon#372).
///
/// <para>
/// <b>Attach before the step it covers.</b> The listeners are attached on
/// construction, so a recorder made before a navigation has a record whose
/// start is known and from which nothing is dropped — unlike
/// <see cref="IPage.ConsoleMessagesAsync"/>, which keeps only what the page
/// still holds. <see cref="Dispose"/> detaches them, so a recorder scoped to
/// one step leaves nothing listening after it.
/// </para>
///
/// <para>
/// Playwright raises the events on its own threads, so every record is taken
/// and read under one lock; the lists handed out are copies.
/// </para>
/// </summary>
internal sealed class PageRecorder : IDisposable
{
    private readonly IPage _page;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private readonly List<string> _console = [];
    private readonly List<string> _pageErrors = [];
    private readonly List<string> _failedRequests = [];
    private bool _detached;

    /// <summary>Start recording <paramref name="page"/>.</summary>
    /// <param name="page">The page to record.</param>
    public PageRecorder(IPage page)
    {
        _page = page;
        page.Console += OnConsole;
        page.PageError += OnPageError;
        page.RequestFailed += OnRequestFailed;
    }

    /// <summary>The console messages recorded so far, numbered, each with where it was logged.</summary>
    public IReadOnlyList<string> Console => Snapshot(_console);

    /// <summary>The uncaught page errors recorded so far.</summary>
    public IReadOnlyList<string> PageErrors => Snapshot(_pageErrors);

    /// <summary>The requests recorded so far that failed to complete, with why.</summary>
    public IReadOnlyList<string> FailedRequests => Snapshot(_failedRequests);

    /// <summary>Stop recording: detach every listener. Recording stops once; a second call does nothing.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_detached) return;
            _detached = true;
        }
        _page.Console -= OnConsole;
        _page.PageError -= OnPageError;
        _page.RequestFailed -= OnRequestFailed;
    }

    private void OnConsole(object? sender, IConsoleMessage message) =>
        Record(_console, (number, ms) => $"#{number} +{ms} ms [{message.Type}] {message.Text}"
            + Environment.NewLine + $"    at {message.Location}");

    private void OnPageError(object? sender, string error) =>
        Record(_pageErrors, (number, ms) => $"#{number} +{ms} ms {error}");

    private void OnRequestFailed(object? sender, IRequest request) =>
        Record(_failedRequests, (number, ms) => $"#{number} +{ms} ms {request.Method} {request.Url} — {request.Failure}");

    /// <summary>Add one record, numbered within its list and stamped with when it arrived.</summary>
    private void Record(List<string> into, Func<int, long, string> format)
    {
        var received = _clock.ElapsedMilliseconds;
        lock (_gate)
        {
            into.Add(format(into.Count + 1, received));
        }
    }

    private IReadOnlyList<string> Snapshot(List<string> records)
    {
        lock (_gate) return [.. records];
    }
}
