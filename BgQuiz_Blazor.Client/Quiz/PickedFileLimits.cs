namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The caps the folder pick enforces on its <see cref="PickedFile"/>s, and the
/// derived figures the help page states as prose.
///
/// <para>
/// These are one rule with several consumers — <c>folderAccess.js</c>
/// <i>enforces</i> them against the pick's metadata before any bytes cross the
/// interop boundary (the count caps are handed to it as
/// <see cref="MaxFileCounts"/>, so the module holds no copy of its own),
/// <c>Home.razor</c> reports what a truncation left behind, and
/// <c>Help.razor</c> <i>documents</i> them — so they live here rather than as
/// private constants on the enforcing type. The megabyte figure is
/// <b>derived</b> from <see cref="MaxFileBytes"/>, never restated: raising the
/// byte cap moves the documented figure with it.
/// </para>
/// </summary>
internal static class PickedFileLimits
{
    /// <summary>The match-file extension, lower-case and dot-bearing — a key of <see cref="MaxFileCounts"/>.</summary>
    internal const string XgExtension = ".xg";

    /// <summary>The position-file extension, lower-case and dot-bearing — a key of <see cref="MaxFileCounts"/>.</summary>
    internal const string XgpExtension = ".xgp";

    /// <summary>
    /// Per-file size cap in bytes (50 MB) — mirrors the XG extractor's web-mode limit.
    /// Enforced at pick time by <see cref="JsFolderAccess"/>: checked against the
    /// enumerated metadata up front, and re-asserted as the
    /// <c>IJSStreamReference.OpenReadStreamAsync</c> <c>maxAllowedSize</c> on the
    /// actual transfer.
    /// </summary>
    internal const long MaxFileBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Upper bound on <c>.xg</c> match files taken from a single folder pick.
    /// Unchanged at 500 by the per-format ruling (issue #59): one match file
    /// averages ~120 decisions, so it is the expensive format and keeps the
    /// original cap.
    /// </summary>
    internal const int MaxXgFileCount = 500;

    /// <summary>
    /// Upper bound on <c>.xgp</c> position files taken from a single folder pick.
    /// Raised to 2000 by the per-format ruling (issue #59): one position file is
    /// one decision, so a real position library costs a fraction of the same
    /// number of match files — and 500 hard-blocked exactly that use.
    /// </summary>
    internal const int MaxXgpFileCount = 2000;

    /// <summary>
    /// The per-extension file-count caps: the pick's problem-file kinds and what
    /// each one admits, in the order any per-type report reads them.
    ///
    /// <para>
    /// <b>The whole table crosses the interop boundary</b> — <c>folderAccess.js</c>
    /// is handed it on every enumeration and derives <i>both</i> jobs from it: which
    /// names count as problem files, and how many of each to take. File count is
    /// only a cost proxy <i>within</i> one format, so each extension truncates at
    /// its own cap independently and a mixed folder can admit its full quota of
    /// both. Keeping the table here rather than in the module is what stops the
    /// two languages from disagreeing about either job.
    /// </para>
    /// </summary>
    internal static IReadOnlyDictionary<string, int> MaxFileCounts { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [XgExtension] = MaxXgFileCount,
            [XgpExtension] = MaxXgpFileCount,
        }.AsReadOnly();

    /// <summary>
    /// The cap applied to <paramref name="extension"/> — the lookup behind
    /// <see cref="PickTruncation.MaxFileCount"/>, so a reported truncation states
    /// the same figure the pick enforced.
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// <paramref name="extension"/> is not a problem-file extension. Unreachable
    /// from a truncation report: the caps table is what taught the JS module which
    /// extensions exist, so it can only report back a key from this table.
    /// </exception>
    internal static int MaxFileCountFor(string extension) => MaxFileCounts[extension];

    /// <summary>
    /// <see cref="MaxFileBytes"/> expressed in whole mebibytes — the human-facing
    /// figure the help page renders. Derived, so page prose and enforced rule cannot
    /// drift.
    /// </summary>
    internal const long MaxFileMegabytes = MaxFileBytes / (1024 * 1024);
}
