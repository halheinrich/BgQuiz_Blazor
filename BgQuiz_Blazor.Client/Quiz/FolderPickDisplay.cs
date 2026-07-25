namespace BgQuiz_Blazor.Client.Quiz;

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
/// pinning it to these constants would fight the page rather than serve it.
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
    /// What the absence of write access to the picked folder actually costs, in
    /// one clause that reads correctly mid-sentence on either surface that
    /// renders it: Home's in-flight pick guidance (forward-looking — "without
    /// it, …") and Home's <see cref="StatsSaveCapability.PermissionDenied"/>
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
    /// <see cref="StatsSaveCapability.PermissionDenied"/> has two causes and
    /// cannot tell them apart: the user answered no, <i>or</i> the readwrite
    /// request auto-denied because the picker had already consumed the transient
    /// user activation (observed on some Chromium versions — see
    /// <c>folderAccess.js</c>'s <c>pickDirectory</c>). On that second path no
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
