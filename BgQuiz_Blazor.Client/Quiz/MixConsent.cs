namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The per-app (Scoped, one-per-tab in WASM) consent bit for the weighted mix
/// — the state of the <b>"Mix applies"</b> checkbox, the sole activation
/// control (<c>SPEC-filtering.md</c> §5, Fork B). Checked means <i>the mix on
/// screen is in effect</i>: there is no committed copy of the mix, so what a
/// consented Start runs is <c>MixDraft.Build()</c> itself and screen and
/// effect cannot diverge.
///
/// <para>
/// <b>The checkbox is consent; the rows are choice</b> (the spec's §4 law
/// applied to the mix). The rows live in <c>MixDraft</c> and persist; this bit
/// deliberately does not: <see cref="Revoke"/> is called from
/// <c>Home.EndCurrentSetupAsync</c> — the start of every pick gesture, and
/// Clear — and a full reload resets it for free (Scoped state dies with the
/// app), so consent never outlives the setup it was given in. Navigation
/// within a setup changes nothing, which is why the bit lives here and never
/// in the panel — the panel dies on navigation and §4 says navigation changes
/// nothing.
/// </para>
///
/// <para>
/// <b>Only the user moves it.</b> The check gesture is gated (the filter must
/// be in effect <i>now</i> — Fork A, enforced by the host through the panel's
/// <c>CanActivate</c>), unchecking is always live, and the app flips the bit
/// in neither direction while a setup stands: auto-uncheck was explicitly
/// rejected (a control the app flips stops being consent — the spec's §5
/// records why), and a checked-but-invalid mix gates Start instead, with the
/// box still checked because it records intent.
/// </para>
/// </summary>
internal sealed class MixConsent
{
    /// <summary>
    /// Whether the on-screen mix is in effect — the "Mix applies" checkbox
    /// state. Effect follows from this <i>and</i> the draft: checked over a
    /// blank draft is vacuous consent (the blank mix builds
    /// <c>QuizMix.Empty</c>, the passthrough), so nothing downstream may read
    /// this bit alone as "a weighted run is coming".
    /// </summary>
    public bool Applies { get; private set; }

    /// <summary>
    /// Raised when the bit actually moves — a same-value <see cref="Set"/> is
    /// a no-op raising nothing. Subscribers (Home re-derives its start gate;
    /// the panel renders the box) must unsubscribe on dispose.
    /// </summary>
    public event Action? Changed;

    /// <summary>The checkbox gesture. Idempotent: setting the current value changes nothing and raises nothing.</summary>
    public void Set(bool value)
    {
        if (Applies == value) return;
        Applies = value;
        Changed?.Invoke();
    }

    /// <summary>
    /// End-of-setup reset (§4: your choices outlive the setup; your consent
    /// does not) — the one app-initiated move, distinct from the in-setup
    /// no-flips rule above because the setup it belonged to is over. Called
    /// from <c>Home.EndCurrentSetupAsync</c>; reload gets the same result by
    /// construction.
    /// </summary>
    public void Revoke() => Set(false);
}
