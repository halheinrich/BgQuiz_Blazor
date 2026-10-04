using System.Globalization;
using System.Text.Json;
using BgQuiz_Blazor.Client.Components.Pages;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Evidence of where the quiz page stands, written to a test's output so a
/// reader of the log can tell what a scenario saw, and when: the first
/// response to a CI-only failure (AGENTS.md, Reliability: "CI-only failure →
/// observability first"; halheinrich/backgammon#333). The CI step runs with
/// detailed console verbosity, so a passing test's output reaches the log as a
/// failing one's does.
///
/// <para>
/// <b>It observes; it never decides, waits or fails.</b> Nothing here asserts,
/// and nothing waits for the page to become ready: a print that waited for a
/// value to appear would change what it reports. A read of the page that fails
/// is printed as such and never thrown, so the evidence cannot change a
/// scenario's outcome, nor replace the exception a <c>finally</c> prints on
/// the way out of. What a print does cost the scenario is one round trip to
/// the page, the time its next step waits; it does nothing else on the
/// scenario's path (below).
/// </para>
///
/// <para>
/// <b>Two sources, one clock.</b> Each print reads the page once, in one
/// script, so every field of its line describes the same instant. Between
/// prints, a recorder in the page (<see cref="RecordAsync"/>) notes every
/// change of that state as the DOM takes it, every click with the state on
/// screen as it landed, and the row-fit module's fetch, each stamped when it
/// happened. That is how the log shows the first problem the page rendered
/// even where a scenario's own read came before it, with no print having
/// waited for it. The page and the test read the same machine's clock, so
/// every line carries one timeline, to within a millisecond or so across the
/// two: wall-clock time, and milliseconds since the evidence began.
/// </para>
///
/// <para>
/// <b>Written on the way out.</b> A print only keeps what the page returned;
/// <see cref="PrintOnTheWayOutAsync"/>, in a scenario's <c>finally</c>,
/// parses and writes everything, the test's own lines and the page's notes
/// (prefixed "page:") merged in time order. The test's output reaches the log
/// only when the test ends, so nothing is lost by waiting, and the parsing
/// stays off the scenario's path, where it would delay the very read a print
/// stands before.
/// </para>
///
/// <para>
/// <b>What a state line says.</b> The page's path. The row-fit module's state
/// and the row's, kept apart: the module's fetch as the page's resource timing
/// recorded it, and whether the page's two modules have been imported, which
/// the keyboard module's readiness mark (<see cref="QuizKeysMark"/>) says,
/// because the page sets it only after both imports have landed
/// (<c>Quiz.ImportModulesAsync</c>); then the row absent (the page renders no
/// row until the module is in), present with its fit pending, or present and
/// fitted. The problem, by the number the score panel shows and by its fourth
/// answer's reading, the pill's accessible name as the page gives it. The
/// status strip's text. The navigation buttons, each enabled or not: with a
/// problem on screen, ▶ is disabled only while the controller is busy, which
/// is a transition in flight. A field the page does not show is printed as
/// absent; the score panel and the status strip are absent while the maximize
/// setting suppresses them.
/// </para>
/// </summary>
internal sealed class QuizPageEvidence
{
    /// <summary>
    /// The page's state, as one script reads it — the one reader both the
    /// recorder and every print use, so the timeline and the prints describe
    /// the same fields the same way. Takes the readiness mark's name.
    /// </summary>
    private const string StateReader = """
        (mark => {
          const row = document.querySelector('.action-row');
          const pills = row ? [...row.querySelectorAll('.bg-cube-actions input[type=radio]')] : [];
          const text = e => e ? e.textContent.replace(/\s+/g, ' ').trim() : null;
          return {
            path: location.pathname,
            imports: document.documentElement.hasAttribute(mark),
            row: !row ? 'absent' : row.hasAttribute('data-nav-fold-pending') ? 'pending' : 'fitted',
            problem: text(document.querySelector('.problem-position')),
            answers: pills.length,
            fourth: pills.length >= 4 ? pills[3].getAttribute('aria-label') : null,
            status: text(document.querySelector('.status-verdict')),
            nav: row ? [...row.querySelectorAll('.quiz-nav button')]
              .map(b => b.getAttribute('aria-label') + (b.disabled ? ' off' : ' on')).join(', ') : null,
          };
        })
        """;

