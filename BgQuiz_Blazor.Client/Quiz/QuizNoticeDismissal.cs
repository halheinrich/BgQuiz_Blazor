namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;

/// <summary>
/// The <c>Quiz</c> page's dismissible notices, named so a dismissal of one is
/// never a dismissal of another. The two stats <i>degrade</i> notices share a
/// slot, because they are mutually exclusive branches of one condition and
/// therefore one notice as far as the user is concerned; the retirement report
/// gets its own, because it can be showing at the same time as either of them.
/// </summary>
internal enum QuizNotice
{
    /// <summary>
    /// The mix composition notice, in either framing — the capless
    /// composition-only status line or the length-bound shortfall alert.
    /// Occurrence: the <see cref="MixComposition"/> it describes.
    /// </summary>
    Composition,

    /// <summary>
    /// The active stats context's degrade notice —
    /// <see cref="QuizStatsStatus.LoadFailed"/>'s polite one or
    /// <see cref="QuizStatsStatus.WriteFailed"/>'s assertive one. Occurrence:
    /// <see cref="QuizStatsStore.StatusOccurrence"/>.
    /// </summary>
    StatsContext,

    /// <summary>
    /// The stats-retirement report: this run found a stats file in the retired
    /// format, set it aside, and started a fresh one (SPEC-stats-identity.md
    /// §3). Occurrence: <see cref="QuizStatsStore.StatsRetiredOccurrence"/>,
    /// which is null on every run that retired nothing.
    /// </summary>
    StatsRetired,
}

/// <summary>
/// Per-app holder for one bit of <c>Quiz</c>-page presentation state per notice:
/// whether the user has dismissed the notice's <i>current occurrence</i>.
///
/// <para>
/// <b>Why it exists.</b> Every notice above the board says something worth
/// reading once and then costs the board space for the rest of the run — the
/// composition notice describes how this quiz was built, the stats notices
/// report that recording has degraded. Neither may be suppressed by the
/// maximize mode (<c>SPEC-quiz-view.md</c> §4: the composition notice retires on
/// the first answer, so hiding it while answering means it is never seen, and a
/// recording failure must be seen), so the answer to the space they cost is the
/// user dismissing them — which also frees that space for the board, which is
/// the point of the mode they render inside.
/// </para>
///
/// <para>
/// Lifetime: <b>Scoped</b> — one instance per loaded app (one tab), like the
/// start-gate holders. A component field would have been smaller but wrong: the
/// Quiz page is re-instantiated on in-app navigation, so the <i>Show stats</i>
/// round trip (a mainline mid-quiz gesture, right there in the action row) would
/// resurrect a notice the user had already dismissed. See INSTRUCTIONS' Pitfalls:
/// anything that must survive navigation belongs in a holder. Nothing here is
/// persisted — a dismissal is transient by design, and a reload has no quiz to
/// come back to anyway.
/// </para>
///
/// <para>
/// <b>Keyed on an occurrence token, so no reset wiring exists to forget.</b> A
/// dismissal records <i>which</i> occurrence was dismissed, never a bare "this
/// notice is off". The next occurrence is a different token and shows fresh with
/// nothing on Home, Done, or the controller having to remember to clear
/// anything. The comparison is deliberately <see cref="object.ReferenceEquals"/>
/// and not <c>==</c>: <see cref="MixComposition"/> is a record, so value equality
/// would keep a Restart that happened to draw an identical composition silently
/// dismissed. Every occurrence token this holder is handed therefore means
/// <b>identity, never content</b> — see <see cref="QuizStatsStore.StatusOccurrence"/>,
/// which exists to give the stats notices the same kind of token the composition
/// already had.
/// </para>
///
/// <para>
/// Generalized from the composition-only <c>MixNoticeDismissal</c> (issue
/// <c>halheinrich/backgammon#41</c>) by adding the slot key and nothing else —
/// the composition notice's own contract, including its retire-on-first-answer
/// dismissal, is unchanged by the move.
/// </para>
/// </summary>
internal sealed class QuizNoticeDismissal
{
    /// <summary>
    /// The occurrence token dismissed per notice, absent until that notice has
    /// been dismissed at least once. Held only as identity — never read for its
    /// contents, and never enumerated.
    /// </summary>
    private readonly Dictionary<QuizNotice, object> _dismissed = [];

    /// <summary>
    /// Record that <paramref name="notice"/>'s notice for
    /// <paramref name="occurrence"/> has served its purpose and should not render
    /// again for that occurrence.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="occurrence"/> is null.</exception>
    public void Dismiss(QuizNotice notice, object occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        _dismissed[notice] = occurrence;
    }

    /// <summary>
    /// Whether <paramref name="occurrence"/> is the very occurrence of
    /// <paramref name="notice"/> that was dismissed. False for any other instance
    /// — including a value-identical one from a later run (see the type remarks)
    /// — and false for <see langword="null"/>, which never has a notice to
    /// dismiss.
    /// </summary>
    public bool IsDismissed(QuizNotice notice, object? occurrence) =>
        occurrence is not null
        && _dismissed.TryGetValue(notice, out var dismissed)
        && ReferenceEquals(dismissed, occurrence);
}
