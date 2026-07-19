namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The caps the folder pick enforces on its <see cref="PickedFile"/>s, and the
/// derived figures the help page states as prose.
///
/// <para>
/// These are one rule with two consumers — <see cref="JsFolderAccess"/>
/// <i>enforces</i> them against the pick's metadata before any bytes cross the
/// interop boundary, and <c>Help.razor</c> <i>displays</i> them — so they live
/// here rather than as private constants on the enforcing type. The megabyte
/// figure is <b>derived</b> from <see cref="MaxFileBytes"/>, never restated:
/// raising the byte cap moves the documented figure with it.
/// </para>
/// </summary>
internal static class PickedFileLimits
{
    /// <summary>
    /// Per-file size cap in bytes (50 MB) — mirrors the XG extractor's web-mode limit.
    /// Enforced at pick time by <see cref="JsFolderAccess"/>: checked against the
    /// enumerated metadata up front, and re-asserted as the
    /// <c>IJSStreamReference.OpenReadStreamAsync</c> <c>maxAllowedSize</c> on the
    /// actual transfer.
    /// </summary>
    internal const long MaxFileBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Upper bound on problem files accepted in a single folder pick. Enforced by
    /// <see cref="JsFolderAccess"/>, which fails the whole pick past it.
    /// </summary>
    internal const int MaxFileCount = 500;

    /// <summary>
    /// <see cref="MaxFileBytes"/> expressed in whole mebibytes — the human-facing
    /// figure the help page renders. Derived, so page prose and enforced rule cannot
    /// drift.
    /// </summary>
    internal const long MaxFileMegabytes = MaxFileBytes / (1024 * 1024);
}