    /// <summary>
    /// The recorder, installed once per document: a MutationObserver noting
    /// each change of <see cref="StateReader"/>'s state, a capture-phase click
    /// listener noting what was clicked and the state it landed on, and a
    /// PerformanceObserver noting the row-fit module's fetch. It changes
    /// nothing the page does. Returns whether it was installed by this call.
    /// </summary>
    private const string Recorder = $$"""
        mark => {
          if (window.__quizEvidence) return false;
          const read = {{StateReader}};
          const at = () => performance.timeOrigin + performance.now();
          const pending = [], fetches = [];
          let last = null, first = null, lastClick = null;
          const note = () => {
            const state = read(mark);
            const signature = JSON.stringify(state);
            if (signature === last) return;
            last = signature;
            const e = { kind: 'state', at: at(), state };
            pending.push(e);
            if (first === null && state.row !== 'absent') first = e;
          };
          const fetched = entries => {
            for (const r of entries) {
              if (!/\/js\/actionRowFit[^/]*\.js/.test(r.name)) continue;
              const f = { kind: 'fetch', at: performance.timeOrigin + r.responseEnd,
                          start: performance.timeOrigin + r.startTime, status: r.responseStatus };
              fetches.push(f);
              pending.push(f);
            }
          };
          new MutationObserver(note).observe(document.documentElement,
            { subtree: true, childList: true, attributes: true, characterData: true });
          const resources = new PerformanceObserver(list => fetched(list.getEntries()));
          resources.observe({ type: 'resource', buffered: true });
          window.addEventListener('click', ev => {
            const c = ev.target instanceof Element ? ev.target.closest('button, input, a, label') : null;
            const target = c ? (c.getAttribute('aria-label') || c.textContent.replace(/\s+/g, ' ').trim()) : String(ev.target?.nodeName);
            lastClick = { kind: 'click', at: at(), target, state: read(mark) };
            pending.push(lastClick);
          }, true);
          window.__quizEvidence = {
            take() {
              fetched(resources.takeRecords());
              return { recording: true, now: at(), state: read(mark), events: pending.splice(0), first, fetches, lastClick };
            },
          };
          note();
          return true;
        }
        """;

    /// <summary>
    /// One print's read: the recorder's notes since the last print and the
    /// state now, or, in a document without the recorder, the state alone.
    /// Returned as the page serialized it, for <see cref="PrintOnTheWayOutAsync"/>
    /// to parse.
    /// </summary>
    private const string Take = $$"""
        mark => JSON.stringify(window.__quizEvidence
          ? window.__quizEvidence.take()
          : { recording: false, now: performance.timeOrigin + performance.now(), state: ({{StateReader}})(mark),
              events: [], first: null, fetches: [], lastClick: null })
        """;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IPage _page;
    private readonly ITestOutputHelper _output;
    private readonly double _origin = NowMs();

    /// <summary>The test's own lines, each stamped by the test's clock.</summary>
    private readonly List<(double At, string Text)> _lines = [];

    /// <summary>Every print's read, in order: its label, what it adds, and the page's JSON or why there is none.</summary>
    private readonly List<(string Label, Print Print, double At, string? Json, string? Failure)> _takes = [];

    /// <summary>Every <see cref="ReadAsync"/> so far: what it read, and when it was issued and returned.</summary>
    private readonly List<(string What, double Issued, double Returned)> _reads = [];

    /// <summary>Begin the evidence; its clock starts now.</summary>
    internal QuizPageEvidence(IPage page, ITestOutputHelper output)
    {
        _page = page;
        _output = output;
    }

    /// <summary>What a print adds to its state line.</summary>
    private enum Print
    {
        /// <summary>The state line alone.</summary>
        StateOnly,

        /// <summary>The last click, and whether the page still shows the problem it landed on.</summary>
        WithClick,
    }

    /// <summary>
    /// Install the recorder in the page's current document. A full page load
    /// discards it, so call this once the document the quiz will run in is
    /// loaded: after a scenario's last full load, before Start. The quiz's own
    /// navigations stay in that document.
    /// </summary>
    internal async Task RecordAsync()
    {
        try
        {
            var installed = await _page.EvaluateAsync<bool>(Recorder, QuizKeysMark.AttachedAttribute);
            _lines.Add((NowMs(), installed ? "recorder installed in this document" : "recorder already in this document"));
        }
        catch (Exception ex)
        {
            _lines.Add((NowMs(), "recorder not installed: " + Describe(ex)));
        }
    }

