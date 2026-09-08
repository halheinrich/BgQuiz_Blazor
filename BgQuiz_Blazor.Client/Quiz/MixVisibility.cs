namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// <b>The one derivation of the mix's <i>visible</i> fact</b>
/// (<c>SPEC-filtering.md</c> §5, "Visible means in effect — the setting is the
/// one control", ruled 2026-09-07): visible ⟺ the
/// <see cref="QuizSettings.WeightQuizzesByStats"/> setting is on <i>and</i> the
/// picked folder holds a stats document with content
/// (<see cref="IProblemStatsSink.PickedFolderHasStats"/>).
///
/// <para>
/// <b>Visible means in effect.</b> The rule in the user's words is that a
/// visible mix always applies and a hidden one never does, so this one boolean
/// answers two questions that must never disagree: whether <c>Home</c> renders
/// the mix panel, and whether the rows on screen are what Start composes with.
/// <c>Done</c> reads the same member for Restart. Screen and effect stay
/// identical — Fork B's purpose — now with one control and no consent bit.
/// </para>
///
/// <para>
/// <b>Why a service and not a member on either input.</b> The derivation needs
/// both halves, and neither half may learn about the other:
/// <see cref="QuizStatsStore"/> is about what a folder holds and must not grow
/// a dependency on user settings, while <see cref="QuizSettings"/> is a bag of
/// stored choices that knows nothing of folders. Hanging the conjunction on
/// either would put a UI-visibility policy on an abstraction that has no
/// business holding one — and on the stats side it would reach
/// <see cref="IProblemStatsSink"/>, the controller's sink, which has no
/// business knowing the panel exists at all. A third type that depends on both
/// and is depended on by the two pages is the shape that leaves each input
/// saying only its own half. It takes the same Scoped slot
/// <c>MixConsent</c> held, and holds no state whatever: the two facts already
/// have owners, and a cached copy of a derivation is the second copy of a
/// truth this ruling exists to delete.
/// </para>
///
/// <para>
/// <b>No <c>Changed</c> event, and the reason is structural.</b> Both inputs
/// move only where this fact is not on screen: the setting changes on the
/// Settings page, and the stats fact changes when a pick lands — which
/// <c>Home</c> already re-renders for, driving the probe itself. So every
/// reader picks the new answer up on its own next render. Read live per render,
/// never captured into a field.
/// </para>
/// </summary>
internal sealed class MixVisibility(QuizSettings settings, IProblemStatsSink stats)
{
    /// <summary>
    /// Whether the mix panel is on screen — and therefore whether the mix on
    /// screen is in effect. The two are the same fact by ruling, which is why
    /// this member has no sibling.
    ///
    /// <para>
    /// Deliberately over the bare fact rather than
    /// <see cref="IProblemStatsSink.CanWeightMix"/>: visibility asks what the
    /// folder <i>holds</i>, and the write capability the policy adds belongs to
    /// the controller's refusal, not to whether there is anything to show. The
    /// two read the same today (a probe cannot find stats in a folder it could
    /// not have written) — see <c>QuizStatsStore.CanWeightMix</c> for why that
    /// is the probe's invariant and not the model's.
    /// </para>
    /// </summary>
    public bool IsVisible => settings.WeightQuizzesByStats && stats.PickedFolderHasStats;
}
