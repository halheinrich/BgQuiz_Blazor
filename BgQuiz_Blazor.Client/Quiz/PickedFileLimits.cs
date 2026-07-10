namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The caps the file picker enforces on a single pick of <see cref="PickedFile"/>s,
/// and the derived figures the help page states as prose.
///
/// <para>
/// These are one rule with two consumers — <c>Home.razor.cs</c> passes them to
/// <c>InputFileChangeEventArgs.GetMultipleFiles</c> / <c>IBrowserFile.OpenReadStream</c>
/// to <i>enforce</i> them, and <c>Help.razor</c> <i>displays</i> them — so they live
/// here rather than as private constants on the enforcing page. The megabyte figure
/// is <b>derived</b> from <see cref="MaxFileBytes"/>, never restated: raising the byte
/// cap moves the documented figure with it.
/// </para>
/// </summary>
internal static class PickedFileLimits
{
    /// <summary>
    /// Per-file size cap in bytes (50 MB) — mirrors the XG extractor's web-mode limit.
    /// Enforced at pick time by <c>IBrowserFile.OpenReadStream</c>, which throws once a
    /// file's stream exceeds it.
    /// </summary>
    internal const long MaxFileBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Upper bound on files accepted in a single pick. Enforced by
    /// <c>InputFileChangeEventArgs.GetMultipleFiles</c>, which throws past it.
    /// </summary>
    internal const int MaxFileCount = 500;

    /// <summary>
    /// <see cref="MaxFileBytes"/> expressed in whole mebibytes — the human-facing
    /// figure the help page renders. Derived, so page prose and enforced rule cannot
    /// drift.
    /// </summary>
    internal const long MaxFileMegabytes = MaxFileBytes / (1024 * 1024);
}
