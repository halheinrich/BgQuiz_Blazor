namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The identity of one <b>sample</b> — the files a pick or a resample drew
/// from the folder (<c>SPEC-filtering.md</c> §4, "The sample, the limits and
/// Resample"). The parse and the count follow it: a new one is minted by every
/// pick, every resample and every Clear, and the parse cache, the match count,
/// the pick-time stats probe and the pick-time notices' occurrence are keyed
/// by it, so each of them expires with the sample it describes and none of
/// them by anyone remembering to reset it.
///
/// <para>
/// <b>Opaque, and equal only to itself</b> — compared by reference, as every
/// occurrence token <see cref="QuizNoticeDismissal"/> is handed must be. It
/// carries nothing a consumer could compare by value, and it is a different
/// type from the setup's identity (<see cref="SetupIdentity"/>), which a
/// resample keeps while this one changes.
/// </para>
/// </summary>
internal sealed class SampleIdentity
{
    private readonly int _sequence;

    /// <summary>The identity of the holder's <paramref name="sequence"/>th sample.</summary>
    internal SampleIdentity(int sequence) => _sequence = sequence;

    /// <summary>A description for diagnostics — logs and test failure messages.</summary>
    public override string ToString() => $"sample {_sequence}";
}
