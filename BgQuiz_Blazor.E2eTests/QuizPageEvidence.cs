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
/// failing one's does. Every line claims only what its instrument measured.
///
/// <para>
/// <b>It observes; it never decides, waits or fails.</b> Nothing here asserts,
/// and nothing waits for the page to become ready: a print that waited for a
/// value to appear would change what it reports. A failure of the evidence
/// itself — a page that cannot be read, a reply that cannot be parsed — is
/// printed as such and never thrown, so it cannot fail the test, nor replace
/// the exception a <c>finally</c> prints on the way out of.
/// </para>
///
/// <para>
/// <b>It is not free, and so it can move the race it watches.</b> The
/// recorder changes no state the page holds or renders, but its callbacks run
/// on the page's main thread, after every DOM change and on every click, and
/// every print awaits a round trip to the page before the scenario's next
/// step. Both take time the page and the scenario would otherwise spend
/// elsewhere, so the evidence can shift the scheduling of the very race it
/// observes. Parsing and writing are kept off the scenario's path (below) to
/// keep that cost to the one round trip.
/// </para>
///
/// <para>
/// <b>Two sources, two clocks.</b> Each print reads the page once, in one
/// script, so every field of its line describes the same instant. Between
/// prints, a recorder in the page (<see cref="RecordAsync"/>) notes each
/// change of that state when its MutationObserver callback sees it, each
/// click when the click's event reaches its capture listener, and the row-fit
/// module's fetch as resource timing reports it. The times it notes are those
/// observations, not the instants the DOM changed: a node is in the DOM no
/// later than the callback that sees it. That is how the log shows the first
/// problem the page rendered even where a scenario's own read came before it,
/// with no print having waited for it. Page-side times come from the page's
/// clock (<c>performance.timeOrigin + performance.now()</c>), test-side times
/// from the test's (<see cref="DateTimeOffset.UtcNow"/>), and each line is
/// stamped with its clock. The run measures how far apart the two are: each
/// print's page instant lies between the test's issuing it and its returning,
/// which brackets the offset, and every print narrows the bracket. A page
/// event is placed before or after a test-side window only where it is
/// separated by more than that bracket; inside it, the placement is printed
/// as too close to call, with the raw times.
/// </para>
///
/// <para>
/// <b>Written on the way out.</b> A print only keeps what the page returned;
/// <see cref="PrintOnTheWayOutAsync"/>, in a scenario's <c>finally</c>,
/// parses and writes everything, merged into one order by the measured
/// offset. The test's output reaches the log only when the test ends, so
/// nothing is lost by waiting.
/// </para>
///
/// <para>
/// <b>What a state line says.</b> The page's path. The row-fit module's fetch
/// and the row, kept apart: the fetch as resource timing recorded it (a fetch,
/// not the module imported or evaluated); the keyboard module's readiness
/// mark (<see cref="QuizKeysMark"/>), which the page sets only after both of
/// its module imports have completed (<c>Quiz.ImportModulesAsync</c>), so the
/// mark set confirms both imports and the mark absent confirms nothing; then
/// the row absent, present with its fit pending, or present and fitted. The
/// problem, by the number the score panel shows and by its fourth answer's
/// reading, the pill's accessible name as the page gives it. The status
/// strip's text. The navigation buttons, each enabled or not: with a problem
/// on screen, ▶ is disabled only while the controller is busy, which is a
/// transition in flight. A field the page does not show is printed as absent;
/// the score panel and the status strip are absent while the maximize setting
/// suppresses them.
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
            mark: document.documentElement.hasAttribute(mark),
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
    /// each change of <see cref="StateReader"/>'s state as its callback sees
    /// it, a capture-phase click listener noting what was clicked and the
    /// state on screen as the event reached it, and a PerformanceObserver
    /// noting the row-fit module's fetch. Until it first sees a row it also
    /// keeps the last time any of its observations saw none, so the first
    /// row's insertion is bracketed: after that, and no later than the
    /// observation that saw it. It writes no state of the page's. Returns
    /// whether it was installed by this call.
    /// </summary>
    private const string Recorder = $$"""
        mark => {
          if (window.__quizEvidence) return false;
          const read = {{StateReader}};
          const at = () => performance.timeOrigin + performance.now();
          const pending = [], fetches = [];
          let last = null, first = null, lastClick = null, lastWithoutRow = null;
          const observe = (state, when) => {
            if (first !== null) return;
            if (state.row === 'absent') lastWithoutRow = when;
            else first = { kind: 'state', at: when, state, lastWithoutRow };
          };
          const note = () => {
            const when = at();
            const state = read(mark);
            observe(state, when);
            const signature = JSON.stringify(state);
            if (signature === last) return;
            last = signature;
            pending.push({ kind: 'state', at: when, state });
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
            const when = at();
            const c = ev.target instanceof Element ? ev.target.closest('button, input, a, label') : null;
            const target = c ? (c.getAttribute('aria-label') || c.textContent.replace(/\s+/g, ' ').trim()) : String(ev.target?.nodeName);
            const state = read(mark);
            observe(state, when);
            lastClick = { kind: 'click', at: when, target, state };
            pending.push(lastClick);
          }, true);
          window.__quizEvidence = {
            take() {
              fetched(resources.takeRecords());
              const now = at();
              const state = read(mark);
              observe(state, now);
              return { recording: true, now, state, events: pending.splice(0), first, fetches, lastClick };
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

    /// <summary>
    /// Every print's read, in order: its label, what it adds, when the test
    /// issued it and when it returned, and the page's JSON or why there is none.
    /// </summary>
    private readonly List<(string Label, Print Print, double Issued, double Returned, string? Json, string? Failure)> _takes = [];

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

        /// <summary>The last click the recorder saw, and whether the page still shows the problem on screen then.</summary>
        WithClick,
    }

    /// <summary>Which clock stamped a line.</summary>
    private enum Clock
    {
        /// <summary>The test's: <see cref="DateTimeOffset.UtcNow"/>.</summary>
        Test,

        /// <summary>The page's: <c>performance.timeOrigin + performance.now()</c>.</summary>
        Page,
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
    /// line with the gesture's timing and the last click the recorder saw, so
    /// it says whether the page still shows the problem the navigation is
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
    /// included, then write every line, the clocks' measured offset, the first
    /// problem the page rendered, and where that first row falls against each
    /// of the scenario's reads. Never throws.
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
        var issued = NowMs();
        try
        {
            var json = await _page.EvaluateAsync<string>(Take, QuizKeysMark.AttachedAttribute);
            _takes.Add((label, print, issued, NowMs(), json, null));
        }
        catch (Exception ex)
        {
            // Evidence never fails the scenario, and in a finally it must not
            // replace the exception on its way out.
            _takes.Add((label, print, issued, NowMs(), null, Describe(ex)));
        }
    }

    private void WriteAll()
    {
        var takes = _takes.Select(t => (t.Label, t.Print, t.Issued, t.Returned, Parsed: Parse(t.Json, t.Failure))).ToList();
        var read = takes.Where(t => t.Parsed.Snapshot is not null)
            .Select(t => (t.Issued, t.Returned, Snapshot: t.Parsed.Snapshot!)).ToList();
        var offset = MeasureOffset(read);

        // The module's fetches as the last read knew them: the recorder keeps
        // every one, and a late-delivered entry is in a later read's list.
        var last = read.Count > 0 ? read[^1].Snapshot : null;
        var fetches = last?.Fetches ?? [];

        var lines = new List<(double At, Clock Clock, string Text)>(_lines.Select(l => (l.At, Clock.Test, l.Text)));
        foreach (var t in takes)
        {
            if (t.Parsed.Snapshot is not { } s)
            {
                lines.Add((t.Returned, Clock.Test, $"{t.Label}: the page could not be read: {t.Parsed.Failure}"));
                continue;
            }
            lines.AddRange(s.Events.Select(e => (e.At, Clock.Page, DescribeEvent(e, fetches))));
            var line = $"{t.Label}: {DescribeState(s.State, s.Now, fetches)}";
            if (!s.Recording) line += " [no recorder in this document]";
            if (t.Print == Print.WithClick) line += " | " + Leaving(s);
            lines.Add((s.Now, Clock.Page, line));
        }

        Write(Clock.Test, NowMs(), DescribeOffset(offset, read.Count));

        // One order for both clocks: a page time is moved onto the test's
        // clock by the middle of the measured offset. Lines closer together
        // than the offset's bracket are in no proven order.
        var middle = offset is { } o ? (o.Low + o.High) / 2 : 0;
        foreach (var (at, clock, text) in lines.OrderBy(l => l.Clock == Clock.Page ? l.At - middle : l.At))
            Write(clock, at, text);

        // The recorder keeps the first row for the document's life, so the
        // last read that succeeded knows it.
        if (last is not null)
            WriteFirstProblem(last, offset);
    }

    /// <summary>
    /// The page clock's offset from the test's (page minus test), as the run
    /// measured it: each print ran in the page at its page instant, somewhere
    /// between the test's issuing it and its returning, so the offset lies
    /// between that instant less the return and that instant less the issue.
    /// Every print's bracket holds, so the offset lies in their intersection.
    /// Null where nothing was read, or where the brackets do not meet, which a
    /// clock stepping during the run would do.
    /// </summary>
    private static (double Low, double High)? MeasureOffset(
        IReadOnlyCollection<(double Issued, double Returned, Snapshot Snapshot)> reads)
    {
        if (reads.Count == 0) return null;
        var low = reads.Max(r => r.Snapshot.Now - r.Returned);
        var high = reads.Min(r => r.Snapshot.Now - r.Issued);
        return low <= high ? (low, high) : null;
    }

    private static string DescribeOffset((double Low, double High)? offset, int reads) => offset is { } o
        ? $"clocks: the page's clock minus the test's lies in [{Ms(o.Low)}, {Ms(o.High)}] ms (width {Ms(o.High - o.Low)} ms), "
          + $"measured from {reads} prints, each run in the page between the test's issuing it and its returning; "
          + "times closer than that width across the two clocks are too close to call"
        : $"clocks: the offset between the page's clock and the test's could not be measured ({reads} prints read, "
          + "or their brackets do not meet); no page time is placed against a test time";

    /// <summary>
    /// The first problem the page rendered, the bracket on when its row was
    /// inserted (after the recorder's last observation without a row, no later
    /// than the observation that saw it, both on the page's clock), and where
    /// that bracket falls against each of the scenario's reads, moved onto the
    /// test's clock through the measured offset's whole width. A placement is
    /// made only where the brackets separate; otherwise the line says what is
    /// not known, with the raw times.
    /// </summary>
    private void WriteFirstProblem(Snapshot snapshot, (double Low, double High)? offset)
    {
        if (snapshot.First is not { State: { } first } shown)
        {
            Write(Clock.Page, snapshot.Now, "first problem the page rendered: none (the recorder saw no row in this document)");
            foreach (var r in _reads)
            {
                Write(Clock.Test, r.Returned, $"{r.What} (issued {Since(r.Issued)}, returned {Since(r.Returned)}): "
                    + "the recorder saw no row before it or since");
            }
            return;
        }

        var after = shown.LastWithoutRow;
        Write(Clock.Page, shown.At, $"first problem the page rendered: problem {first.Problem ?? "absent"}, "
            + $"4th answer {Quoted(first.Fourth)}, row {first.Row}; first seen by the recorder at {Since(shown.At)}, "
            + (after is { } a
                ? $"last seen without a row at {Since(a)}, so inserted after {Since(a)} and by {Since(shown.At)} (page clock)"
                : "never seen without a row, so inserted at some time by then (page clock)"));

        foreach (var r in _reads)
            Write(Clock.Test, r.Returned, $"{r.What}: {Place(r.Issued, r.Returned, after, shown.At, offset)}");
    }

    /// <summary>
    /// Where a row inserted after <paramref name="insertedAfter"/> and by
    /// <paramref name="insertedBy"/> (page clock) falls against a read issued
    /// and returned at the given test-clock times.
    /// </summary>
    private string Place(
        double issued, double returned, double? insertedAfter, double insertedBy, (double Low, double High)? offset)
    {
        var raw = $"read issued {Since(issued)} and returned {Since(returned)} (test clock); row inserted "
            + (insertedAfter is { } a ? $"after {Since(a)} and " : "")
            + $"by {Since(insertedBy)} (page clock)";
        if (offset is not { } o)
            return $"not placed, the clocks' offset unmeasured: {raw}";

        // On the test's clock, the latest the row can have been inserted, and
        // the earliest, across the offset's whole bracket.
        var latest = insertedBy - o.Low;
        double? earliest = insertedAfter is { } after ? after - o.High : null;

        if (latest < issued)
            return $"the row was in the DOM before the read was issued: {raw}";
        if (earliest > returned)
            return $"the row was not in the DOM until after the read returned, so the read saw no row: {raw}";
        if (earliest >= issued && latest <= returned)
            return $"the row was inserted while the read was in flight, so what the read saw is not known: {raw}";
        return $"too close to call (the row's insertion and the read overlap within the clocks' bracket), "
            + $"so what the read saw is not known: {raw}";
    }

    /// <summary>
    /// The last click the recorder saw, against what the page shows now: a
    /// navigation in flight still shows the problem it is leaving. The click
    /// is named by its target, so the line shows whether it was the
    /// gesture's; it is not matched to the gesture by time, which would cross
    /// the two clocks.
    /// </summary>
    private string Leaving(Snapshot snapshot)
    {
        if (snapshot.LastClick is not { State: { } then } click)
            return "the recorder saw no click";

        var now = snapshot.State;
        var verdict = then.Problem is null && then.Fourth is null
            ? "no problem was identified on screen then"
            : then.Problem == now.Problem && then.Fourth == now.Fourth
                ? "the page still shows that problem"
                : "the page now shows a different problem";
        return $"the recorder saw the last click, on {Quoted(click.Target)}, reach it at {Since(click.At)} (page clock) "
            + $"with problem {then.Problem ?? "absent"}, 4th answer {Quoted(then.Fourth)} on screen: {verdict}";
    }

    private string DescribeEvent(Event e, IReadOnlyList<Event> fetches) => e.Kind switch
    {
        "state" when e.State is { } s => "recorder saw: " + DescribeState(s, e.At, fetches),
        "click" when e.State is { } s =>
            $"recorder saw a click on {Quoted(e.Target)} reach it, with on screen: {DescribeState(s, e.At, fetches)}",
        "fetch" => $"resource timing: actionRowFit.js fetch requested {Since(e.Start ?? e.At)}, "
            + $"response end {Since(e.At)}, status {e.Status?.ToString(CultureInfo.InvariantCulture) ?? "?"}",
        _ => $"unrecognized note '{e.Kind}'",
    };

    private string DescribeState(State s, double at, IReadOnlyList<Event> fetches)
    {
        var fetch = fetches.Where(f => f.At <= at).Select(f => (double?)f.At).FirstOrDefault();
        var fetchText = fetch is { } f ? $"actionRowFit.js fetch complete {Since(f)}" : "no actionRowFit.js fetch complete";
        var markText = s.Mark ? "readiness mark set: both imports confirmed" : "readiness mark absent: imports not confirmed";
        return $"{s.Path} | {fetchText} | {markText} | row {s.Row} | problem {s.Problem ?? "absent"} | "
            + $"4th answer {Quoted(s.Fourth)} of {s.Answers} | status {Quoted(s.Status)} | nav {s.Nav ?? "absent"}";
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

    private static string Quoted(string? text) => text is null ? "absent" : $"\"{text}\"";

    private static string Describe(Exception ex) =>
        $"{ex.GetType().Name}: {ex.Message.Split('\n', 2)[0].Trim()}";

    private static string Ms(double ms) => ms.ToString("0.0", CultureInfo.InvariantCulture);

    private void Write(Clock clock, double atMs, string text)
    {
        var wall = DateTimeOffset.UnixEpoch.AddMilliseconds(atMs);
        var tag = clock == Clock.Page ? "page" : "test";
        _output.WriteLine($"[evidence {tag} {wall:HH:mm:ss.fff} {Since(atMs)}] {text}");
    }

    private string Since(double atMs) =>
        "+" + (atMs - _origin).ToString("0.0", CultureInfo.InvariantCulture) + "ms";

    private static double NowMs() => (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalMilliseconds;

    private sealed record State(
        string Path, bool Mark, string Row, string? Problem, int Answers, string? Fourth, string? Status, string? Nav);

    private sealed record Event(
        string Kind, double At, State? State, string? Target, double? Start, int? Status, double? LastWithoutRow);

    private sealed record Snapshot(
        bool Recording, double Now, State State, Event[] Events, Event? First, Event[] Fetches, Event? LastClick);
}
