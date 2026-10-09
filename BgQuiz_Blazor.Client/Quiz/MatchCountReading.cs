namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// What <see cref="MatchCount"/> knows about one set of inputs
/// (<see cref="MatchCount.ReadingFor"/>): that they are being counted, what
/// they matched, or nothing. A page reads its count's activity and its result
/// from this one reading for its own current inputs, so a request for other
/// inputs — a selection since cleared or re-picked, a filter since edited out
/// of effect — can neither show a result nor make the page busy
/// (halheinrich/backgammon#374).
///
/// <para>
/// Exactly one of three states, so "counting with a result" is not
/// representable: <see cref="Counting"/>; <see cref="Counted"/> with what the
/// inputs matched; or <see cref="Unknown"/> — no count of these inputs is
/// running and none is known, because they are not the inputs being counted
/// or because their count failed. A failed count is unknown, never zero.
/// </para>
/// </summary>
internal sealed class MatchCountReading
{
    private MatchCountReading(bool isCounting, MatchSummary? summary)
    {
        IsCounting = isCounting;
        Summary = summary;
    }

    /// <summary>Nothing running and nothing known for the inputs read.</summary>
    public static MatchCountReading Unknown { get; } = new(isCounting: false, summary: null);

    /// <summary>The inputs read are being counted; nothing is known of them yet.</summary>
    public static MatchCountReading Counting { get; } = new(isCounting: true, summary: null);

    /// <summary>The inputs read have been counted, and matched <paramref name="summary"/>.</summary>
    /// <param name="summary">What the inputs matched.</param>
    /// <returns>The reading.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="summary"/> is <see langword="null"/>.</exception>
    public static MatchCountReading Counted(MatchSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new(isCounting: false, summary);
    }

    /// <summary>Whether the count of the inputs read is running.</summary>
    public bool IsCounting { get; }

    /// <summary>
    /// What the inputs read matched, or <see langword="null"/> while they are
    /// being counted, when their count failed, or when they are not the inputs
    /// being counted.
    /// </summary>
    public MatchSummary? Summary { get; }
}
