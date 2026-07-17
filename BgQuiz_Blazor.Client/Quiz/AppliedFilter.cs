namespace BgQuiz_Blazor.Client.Quiz;

using XgFilter_Lib.Filtering;

/// <summary>
/// Per-app holder for the filter config the user has <i>deliberately applied</i>
/// on <c>Home</c> — the filter half of the quiz-start gate.
///
/// <para>
/// Lifetime: <b>Scoped</b> — in the WebAssembly client that resolves to one
/// instance per loaded app (one tab), the same lifetime as
/// <see cref="PickedProblemSet"/> and <see cref="QuizController"/>. It holds the
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
/// In-memory only: the applied state survives in-app navigation but is reset by
/// a full browser reload, which re-boots the WASM runtime. Persisting it across
/// reloads is a deferred phase and out of scope by design — reload-reset matches
/// <see cref="PickedProblemSet"/> and <see cref="QuizController"/>.
/// </para>
/// </summary>
internal sealed class AppliedFilter
{
    /// <summary>
    /// The applied filter config; <c>null</c> until the user applies one, and
    /// again after an edit clears it via <see cref="Clear"/>.
    /// </summary>
    public FilterConfig? Config { get; private set; }

    /// <summary>True once the user has deliberately applied a filter config.</summary>
    public bool IsApplied => Config is not null;

    /// <summary>Record <paramref name="config"/> as the deliberately-applied filter set.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
    public void Set(FilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Config = config;
    }

    /// <summary>
    /// Drop the applied state — called when the user edits a filter control, so a
    /// half-edited set re-gates Start until the user applies again.
    /// </summary>
    public void Clear() => Config = null;
}
