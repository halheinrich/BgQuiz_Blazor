namespace BgQuiz_Blazor.Client.Quiz;

using Microsoft.Extensions.Logging;

/// <summary>
/// The one rule this app releases a stream it drew from by
/// (<c>SPEC-quiz-history.md</c> §8, "Releasing never misreports"): what the
/// draw came to stands — a fault, the end of the stream, or a complete walk —
/// and a release that fails is logged where the release happens, and nowhere
/// else. It never replaces the draw's outcome and is never reported as a
/// failed Start, Restart, count or parse.
///
/// <para>
/// <b>Why the consumer guards it.</b> The producer's decorators no longer let
/// an inner stream's release replace a draw's outcome; they throw that
/// failure from their own release (BgGame_Lib's "Releasing the inner
/// stream"). A plain <c>await foreach</c> releases in its own
/// <c>finally</c>, where a throwing release replaces whatever the loop was
/// ending with — its fault, or its end — exactly as the decorators once did
/// inside. So every place here that releases a stream it drew from releases
/// it through this type: a run's and an attempt's stream
/// (<see cref="QuizController"/>), the match count's drain
/// (<see cref="QuizController.SummarizeMatchesAsync"/>), and the parse walk
/// (<see cref="CachedProblemSetSource"/>).
/// </para>
/// </summary>
internal static class StreamRelease
{
    /// <summary>
    /// Release <paramref name="stream"/>. A release that throws is logged on
    /// <paramref name="logger"/>, naming <paramref name="what"/> was released —
    /// and, when the draw ended with <paramref name="outcome"/>, that error,
    /// which is the one the caller reports — and never thrown.
    /// </summary>
    /// <param name="stream">The stream to release.</param>
    /// <param name="logger">Where the release happens, and so where its failure is said.</param>
    /// <param name="what">What the stream is, for the log line.</param>
    /// <param name="outcome">The error the draw ended with, or null when it ended without one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/>, <paramref name="logger"/> or <paramref name="what"/> is null.</exception>
    public static async ValueTask ReleaseAsync(
        IAsyncDisposable stream, ILogger logger, string what, Exception? outcome = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(what);
        try
        {
            await stream.DisposeAsync();
        }
        catch (Exception releaseFault)
        {
            if (outcome is null)
                logger.LogWarning(releaseFault,
                    "Releasing {What} failed; what was drawn from it stands.", what);
            else
                logger.LogWarning(releaseFault,
                    "Releasing {What} also failed; the error it ended with is the one reported: {Outcome}",
                    what, outcome.Message);
        }
    }

    /// <summary>
    /// Draw every item of <paramref name="source"/>, in order, into
    /// <paramref name="each"/>, then release the stream
    /// (<see cref="ReleaseAsync"/>). The draw's outcome stands: its end
    /// returns normally whatever the release does, and its fault — a
    /// cancellation included, and a fault of <paramref name="each"/>'s — is
    /// the one thrown, with the release's failure logged beside it.
    /// </summary>
    /// <param name="source">What to draw from.</param>
    /// <param name="each">Takes each item drawn.</param>
    /// <param name="logger">Where the release happens, and so where its failure is said.</param>
    /// <param name="what">What the stream is, for the log line.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static async Task DrainAsync<T>(
        IAsyncEnumerable<T> source, Action<T> each, ILogger logger, string what)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(each);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(what);

        var stream = source.GetAsyncEnumerator();
        try
        {
            while (await stream.MoveNextAsync()) each(stream.Current);
        }
        catch (Exception fault)
        {
            await ReleaseAsync(stream, logger, what, fault);
            throw;
        }
        await ReleaseAsync(stream, logger, what);
    }
}
