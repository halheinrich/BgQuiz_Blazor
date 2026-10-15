namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
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
    /// Upper bound on <c>.xg</c> match files taken in one draw of a folder — a
    /// pick's or a resample's. One match file averages ~120 decisions, so it is
    /// the expensive format (issue halheinrich/backgammon#59), and its limit is
    /// set by measurement (<c>SPEC-filtering.md</c> §4, "The sample, the limits
    /// and Resample"; issue halheinrich/backgammon#403): the largest N whose
    /// one-rule transition passes with margin, 500 if 500 does. It does.
    ///
    /// <para>
    /// <b>The measurement</b> (2026-10-14, v1.12.2 Arc 5 leg 3). The trimmed
    /// AOT publish, driven in Playwright's Chromium 149.0.7827.55 through the
    /// transition — a quiz on a folder of 1,000 match files (a seed-42 sample
    /// of a real corpus, 134.9 MB) drawn to 500, two resamples (the first
    /// ending the quiz), Start, End quiz, Restart — with the WebAssembly heap
    /// extent read at every step. Two runs: peak 1,269.9 MB and 1,265.6 MB
    /// (1,075.8 MB and 1,169.0 MB after the first parse), against a pass mark
    /// of 1,536 MB of the runtime's 2,048 MB; no runtime error; each
    /// resample's count was of its own sample (202,142, then 205,238, then
    /// 204,187 decisions in the first run) and Restart kept the second's. A
    /// 24-thread i9-12900K under Windows 11, with unrelated analyses holding
    /// 30–80% of the CPU throughout, which moves timings, not the heap. No
    /// lower N was measured: the rule stops at the first that passes.
    /// </para>
    /// </summary>
    internal const int MaxXgFileCount = 500;

    /// <summary>
    /// Upper bound on <c>.xgp</c> position files taken from a single folder pick.
    /// Raised to 2000 by the per-format ruling (issue halheinrich/backgammon#59): one position file is
    /// one decision, so a real position library costs a fraction of the same
    /// number of match files — and 500 hard-blocked exactly that use.
    /// </summary>
    internal const int MaxXgpFileCount = 2000;

    /// <summary>
    /// The per-extension file-count caps: the pick's problem-file kinds and what
    /// each one admits, <b>in this order: <c>.xg</c>, then <c>.xgp</c></b> —
    /// match files first, the format a folder is most often made of, and the
    /// order every per-type report reads them in. File count is only a cost
    /// proxy <i>within</i> one format, so each extension truncates at its own
    /// cap independently and a mixed folder can admit its full quota of both.
    ///
    /// <para>
    /// <b>The order is a stated fact, so the table is an array.</b>
    /// <see cref="FolderPickLimits"/> keeps its table in the order it is handed
    /// ("the ctor's order, by position"), and that order reaches the user: the
    /// lib hands the table to its JS module in it, and the module's left-behind
    /// report — the truncation notice's lines — follows it. A dictionary
    /// documents no enumeration order at all, so an array, whose order is its
    /// positions, is what lets this sentence be true. Immutable, because it is
    /// one shared instance (halheinrich/backgammon#273's collection rider).
    /// </para>
    ///
    /// <para>
    /// This table is the values half of the registered
    /// <see cref="FolderPickLimits"/> (built from it once, in
    /// <c>Program.cs</c>), through which the lib hands the whole table to its
    /// JS module on every enumeration — the module derives <i>both</i> jobs
    /// from it (which names count as problem files, and how many of each to
    /// take) and keeps no copy. Each reported kind's
    /// <see cref="SampledKind.MaxFileCount"/> is derived lib-side from that
    /// same enforced instance, so the figure a notice states is by construction
    /// the figure the pick applied.
    /// </para>
    /// </summary>
    internal static ImmutableArray<KeyValuePair<string, int>> MaxFileCounts { get; } =
    [
        new(XgExtension, MaxXgFileCount),
        new(XgpExtension, MaxXgpFileCount),
    ];

    /// <summary>
    /// <see cref="MaxFileBytes"/> expressed in whole mebibytes — the human-facing
    /// figure the help page renders. Derived, so page prose and enforced rule cannot
    /// drift.
    /// </summary>
    internal const long MaxFileMegabytes = MaxFileBytes / (1024 * 1024);
}
