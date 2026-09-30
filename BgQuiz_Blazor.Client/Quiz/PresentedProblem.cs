namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;

/// <summary>
/// One entry of a run's presented sequence (SPEC-quiz-history.md §1): a problem
/// the user was shown, with what the run keeps about that showing — where it
/// sat in the problem stream, which side its board was given, and what has been
/// recorded against it. Immutable; a <see cref="QuizRun"/> builds its entries
/// and replaces one when its disposition changes.
///
/// <para>
/// <b>The entry is the problem's identity in the run.</b> A problem is known by
/// its place in the sequence, never by comparing records or submissions
/// (SPEC-quiz-history.md §7), so an entry is reference-equal and defines no
/// equality of its own.
/// </para>
/// </summary>
internal sealed class PresentedProblem
{
    /// <summary>Build an entry. A <see cref="QuizRun"/> does this; nothing else has a sequence to put one in.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="problem"/> or <paramref name="disposition"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="streamSlot"/> is less than 1.</exception>
    internal PresentedProblem(
        BgDecisionData problem, int streamSlot, bool randomHomeBoardOnRight, ProblemDisposition disposition)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentOutOfRangeException.ThrowIfLessThan(streamSlot, 1);
        ArgumentNullException.ThrowIfNull(disposition);
        Problem = problem;
        StreamSlot = streamSlot;
        RandomHomeBoardOnRight = randomHomeBoardOnRight;
        Disposition = disposition;
    }

    /// <summary>The decision that was presented.</summary>
    public BgDecisionData Problem { get; }

    /// <summary>
    /// The problem's 1-based slot in the problem stream — the N of "Problem N
    /// of M" while this problem is on screen (SPEC-quiz-history.md §5).
    ///
    /// <para>
    /// <b>It counts consumed stream slots, not presentations.</b> A position
    /// the orchestration passes over silently (one offering no play choice)
    /// occupies a slot without entering the sequence, so the next problem's
    /// number skips it and the gap shows. That keeps N commensurable with the
    /// run's total, <see cref="QuizRun.ProblemCount"/>, which counts the stream
    /// too: N never exceeds M, and the stream's last slot reads M. The
    /// accepted trade-off is the visible gap — honest about the slot having
    /// been in the quiz — rather than a presented-only N that ends below M.
    /// </para>
    /// </summary>
    public int StreamSlot { get; }

    /// <summary>
    /// The random orientation choice made for this problem when it was
    /// presented — true for home board on the right. The run keeps the choice
    /// for the life of the run, so returning to a problem never re-rolls it
    /// (SPEC-quiz-history.md §5).
    ///
    /// <para>
    /// <b>The choice is stable; the side shown is derived.</b> What a page
    /// draws is this choice plus the user's current orientation setting,
    /// composed in one place outside the run
    /// (<see cref="QuizSettings.EffectiveHomeBoardOnRight"/>): the roll is
    /// used only while the user asks for a random side. So a board does not
    /// flip because the user came back to it, and it may change side because
    /// the user changed the setting — deliberately.
    /// </para>
    ///
    /// <para>
    /// <b>Supplied, never rolled here.</b> Whoever presents the problem takes
    /// the roll and hands it in; the run owns no randomness. And it is
    /// presentation state of this entry, not part of the problem's identity:
    /// the same position presented again, in this run or another, takes a roll
    /// of its own.
    /// </para>
    /// </summary>
    public bool RandomHomeBoardOnRight { get; }

    /// <summary>What has been recorded against this problem — the single source for it.</summary>
    public ProblemDisposition Disposition { get; }

    /// <summary>This entry with <paramref name="disposition"/> recorded against it, everything else as it was.</summary>
    internal PresentedProblem With(ProblemDisposition disposition) =>
        new(Problem, StreamSlot, RandomHomeBoardOnRight, disposition);
}
