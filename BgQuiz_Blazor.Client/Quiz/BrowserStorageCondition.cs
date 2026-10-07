namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The per-app (Scoped, one-per-tab in WASM) <b>one fact that browser storage
/// has been refused</b> this visit (issue <c>halheinrich/backgammon#360</c>):
/// whether any call this app makes on the browser's <c>localStorage</c> or
/// <c>sessionStorage</c> has been refused, and — as an occurrence token — which
/// condition a dismissal of <c>Home</c>'s storage notice belongs to.
///
/// <para>
/// <b>One fact, many reporters.</b> Every store that touches browser storage
/// reports a refusal here from its own guard — <see cref="QuizSettings"/>,
/// <see cref="MixDraft"/>, <see cref="NotesPlacementStore"/> and
/// <see cref="QuizLiveMarker"/> — and <c>Home</c> reports the hosted filter
/// panel's <c>FilterSurface.OnStorageUnavailable</c>. They feed one occurrence
/// because the user is told one thing: the browser is not keeping what BgQuiz
/// asks it to keep, so the filters, the mix and the settings work for this
/// visit and may not be there for the next. Which key was refused first, and
/// by whom, changes nothing the user can do; separate notices for the panel's
/// keys and the app's would say the same sentence twice. A refusal before
/// <c>Home</c> mounts — a settings read on a cold deep link to Settings, the
/// notes' placement on the Quiz page — lands here all the same and is on
/// <c>Home</c> the next time it renders, because the holder outlives every
/// page.
/// </para>
///
/// <para>
/// <b>The occurrence, and when it ends.</b> It begins at the first refusal
/// reported and lasts for the rest of the visit: every later refusal, from any
/// reporter — a remounted filter panel's fresh report included — is the same
/// condition and keeps the same token, so a dismissal survives navigation,
/// remounting and duplicate reports (<c>SPEC-notices.md</c> §2). A reload
/// starts a fresh app with no occurrence. <b>No global recovery is
/// tracked</b>, so there is no second occurrence. Recovery can happen inside
/// one store — <see cref="QuizSettings"/> writes the whole settings object,
/// so a later write that lands repairs an earlier refused one — but a success
/// in one store establishes nothing about another (the mix's refused write is
/// still unsaved after a settings write lands), and a read that succeeds says
/// nothing about writes (the quota case: reads served, writes refused).
/// Nothing records which stores are whole again, so no success ends the
/// occurrence: ending it on one would tell the user their choices are kept
/// while one may not be.
/// </para>
///
/// <para>
/// <b>It reports; it never gates.</b> No store consults this before calling
/// storage: one store's refused write does not establish that another's read
/// will fail, so every store keeps trying its own calls and keeps every value
/// they return. Each store logs its own refusal, with the exception and what
/// it costs that store; this holder adds no log of its own.
/// </para>
/// </summary>
internal sealed class BrowserStorageCondition
{
    /// <summary>
    /// The occurrence of the refusal condition — <see langword="null"/> while
    /// nothing has been refused this visit, and one opaque token from the first
    /// refusal on. <b>The token is the flag</b>, the
    /// <see cref="QuizStatsStore.StatsRetiredOccurrence"/> discipline: no
    /// companion boolean can disagree with it. Compared by identity only, as
    /// every occurrence <see cref="QuizNoticeDismissal"/> holds.
    /// </summary>
    public object? Occurrence { get; private set; }

    /// <summary>
    /// Raised once, when the condition begins, so a page already on screen —
    /// <c>Home</c>, when the mix panel's hydration is refused after its first
    /// render — shows the notice without waiting for its next render.
    /// </summary>
    public event Action? Began;

    /// <summary>
    /// Report that the browser refused a storage call. The first report begins
    /// the condition; every later one is the same condition and changes
    /// nothing (see the type's remarks for why there is no second occurrence).
    /// </summary>
    public void ReportRefused()
    {
        if (Occurrence is not null) return;
        Occurrence = new object();
        Began?.Invoke();
    }
}
