namespace BgQuiz_Blazor.Client.Quiz;

using XgFilter_Lib.Filtering;

/// <summary>
/// Per-app holder for the filter config the user has <i>deliberately applied</i>
/// on <c>Home</c> — the filter half of the quiz-start gate.
///
/// <para>
/// Lifetime: <b>Scoped</b> — in the WebAssembly client that resolves to one
/// instance per loaded app (one tab), the same lifetime as
/// <see cref="PickedProblemFolder"/> and <see cref="QuizController"/>. It holds the
/// <see cref="FilterConfig"/> captured when the user clicks <i>Apply Filter</i>,
/// so "filters were applied at least once this session" survives in-app
/// navigation: when <c>Home</c> is re-instantiated on navigate-back the gate is
/// re-derived from this holder rather than from a transient component field
/// (which reset to <c>false</c>, disabling Start until a needless re-click).
/// </para>
///
/// <para>
/// <b>Gate semantics — applied, not merely present.</b> <see cref="IsApplied"/>
/// means the user took the deliberate Apply action, not just that some config
/// exists. Editing any filter control (the panel's dirty signal) must
/// <see cref="Clear"/> this holder, so a half-edited, un-applied filter set
/// disables Start. Restoring the panel's values from localStorage is silent (it
/// raises neither the applied nor the dirty callback), so it never spuriously
/// marks applied or clears an existing applied state.
/// </para>
///
/// <para>
/// <b>Two facts, two lifetimes.</b> Beside the config itself this holder
/// records <i>which pick</i> the last Apply was made for
/// (<see cref="WasAppliedFor"/>). The config is edit-coupled — any control
/// change <see cref="Clear"/>s it — while the pick stamp is <i>not</i>: it
/// answers "has this corpus ever been filtered?", which a half-typed edit does
/// not un-answer. The two are independent by design, and
/// <c>Home</c> gates on each separately (Start on the config; Apply Mix on the
/// stamp — see <see cref="WasAppliedFor"/>).
/// </para>
///
/// <para>
/// In-memory only: the applied state survives in-app navigation but is reset by
/// a full browser reload, which re-boots the WASM runtime. Persisting it across
/// reloads is a deferred phase and out of scope by design — reload-reset matches
/// <see cref="PickedProblemFolder"/> and <see cref="QuizController"/>.
/// </para>
/// </summary>
internal sealed class AppliedFilter
{
    /// <summary>
    /// The applied filter config; <c>null</c> until the user applies one, and
    /// again after an edit clears it via <see cref="Clear"/>.
    /// </summary>
    public FilterConfig? Config { get; private set; }

    /// <summary>
    /// The <see cref="PickedProblemFolder.PickGeneration"/> the last
    /// <see cref="Set"/> was stamped with; <c>null</c> until the first Apply of
    /// the app's lifetime. Deliberately <i>not</i> cleared by
    /// <see cref="Clear"/> — see <see cref="WasAppliedFor"/>.
    /// </summary>
    private int? _appliedGeneration;

    /// <summary>True once the user has deliberately applied a filter config.</summary>
    public bool IsApplied => Config is not null;

    /// <summary>
    /// Record <paramref name="config"/> as the deliberately-applied filter set,
    /// stamped with the pick it was applied against.
    /// </summary>
    /// <param name="config">The config the user committed.</param>
    /// <param name="pickGeneration">
    /// The current <see cref="PickedProblemFolder.PickGeneration"/> — what
    /// <see cref="WasAppliedFor"/> later compares against. Passed in rather than
    /// read from an injected folder holder: this stays a value holder with no
    /// dependency on its sibling, and <c>Home</c> — which owns the page's
    /// sequencing story — states the relationship at the one call site.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
    public void Set(FilterConfig config, int pickGeneration)
    {
        ArgumentNullException.ThrowIfNull(config);
        Config = config;
        _appliedGeneration = pickGeneration;
    }

    /// <summary>
    /// Whether a filter has been applied for the pick identified by
    /// <paramref name="pickGeneration"/> — i.e. "this corpus has been filtered
    /// at least once". <c>Home</c> gates <c>Apply Mix</c> on this (the mix draws
    /// from the filtered pool, so mix-before-filter is premature), passing
    /// <see cref="PickedProblemFolder.PickGeneration"/> live.
    ///
    /// <para>
    /// <b>Why a generation stamp rather than a flag.</b> The counter is
    /// monotonic and bumped by <i>both</i> <see cref="PickedProblemFolder.Set"/>
    /// and <see cref="PickedProblemFolder.Clear"/>, so ending a setup expires
    /// this answer by construction — there is no reset to call and none to
    /// forget, and no stored judgement that can outlive the state it judges
    /// (finding AK's failure mode). Comparing against the live folder is the
    /// same staleness idiom the parse cache uses
    /// (<see cref="PickedProblemFolder.StoreParsed"/>).
    /// </para>
    ///
    /// <para>
    /// <b>Survives <see cref="Clear"/> on purpose.</b> A filter edit un-applies
    /// the <i>config</i> (Start re-gates), but the user has still filtered this
    /// corpus, so it must not re-gate Apply Mix — a settled ruling: a dirty
    /// filter draft does not revoke the mix gate.
    /// </para>
    /// </summary>
    public bool WasAppliedFor(int pickGeneration) => _appliedGeneration == pickGeneration;

    /// <summary>
    /// Drop the applied state — called when the user edits a filter control, so a
    /// half-edited set re-gates Start until the user applies again. The pick
    /// stamp is deliberately left standing (see <see cref="WasAppliedFor"/>).
    /// </summary>
    public void Clear() => Config = null;
}
