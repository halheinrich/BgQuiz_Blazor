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
    /// What declining write access to the picked folder actually costs, in one
    /// clause that reads correctly mid-sentence on either surface that renders
    /// it: Home's in-flight pick guidance (forward-looking — "decline and …")
    /// and Home's <see cref="StatsSaveCapability.PermissionDenied"/> outcome
    /// notice (after the fact — "the quiz runs, but …").
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
}
