namespace BgQuiz_Blazor.Client.Quiz;

using BgFolderAccess_Razor;

/// <summary>
/// The one home for user-facing folder-pick wording shared across more than one
/// surface — the folder-pick sibling of <see cref="MixDisplay"/>. Keeping such a
/// phrase here, rather than as a literal per render site, is what stops the two
/// renderings of the same fact from drifting apart.
///
/// <para>
/// Deliberately <b>not</b> a home for every string on the pick surface: a phrase
/// earns a constant here only once a second surface renders it. Help's prose
/// stays prose — it explains the same rules at length and in its own voice, so
/// pinning it to these constants would fight the page rather than serve it. The
/// one exception is <see cref="SupportedBrowsers"/>, which Help's "before you
/// start" lead renders <i>verbatim</i> rather than restating: it is a single
/// sentence of fact, not an explanation, and the two surfaces must agree
/// exactly — see its own remarks.
/// </para>
///
/// <para>
/// <b>Never quote a browser's prompt text.</b> Chrome and Edge word the File
/// System Access prompts differently, and Edge interpolates the folder's own
/// name into the write prompt, so any string claiming to be what the user will
/// read is wrong somewhere. Wording here describes the <i>grant</i> being asked
/// for, hedged ("your browser asks whether…"), and asserts no browser's exact
/// string.
/// </para>
/// </summary>
internal static class FolderPickDisplay
{
    /// <summary>
    /// Which browsers and devices the folder pick actually works on, in one
    /// sentence pair that reads correctly standing alone (Home, beside the pick
    /// button) and inside a prerequisites list (Help's "before you start" lead).
    ///
    /// <para>
    /// It exists because the pick is a <b>dead entry point</b> where it isn't
    /// supported: on a phone the <c>webkitdirectory</c> fallback is weak to
    /// absent, so the button may raise nothing at all and leave the visitor on an
    /// unchanged page with no account of why — the same silence the cancelled-pick
    /// notice was added to end, except that no code path here ever runs to report
    /// it. Only a statement made <i>before</i> the gesture can cover that case.
    /// </para>
    ///
    /// <para>
    /// Home renders it <b>ungated by the File System Access probe</b>, unlike the
    /// two-step permission guidance: the readers it is written for are exactly the
    /// ones the probe excludes. It hides once a folder is held (the pick worked;
    /// the caution is moot), the same "no folder held" window its neighbour uses.
    /// </para>
    ///
    /// <para>
    /// The middle clause is hedged on purpose. A desktop non-Chromium browser is
    /// not broken — the fallback picker works and the quiz runs, it just cannot
    /// write, which is the <see cref="FolderWriteCapability.BrowserUnsupported"/>
    /// rung. Only the phone case is genuinely "may not work at all", and it says
    /// <i>may</i>: this asserts no behavior of a browser we cannot observe from
    /// here, the same discipline as the never-quote-a-prompt rule above.
    /// </para>
    /// </summary>
    internal const string SupportedBrowsers =
        "BgQuiz supports desktop Chrome and Edge. Other desktop browsers can "
        + "usually run the quiz but save nothing; on phones, choosing a folder "
        + "may not work at all.";

    /// <summary>
    /// What the absence of write access to the picked folder actually costs, in
    /// one clause that reads correctly mid-sentence on either surface that
    /// renders it: Home's pre-pick permission guidance (forward-looking —
    /// "without it, …") and Home's <see cref="FolderWriteCapability.PermissionDenied"/>
    /// outcome notice (after the fact — "the quiz runs, but …").
    ///
    /// <para>
    /// The point it exists to make: the loss is not an abstract "stats file",
    /// it is the lifetime record of <i>which problems the user finds hard</i> —
    /// the thing the weighted mix and the lifetime scoreboard are built from.
    /// The bare "stats won't be saved" this replaced understated that.
    /// </para>
    /// </summary>
    internal const string WriteAccessConsequence =
        "BgQuiz can't keep its lifetime record of which problems give you difficulty";

    /// <summary>
    /// The cause-agnostic premise every "no write access" surface opens with —
    /// the lead-in that <see cref="WriteAccessConsequence"/> and the
    /// saved-filters load-only reason each complete in their own words.
    ///
    /// <para>
    /// <b>It deliberately does not say the user declined.</b>
    /// <see cref="FolderWriteCapability.PermissionDenied"/> has two causes and
    /// cannot tell them apart: the user answered no, <i>or</i> the readwrite
    /// request auto-denied because the picker had already consumed the transient
    /// user activation (observed on some Chromium versions — see
    /// <c>folderAccess.js</c>'s <c>beginPick</c>). On that second path no
    /// second prompt is ever shown, so "you declined write access" attributes a
    /// decision the user never made. Same discipline as the cancelled-pick
    /// notice: true under both causes, and non-accusatory.
    /// </para>
    ///
    /// <para>
    /// Rendered by two surfaces — Home's PermissionDenied outcome notice and
    /// Home's <c>SavedFiltersDisabledReason</c> — which is exactly the bar for
    /// living here. It earned the constant the hard way: the two carried the
    /// same false "you declined" premise and one was corrected without the
    /// other, which is the drift this class exists to prevent.
    /// </para>
    /// </summary>
    internal const string WriteAccessNotGranted = "Write access wasn't granted";
}