    /// <summary>
    /// Read the page's state now, with the recorder's notes since the last
    /// print, for a line labelled <paramref name="label"/>.
    /// </summary>
    internal Task PrintAsync(string label) => TakeAsync(label, Print.StateOnly);

    /// <summary>
    /// Run a scenario's own read and keep the value it returned, with when it
    /// was issued and when it returned. The read is the scenario's, called
    /// once and returned unchanged; nothing else is read for the line. When
    /// in that window the page carried it out is not observable, so the way
    /// out places the page's first row against the window rather than claiming
    /// what the read saw.
    /// </summary>
    internal async Task<T> ReadAsync<T>(string what, Func<Task<T>> read)
    {
        var issued = NowMs();
        var value = await read();
        var returned = NowMs();
        _reads.Add((what, issued, returned));
        _lines.Add((returned, $"{what} = {value} (issued {Since(issued)}, returned {Since(returned)})"));
        return value;
    }

    /// <summary>
    /// Run a scenario's navigation gesture, then read the page's state, for a
    /// line with the gesture's timing and the state its click landed on, so it
    /// says whether the page still shows the problem the navigation is
    /// leaving.
    /// </summary>
    internal async Task NavigateAsync(string what, Func<Task> gesture)
    {
        var issued = NowMs();
        await gesture();
        var returned = NowMs();
        await TakeAsync($"after {what} (issued {Since(issued)}, returned {Since(returned)})", Print.WithClick);
    }

    /// <summary>
    /// The scenario's <c>finally</c>: read the state it ends in, a timeout's
    /// included, then write every line, the first problem the page rendered,
    /// and where that first row falls against each of the scenario's reads.
    /// Never throws.
    /// </summary>
    internal async Task PrintOnTheWayOutAsync()
    {
        await TakeAsync("on the way out", Print.StateOnly);
        try
        {
            WriteAll();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[evidence] could not be written: {Describe(ex)}");
        }
        _lines.Clear();
        _takes.Clear();
    }

    private async Task TakeAsync(string label, Print print)
    {
        try
        {
            var json = await _page.EvaluateAsync<string>(Take, QuizKeysMark.AttachedAttribute);
            _takes.Add((label, print, NowMs(), json, null));
        }
        catch (Exception ex)
        {
            // Evidence never fails the scenario, and in a finally it must not
            // replace the exception on its way out.
            _takes.Add((label, print, NowMs(), null, Describe(ex)));
        }
    }

    private void WriteAll()
    {
        var takes = _takes.Select(t => (t.Label, t.Print, t.At, Parsed: Parse(t.Json, t.Failure))).ToList();

        // The module's fetches as the last read knew them: the recorder keeps
        // every one, and a late-delivered entry is in a later read's list.
        var last = takes.LastOrDefault(t => t.Parsed.Snapshot is not null).Parsed.Snapshot;
        var fetches = last?.Fetches ?? [];

        var lines = new List<(double At, string Text)>(_lines);
        foreach (var t in takes)
        {
            if (t.Parsed.Snapshot is not { } s)
            {
                lines.Add((t.At, $"{t.Label}: the page could not be read: {t.Parsed.Failure}"));
                continue;
            }
            lines.AddRange(s.Events.Select(e => (e.At, "page: " + DescribeEvent(e, fetches))));
            var line = $"{t.Label}: {DescribeState(s.State, s.Now, fetches)}";
            if (!s.Recording) line += " [no recorder in this document]";
            if (t.Print == Print.WithClick) line += " | " + Leaving(s);
            lines.Add((s.Now, line));
        }

        // Stable: lines stamped alike keep the order they were added in.
        foreach (var (at, text) in lines.OrderBy(l => l.At))
            Write(at, text);

        // The recorder keeps the first row for the document's life, so the
        // last read that succeeded knows it.
        if (last is not null)
            WriteFirstProblem(last);
    }

    private static (Snapshot? Snapshot, string? Failure) Parse(string? json, string? failure)
    {
        if (json is null) return (null, failure);
        try
        {
            return (JsonSerializer.Deserialize<Snapshot>(json, Json), "the page returned null");
        }
        catch (JsonException ex)
        {
            return (null, $"{Describe(ex)}; the page returned: {json}");
        }
    }

