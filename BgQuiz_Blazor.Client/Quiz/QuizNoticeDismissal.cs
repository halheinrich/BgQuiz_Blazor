namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;

/// <summary>
/// The app's dismissible notices, named so a dismissal of one is never a
/// dismissal of another. Two notices share a slot exactly when they are
/// mutually exclusive branches of one condition and therefore one notice as
/// far as the user is concerned (the <c>Quiz</c> page's two stats <i>degrade</i>
/// notices; <c>Home</c>'s three stats-capability branches); notices that can
/// show side by side each get their own.
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

    /// <summary>
    /// <c>Home</c>'s truncated-pick report (issue #59): the folder held more
    /// files of some kind than that kind's cap admits, so the first N were
    /// taken and the rest left unread. One slot for the whole alert, however
    /// many kinds it lists — it is one report about one pick. Occurrence:
    /// <see cref="PickedProblemFolder.PickOccurrence"/>.
    /// </summary>
    PickTruncations,

    /// <summary>
    /// <c>Home</c>'s pick-time stats-capability notice — the stats-location
    /// info line, or either capability-degrade warning
    /// (<c>BrowserUnsupported</c> / <c>PermissionDenied</c>). One slot because
    /// the three are mutually exclusive renderings of one verdict
    /// (<see cref="PickedProblemFolder.Capability"/>, fixed per pick), so at
    /// most one can ever show for a given occurrence. Occurrence:
    /// <see cref="PickedProblemFolder.PickOccurrence"/> — its own slot beside
    /// <see cref="PickTruncations"/>, because the two notices render side by
    /// side and dismiss independently.
    /// </summary>
    PickStatsCapability,
}

/// <summary>
/// Per-app holder for one bit of presentation state per notice: whether the
/// user has dismissed the notice's <i>current occurrence</i>.
///
/// <para>
/// <b>Why it exists.</b> Every notice it covers says something worth reading
/// once and then costs screen space for as long as its subject stands. On the
/// <c>Quiz</c> page that space is the board's — the composition notice
/// describes how this quiz was built, the stats notices report that recording
/// has degraded, and neither may be suppressed by the maximize mode
/// (<c>SPEC-quiz-view.md</c> §4: the composition notice retires on the first
/// answer, so hiding it while answering means it is never seen, and a
/// recording failure must be seen). On <c>Home</c> the pick-outcome notices
/// render for as long as the pick is held (issue #107). In both cases the
/// answer to the space they cost is the user dismissing them.
/// </para>
///
/// <para>
/// Lifetime: <b>Scoped</b> — one instance per loaded app (one tab), like the
/// start-gate holders. A component field would have been smaller but wrong:
/// pages are re-instantiated on in-app navigation, so a mainline round trip —
/// <i>Show stats</i> from the Quiz page, or any visit away from Home while a
/// pick is held — would resurrect a notice the user had already dismissed. See
/// INSTRUCTIONS' Pitfalls: anything that must survive navigation belongs in a
/// holder. Nothing here is persisted — a dismissal is transient by design, and
/// a reload resets the state every covered notice describes anyway.
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
/// already had, and <see cref="PickedProblemFolder.PickOccurrence"/>, which does
/// the same for <c>Home</c>'s pick-outcome notices.
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
