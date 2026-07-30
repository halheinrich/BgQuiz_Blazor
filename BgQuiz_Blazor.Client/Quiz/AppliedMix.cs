namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;

/// <summary>
/// The per-app (Scoped, one-per-tab in WASM) holder for the
/// <see cref="QuizMix"/> the user has <b>committed</b> on <c>Home</c> — the
/// mix sibling of <see cref="AppliedFilter"/>, completing the start gate's
/// composition half. <c>Home.razor</c> writes it from the mix panel's Apply
/// event (<c>OnMixApplied</c> → <see cref="Apply"/>) and reads
/// <see cref="Current"/> at Start.
///
/// <para>
/// <b>This holder stores no dirtiness.</b> The start gate's mix condition is
/// derived per render: the app-scoped draft (<see cref="MixDraft"/>) builds
/// and content-equals <see cref="Current"/> (<see cref="MixDraft.Matches"/>).
/// Committed and draft live in sibling Scoped services with identical
/// lifetimes, so the derived judgment can never outlive the state it judges —
/// the failure mode of the earlier stored <c>IsDirty</c> flag, whose edit
/// lived in the component-scoped panel and produced three wedge variants
/// (remove-last-row, finding AK's navigate-away, and the benign
/// navigate-back-over-committed case).
/// </para>
///
/// <para>
/// <b>Semantics differ from <see cref="AppliedFilter"/> deliberately.</b> The
/// mix is optional, and blank (<see cref="QuizMix.Empty"/>) is the valid
/// default — so there is no "never applied blocks Start" state; only a draft
/// that disagrees with the commitment gates (an edited, uncommitted mix would
/// silently diverge from what Start uses — the same hazard the filter gate
/// guards). The two start-gate halves therefore block by different
/// mechanisms, because their defaults differ: the filter blocks via
/// not-yet-applied (it has no valid default), the mix via draft≠committed
/// (passthrough is its valid default, so "never applied" can't be the gate).
/// </para>
///
/// <para>
/// In-memory only, reset on full reload — but unlike its sibling holders the
/// underlying choice survives the reload in localStorage
/// (<see cref="MixDraft.PersistAsync"/> on every commit), and the draft's
/// hydration re-offers it — gated by the derived rule until re-Applied — on
/// the next boot.
/// </para>
/// </summary>
internal sealed class AppliedMix
{
    /// <summary>
    /// The last committed mix; <see cref="QuizMix.Empty"/> (the inert
    /// passthrough default) until the user applies one.
    /// </summary>
    public QuizMix Current { get; private set; } = QuizMix.Empty;

    /// <summary>Commit <paramref name="mix"/> — Apply, Reset, and the last-row removal all land here (the latter two committing <see cref="QuizMix.Empty"/>).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="mix"/> is null.</exception>
    public void Apply(QuizMix mix)
    {
        ArgumentNullException.ThrowIfNull(mix);
        Current = mix;
    }

    /// <summary>
    /// Reset to the passthrough default — the committed-mix analogue of a
    /// fresh setup. Called from <c>Home.EndCurrentSetupAsync</c> — i.e. at the
    /// start of every pick gesture, and on Clear — beside
    /// <see cref="MixDraft.Discard"/>, so no committed mix silently survives
    /// the end of the setup it was built for: under a no-stats pick the mix
    /// must play no part in Start, and under an
    /// <see cref="StatsSaveCapability.Enabled"/> pick the re-mounted panel's
    /// hydration re-offers the persisted mix — gated by the derived rule
    /// against this now-passthrough holder. Does not touch localStorage — the
    /// stored mix survives for that hydration to re-offer.
    /// </summary>
    public void Reset() => Current = QuizMix.Empty;
}
