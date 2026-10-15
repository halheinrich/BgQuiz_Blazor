namespace BgQuiz_Blazor.Tests;

/// <summary>
/// A stream over <paramref name="inner"/> that draws exactly as the inner
/// stream does — or faults at the <paramref name="faultAtDraw"/>th draw — and
/// whose release releases the inner stream and then fails: the shape of a
/// stream whose release cannot be trusted (<c>SPEC-quiz-history.md</c> §8,
/// "Releasing never misreports"), for the consumers that must not let that
/// failure replace what the draw came to.
/// </summary>
internal sealed class ReleaseFailingStream<T>(IAsyncEnumerable<T> inner, int? faultAtDraw = null) : IAsyncEnumerable<T>
{
    /// <summary>The message the injected draw fault carries.</summary>
    public const string DrawFaultMessage = "The stream was told to fault a draw.";

    /// <summary>The message the injected release fault carries — distinct, so a test can tell which error surfaced.</summary>
    public const string ReleaseFaultMessage = "The stream was told to fault its release.";

    /// <summary>How many times a stream of this was released.</summary>
    public int ReleaseCount { get; private set; }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new Enumerator(this, inner.GetAsyncEnumerator(cancellationToken));

    private sealed class Enumerator(ReleaseFailingStream<T> owner, IAsyncEnumerator<T> inner) : IAsyncEnumerator<T>
    {
        private int _draws;

        public T Current => inner.Current;

        public async ValueTask<bool> MoveNextAsync()
        {
            if (++_draws == owner._faultAtDraw) throw new InvalidOperationException(DrawFaultMessage);
            return await inner.MoveNextAsync();
        }

        public async ValueTask DisposeAsync()
        {
            owner.ReleaseCount++;
            await inner.DisposeAsync();
            throw new InvalidOperationException(ReleaseFaultMessage);
        }
    }

    private readonly int? _faultAtDraw = faultAtDraw;
}
