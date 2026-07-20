namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;

/// <summary>
/// The per-app (Scoped, one-per-tab in WASM) holder for the
/// <see cref="QuizMix"/> the user has <b>committed</b> on <c>Home</c> — the
/// mix sibling of <see cref="AppliedFilter"/>, completing the start gate's
/// composition half. <c>Home.razor</c> writes it from the mix panel's events
/// (<c>OnMixApplied</c> / <c>OnMixRestored</c> → <see cref="Apply"/>,
/// <c>OnMixDirty</c> → <see cref="MarkDirty"/>); <c>Home</c> reads
/// <see cref="Current"/> at Start and <see cref="IsDirty"/> in its gate.
///
/// <para>
/// <b>Semantics differ from <see cref="AppliedFilter"/> deliberately.</b> The
/// mix is optional, and blank (<see cref="QuizMix.Empty"/>) is the valid
/// default — so there is no "never applied blocks Start" state; only
/// <see cref="IsDirty"/> gates (an edited, uncommitted mix would silently
/// diverge from what Start uses — the same hazard the filter gate guards).
/// The panel's localStorage restore also <i>adopts</i> here (via its restored
/// event): a persisted mix is by construction previously-applied, so on
/// navigate-back or reload the holder and the rendered panel agree without a
/// re-Apply.
/// </para>
///
/// <para>
/// <see cref="QuizMix"/> compares by reference (it wraps a list) — never
/// compare instances for equality; branch on
/// <see cref="QuizMix.IsPassthrough"/> or compare <see cref="QuizMix.Entries"/>
/// content.
/// </para>
///
/// <para>
/// In-memory only, reset on full reload — but unlike its sibling holders the
/// underlying choice survives the reload in localStorage, and the panel's
/// restore re-adopts it on the next boot.
/// </para>
/// </summary>
internal sealed class AppliedMix
{
    /// <summary>
    /// The last committed mix; <see cref="QuizMix.Empty"/> (the inert
    /// passthrough default) until the user applies one or the panel restores
    /// one.
    /// </summary>
    public QuizMix Current { get; private set; } = QuizMix.Empty;

    /// <summary>
    /// True while the panel holds edits the user hasn't committed — the mix
    /// half of the start gate: Start stays disabled so it can never run a mix
    /// that differs from what the panel shows.
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>Commit <paramref name="mix"/> (Apply, Reset, or the panel's restore) and clear any dirty state.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="mix"/> is null.</exception>
    public void Apply(QuizMix mix)
    {
        ArgumentNullException.ThrowIfNull(mix);
        Current = mix;
        IsDirty = false;
    }

    /// <summary>Record an uncommitted edit; cleared by the next <see cref="Apply"/>.</summary>
    public void MarkDirty() => IsDirty = true;
}
