namespace BgQuiz_Blazor.Client.Quiz;

using BgFolderAccess_Razor;

/// <summary>
/// The caps BgQuiz asks the folder pick to enforce on its
/// <see cref="PickedFile"/>s, and the derived figures the help page states as
/// prose. Host <b>policy</b>, not machinery: enforcement lives in
/// BgFolderAccess_Razor (its <see cref="FolderPickLimits"/> carries these
/// values into the pick — <c>Program.cs</c> builds the one registered instance
/// from this table — and its JS module derives both of its jobs from the table
/// it is handed, holding no copy of its own), while the numbers encode this
/// app's cost model and stay here.
///
/// <para>
/// One rule, several consumers — the pick <i>enforces</i> it,
/// <c>Home.razor</c> reports what a truncation left behind, and
/// <c>Help.razor</c> <i>documents</i> it — so the values live here rather
/// than as literals per site. The megabyte figure is <b>derived</b> from
/// <see cref="MaxFileBytes"/>, never restated: raising the byte cap moves the
/// documented figure with it. (The lib derives the same figure for its own
/// error message via <see cref="FolderPickLimits.MaxFileMegabytes"/> — from
/// the same registered value, so the two renderings cannot disagree.)
/// </para>
/// </summary>
internal static class PickedFileLimits
{
    /// <summary>The match-file extension, lower-case and dot-bearing — a key of <see cref="MaxFileCounts"/>.</summary>
    internal const string XgExtension = ".xg";

    /// <summary>The position-file extension, lower-case and dot-bearing — a key of <see cref="MaxFileCounts"/>.</summary>
    internal const string XgpExtension = ".xgp";

    /// <summary>
    /// Per-file size cap in bytes (50 MB) — mirrors the XG extractor's web-mode
    /// limit. Enforced at pick time by BgFolderAccess_Razor's
    /// <see cref="JsFolderAccess"/> (checked against the enumerated metadata up
    /// front, re-asserted on the actual transfer), which receives it through the
    /// registered <see cref="FolderPickLimits"/>.
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
    /// each one admits, in the order any per-type report reads them. File count
    /// is only a cost proxy <i>within</i> one format, so each extension
    /// truncates at its own cap independently and a mixed folder can admit its
    /// full quota of both.
    ///
    /// <para>
    /// This table is the values half of the registered
    /// <see cref="FolderPickLimits"/> (built from it once, in
    /// <c>Program.cs</c>), through which the lib hands the whole table to its
    /// JS module on every enumeration — the module derives <i>both</i> jobs
    /// from it (which names count as problem files, and how many of each to
    /// take) and keeps no copy. A reported truncation's
    /// <see cref="PickTruncation.MaxFileCount"/> is derived lib-side from that
    /// same enforced instance, so the figure a notice states is by construction
    /// the figure the pick applied.
    /// </para>
    /// </summary>
    internal static IReadOnlyDictionary<string, int> MaxFileCounts { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [XgExtension] = MaxXgFileCount,
            [XgpExtension] = MaxXgpFileCount,
        }.AsReadOnly();

    /// <summary>
    /// <see cref="MaxFileBytes"/> expressed in whole mebibytes — the human-facing
    /// figure the help page renders. Derived, so page prose and enforced rule cannot
    /// drift.
    /// </summary>
    internal const long MaxFileMegabytes = MaxFileBytes / (1024 * 1024);
}
