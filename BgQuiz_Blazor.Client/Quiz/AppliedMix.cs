namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;

/// <summary>
/// The per-app (Scoped, one-per-tab in WASM) holder for the
/// <see cref="QuizMix"/> the user has <b>committed</b> on <c>Home</c> — the
/// mix sibling of <see cref="AppliedFilter"/>, completing the start gate's
/// composition half. <c>Home.razor</c> writes it from the mix panel's events
/// (<c>OnMixApplied</c> → <see cref="Apply"/>, <c>OnMixDirty</c> →
/// <see cref="MarkDirty"/>, and <c>OnMixRestored</c> → a reconcile that marks
/// <see cref="MarkDirty"/> only on a fresh load); <c>Home</c> reads
/// <see cref="Current"/> at Start and <see cref="IsDirty"/> in its gate.
///
/// <para>
/// <b>Semantics differ from <see cref="AppliedFilter"/> deliberately.</b> The
/// mix is optional, and blank (<see cref="QuizMix.Empty"/>) is the valid
/// default — so there is no "never applied blocks Start" state; only
/// <see cref="IsDirty"/> gates (an edited, uncommitted mix would silently
/// diverge from what Start uses — the same hazard the filter gate guards).
/// The panel's localStorage restore does <b>not</b> adopt here: like the
/// filter panel it re-shows the persisted rows without committing them.
/// <c>Home</c> <i>reconciles</i> the restore against this holder — a non-blank
/// restore marks <see cref="IsDirty"/> only when <see cref="Current"/> is still
/// passthrough (a fresh load, so Start gates until the user re-Applies); when a
/// committed mix already survives here (navigate-back), the restore is left
/// untouched, so no re-Apply is forced. The two start-gate halves therefore
/// block by different mechanisms, because their defaults differ: the filter
/// blocks via not-yet-applied (it has no valid default), the mix via dirty
/// (passthrough is its valid default, so "never applied" can't be the gate).
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
/// underlying choice survives the reload in localStorage, and the panel
/// re-shows it (as a dirty, uncommitted mix) on the next boot.
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

    /// <summary>Commit <paramref name="mix"/> (Apply or Reset) and clear any dirty state.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="mix"/> is null.</exception>
    public void Apply(QuizMix mix)
    {
        ArgumentNullException.ThrowIfNull(mix);
        Current = mix;
        IsDirty = false;
    }

    /// <summary>Record an uncommitted edit; cleared by the next <see cref="Apply"/>.</summary>
    public void MarkDirty() => IsDirty = true;

    /// <summary>
    /// Reset to the passthrough default and clear any dirty state — the
    /// committed-mix analogue of a fresh setup. Called on every folder pick
    /// (<c>Home.ApplyPickOutcomeAsync</c>) so no committed mix silently survives
    /// a re-pick: under a no-stats pick the mix must play no part in Start, and
    /// under an <see cref="StatsSaveCapability.Enabled"/> pick the re-mounted
    /// panel re-offers the persisted config as dirty (reconciled against this
    /// now-passthrough state). Does not touch localStorage — the stored mix
    /// survives for the panel to re-offer.
    /// </summary>
    public void Reset()
    {
        Current = QuizMix.Empty;
        IsDirty = false;
    }
}