    /// <summary>
    /// The first problem the page rendered, and where that first row falls
    /// against each of the scenario's reads: before the read was issued, after
    /// it returned, or inside its window, where what the read saw is not
    /// known. The page's clock and the test's agree to within a millisecond
    /// or so, which bounds the placement.
    /// </summary>
    private void WriteFirstProblem(Snapshot snapshot)
    {
        if (snapshot.First is not { State: { } first } shown)
        {
            Write(snapshot.Now, "first problem the page rendered: none (no row in this document since the recorder began)");
            foreach (var read in _reads)
                Write(snapshot.Now, $"{read.What}: no row was rendered before it or since");
            return;
        }

        Write(snapshot.Now, $"first problem the page rendered, at {Since(shown.At)}: problem {first.Problem ?? "absent"}, "
            + $"4th answer {Quoted(first.Fourth)}, row {first.Row}");
        foreach (var read in _reads)
        {
            var placed = shown.At < read.Issued ? "the row was there before the read was issued"
                : shown.At > read.Returned ? "the row came after the read returned, so the read saw no row"
                : "the row came while the read was in flight, so what the read saw is not known";
            Write(snapshot.Now, $"{read.What} (issued {Since(read.Issued)}, returned {Since(read.Returned)}): "
                + $"first row at {Since(shown.At)}: {placed}");
        }
    }

    /// <summary>
    /// What the page's last recorded click landed on, against what the page
    /// shows now: a navigation in flight still shows the problem it is
    /// leaving. The click is named by its target, so the line shows whether
    /// it was the gesture's; it is not matched to the gesture by time, since
    /// the page's clock and the test's agree only to within a millisecond or
    /// so.
    /// </summary>
    private string Leaving(Snapshot snapshot)
    {
        if (snapshot.LastClick is not { State: { } left } click)
            return "no click recorded";

        var now = snapshot.State;
        var verdict = left.Problem is null && left.Fourth is null
            ? "the click landed on no identified problem"
            : left.Problem == now.Problem && left.Fourth == now.Fourth
                ? "still the problem the click left"
                : "a different problem from the one the click left";
        return $"the last click, on {Quoted(click.Target)}, landed at {Since(click.At)} on problem "
            + $"{left.Problem ?? "absent"}, 4th answer {Quoted(left.Fourth)}: now {verdict}";
    }

    private string DescribeEvent(Event e, IReadOnlyList<Event> fetches) => e.Kind switch
    {
        "state" when e.State is { } s => DescribeState(s, e.At, fetches),
        "click" when e.State is { } s => $"click on {Quoted(e.Target)}, landing on: {DescribeState(s, e.At, fetches)}",
        "fetch" => $"actionRowFit.js fetched: requested {Since(e.Start ?? e.At)}, response complete {Since(e.At)}, "
            + $"status {e.Status?.ToString(CultureInfo.InvariantCulture) ?? "?"}",
        _ => $"unrecognized note '{e.Kind}'",
    };

    private string DescribeState(State s, double at, IReadOnlyList<Event> fetches)
    {
        var fetch = fetches.Where(f => f.At <= at).Select(f => (double?)f.At).FirstOrDefault();
        var module = $"module: fetch {(fetch is { } f ? "complete " + Since(f) : "none complete")}, "
            + $"imports {(s.Imports ? "landed" : "not landed")}";
        return $"{s.Path} | {module} | row {s.Row} | problem {s.Problem ?? "absent"} | "
            + $"4th answer {Quoted(s.Fourth)} of {s.Answers} | status {Quoted(s.Status)} | nav {s.Nav ?? "absent"}";
    }

    private static string Quoted(string? text) => text is null ? "absent" : $"\"{text}\"";

    private static string Describe(Exception ex) =>
        $"{ex.GetType().Name}: {ex.Message.Split('\n', 2)[0].Trim()}";

    private void Write(double atMs, string text)
    {
        var wall = DateTimeOffset.UnixEpoch.AddMilliseconds(atMs);
        _output.WriteLine($"[evidence {wall:HH:mm:ss.fff} {Since(atMs)}] {text}");
    }

    private string Since(double atMs) =>
        "+" + (atMs - _origin).ToString("0", CultureInfo.InvariantCulture) + "ms";

    private static double NowMs() => (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalMilliseconds;

    private sealed record State(
        string Path, bool Imports, string Row, string? Problem, int Answers, string? Fourth, string? Status, string? Nav);

    private sealed record Event(string Kind, double At, State? State, string? Target, double? Start, int? Status);

    private sealed record Snapshot(
        bool Recording, double Now, State State, Event[] Events, Event? First, Event[] Fetches, Event? LastClick);
}
