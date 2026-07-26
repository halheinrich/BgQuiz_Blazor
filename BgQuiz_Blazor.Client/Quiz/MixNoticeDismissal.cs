namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;

/// <summary>
/// Per-app holder for one bit of <c>Quiz</c>-page presentation state: whether the
/// user has dismissed the mix composition notice for the run that notice
/// describes.
///
/// <para>
/// <b>Why it exists.</b> The composition notice (both variants — the capless
/// composition-only status line and the length-bound shortfall alert) says how the
/// quiz now underway was built. That is worth reading <i>before</i> answering and
/// is stale chrome afterwards, yet it rendered above the board for the whole quiz
/// because it derives from <see cref="QuizController.LastComposition"/>, which
/// lives as long as the run does. Dismissal is recorded here rather than by
/// clearing that telemetry: <see cref="QuizController.LastComposition"/> and
/// <see cref="QuizController.ActiveMixHasLength"/> are load-bearing — they pick
/// the notice's framing, they carry <see cref="QuizController.ProblemCount"/>, and
/// Home's composed-to-zero wording reads them — so a presentation concern must not
/// destroy them.
/// </para>
///
/// <para>
/// Lifetime: <b>Scoped</b> — one instance per loaded app (one tab), like the
/// start-gate holders. A component field would have been smaller but wrong: the
/// Quiz page is re-instantiated on in-app navigation, so the <i>Show stats</i>
/// round trip (a mainline mid-quiz gesture, right there in the action row) would
/// resurrect a notice the user had already dismissed. See INSTRUCTIONS' Pitfalls:
/// anything that must survive navigation belongs in a holder.
/// </para>
///
/// <para>
/// <b>Keyed on the composition's identity, so no reset wiring exists to forget.</b>
/// Each Start/Restart builds a fresh <c>MixedProblemSetSource</c>, which assigns a
/// fresh <see cref="MixComposition"/> before its first yield — so a new run cannot
/// match a dismissal recorded against the old one, and its notice shows again with
/// nothing on Home or Done having to remember to clear anything. The comparison is
/// deliberately <see cref="object.ReferenceEquals"/> and not <c>==</c>:
/// <see cref="MixComposition"/> is a record, so value equality would keep a
/// Restart that happened to draw an identical composition silently dismissed.
/// </para>
/// </summary>
internal sealed class MixNoticeDismissal
{
    /// <summary>
    /// The composition instance whose notice the user dismissed, or
    /// <see langword="null"/> before any dismissal. Held only as an identity
    /// token — never read for its contents.
    /// </summary>
    private MixComposition? _dismissed;

    /// <summary>
    /// Record that the notice for <paramref name="composition"/> has served its
    /// purpose and should not render again for that run.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="composition"/> is null.</exception>
    public void Dismiss(MixComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        _dismissed = composition;
    }

    /// <summary>
    /// Whether <paramref name="composition"/> is the very composition whose
    /// notice was dismissed. False for any other instance — including a
    /// value-identical one from a later run (see the type remarks) — and false
    /// for <see langword="null"/>, which never has a notice to dismiss.
    /// </summary>
    public bool IsDismissed(MixComposition? composition) =>
        composition is not null && ReferenceEquals(_dismissed, composition);
}
