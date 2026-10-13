using BgDataTypes_Lib;
using BgGame_Lib;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// What a test's <see cref="RecordingProblemSetSource"/>s did, in order, across
/// every source that shares it: each stream opened, each draw and which stream
/// it came from, and each release. One log is handed to every source a test
/// builds, so a draw can be told apart by the stream it came from — the
/// question <c>SPEC-quiz-history.md</c> §8 turns on, since a kept run must
/// keep drawing from its own stream after an attempt that did not commit.
/// </summary>
internal sealed class StreamLog
{
    private readonly List<RecordedStream> _opened = [];
    private readonly List<(string Stream, BgDecisionData Item)> _draws = [];

    /// <summary>Every stream opened, in the order it was opened.</summary>
    public IReadOnlyList<RecordedStream> Opened => _opened;

    /// <summary>Every item drawn, with the label of the stream it came from, in order.</summary>
    public IReadOnlyList<(string Stream, BgDecisionData Item)> Draws => _draws;

    /// <summary>The one stream opened under <paramref name="label"/>.</summary>
    public RecordedStream Stream(string label) => Assert.Single(_opened, s => s.Label == label);

    internal void RecordOpened(RecordedStream stream) => _opened.Add(stream);

    /// <summary>
    /// Called as each stream is released, before any injected release fault —
    /// so a test can read the consumer's state at the moment of the release,
    /// such as whether a switch has already happened.
    /// </summary>
    public Action<RecordedStream>? OnRelease { get; set; }

    internal void RecordDraw(RecordedStream stream, BgDecisionData item) => _draws.Add((stream.Label, item));

    internal void RecordRelease(RecordedStream stream) => OnRelease?.Invoke(stream);
}

/// <summary>
/// One enumeration of a <see cref="RecordingProblemSetSource"/>: its label —
/// the source's name and the enumeration's 1-based number, <c>"A#1"</c> — how
/// many items it yielded, and how many times it was released.
/// </summary>
internal sealed class RecordedStream(string label)
{
    public string Label { get; } = label;

    public int DrawCount { get; internal set; }

    public int ReleaseCount { get; internal set; }
}

/// <summary>
/// An <see cref="IProblemSetSource"/> that records every stream it opens, every
/// draw and every release into a shared <see cref="StreamLog"/>, and can be told
/// to fail at each of the three places a stream can: opening it
/// (<c>openFaults</c>), drawing from it (<c>faultAtDraw</c>, 1-based, on every
/// enumeration), and releasing it (<c>releaseFaults</c>, which counts the
/// release first and then throws). The fault-injection double for
/// <c>QuizController</c>'s Start and Restart: it is how a test sees which
/// stream a draw came from after an attempt that did not commit, and that
/// each stream is released exactly once.
///
/// <para>
/// The enumerator is written by hand rather than as an async iterator, so a
/// release can throw and a draw after a fault is the test's to define: a
/// faulted stream stays faulted, and a draw after release is a test failure
/// in its own right, since nothing may draw from a released stream.
/// </para>
/// </summary>
internal sealed class RecordingProblemSetSource : IProblemSetSource
{
    /// <summary>The message an injected open or draw fault carries, so a test can tell it from any other.</summary>
    public const string FaultMessage = "The recording source was told to fault.";

    /// <summary>The message an injected release fault carries — distinct, so a test can tell which error surfaced.</summary>
    public const string ReleaseFaultMessage = "The recording source was told to fault its release.";

    private readonly StreamLog _log;
    private readonly IReadOnlyList<BgDecisionData> _items;
    private readonly bool _openFaults;
    private readonly int? _faultAtDraw;
    private readonly bool _releaseFaults;
    private int _enumerations;

    public RecordingProblemSetSource(
        string name,
        StreamLog log,
        IReadOnlyList<BgDecisionData> items,
        bool openFaults = false,
        int? faultAtDraw = null,
        bool releaseFaults = false)
    {
        Name = name;
        _log = log;
        _items = items;
        _openFaults = openFaults;
        _faultAtDraw = faultAtDraw;
        _releaseFaults = releaseFaults;
    }

    public string Name { get; }

    public int? Count => _items.Count;

    public IAsyncEnumerable<BgDecisionData> EnumerateAsync(CancellationToken cancellationToken = default) =>
        new Enumeration(this);

    private sealed class Enumeration(RecordingProblemSetSource source) : IAsyncEnumerable<BgDecisionData>
    {
        public IAsyncEnumerator<BgDecisionData> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            if (source._openFaults) throw new InvalidOperationException(FaultMessage);
            var stream = new RecordedStream($"{source.Name}#{++source._enumerations}");
            source._log.RecordOpened(stream);
            return new Enumerator(source, stream);
        }
    }

    private sealed class Enumerator(RecordingProblemSetSource source, RecordedStream stream) : IAsyncEnumerator<BgDecisionData>
    {
        private BgDecisionData? _current;

        public BgDecisionData Current => _current ?? throw new InvalidOperationException("No item has been drawn.");

        public async ValueTask<bool> MoveNextAsync()
        {
            Assert.True(stream.ReleaseCount == 0, $"A draw from {stream.Label} after it was released.");
            await Task.Yield();

            var draw = stream.DrawCount + 1;
            if (draw == source._faultAtDraw) throw new InvalidOperationException(FaultMessage);
            if (draw > source._items.Count) return false;

            stream.DrawCount = draw;
            _current = source._items[draw - 1];
            source._log.RecordDraw(stream, _current);
            return true;
        }

        public ValueTask DisposeAsync()
        {
            stream.ReleaseCount++;
            source._log.RecordRelease(stream);
            if (source._releaseFaults) throw new InvalidOperationException(ReleaseFaultMessage);
            return ValueTask.CompletedTask;
        }
    }
}
