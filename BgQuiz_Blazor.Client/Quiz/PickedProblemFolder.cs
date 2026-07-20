namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;

/// <summary>
/// One picked XG-format file, fully buffered into memory. The bytes are read
/// out of the browser once, at pick time, and never leave it — this is the
/// in-memory payload the quiz parses locally.
/// </summary>
/// <param name="FileName">
/// The original file name <i>including</i> its extension (<c>.xg</c> /
/// <c>.xgp</c>). The extension is required: the producer's <c>DecisionId</c>
/// stamping discriminates the format from it. See <c>XgFileStream.FileName</c>.
/// </param>
/// <param name="Bytes">
/// The complete file contents. A fresh <c>MemoryStream(Bytes)</c> is minted for
/// each enumeration so the source stays re-iterable (the stream iterator reads a
/// stream exactly once, forward).
/// </param>
internal sealed record PickedFile(string FileName, byte[] Bytes);

/// <summary>
/// Per-app holder for the user's picked problem-set folder: its top-level
/// <c>.xg</c> / <c>.xgp</c> files (buffered) plus the pick-time
/// <see cref="StatsSaveCapability"/> verdict.
///
/// <para>
/// Lifetime: <b>Scoped</b> — in the WebAssembly client that resolves to one
/// instance per loaded app (one tab), the same lifetime as
/// <see cref="QuizController"/>. It shuttles the picked files from
/// <c>Home.razor</c> (the writer) to the <see cref="ProblemSetSourceFactory"/>
/// registered in <c>Program.cs</c> (the reader). Carrying
/// <see cref="Capability"/> here rather than in a component field keeps Home's
/// pick-time stats notice alive across navigate-back — the same
/// holder-vs-field rationale as the start gate.
/// </para>
///
/// <para>
/// The files are buffered byte arrays rather than open handles: bytes are read
/// once at pick time, so the source can re-enumerate (Restart) by minting
/// fresh <c>MemoryStream</c>s. The folder's browser-side directory handle is
/// <i>not</i> here — it lives in the JS module behind
/// <see cref="IFolderAccess"/> (handles can't cross the interop boundary),
/// in the <i>picked</i> slot this holder mirrors.
/// </para>
///
/// <para>
/// In-memory only: the pick survives in-app navigation (the holder is per-app)
/// but is reset by a full browser reload, which re-boots the WASM runtime and
/// constructs a fresh instance. Persisting picks across reloads is a deferred
/// phase and out of scope by design — reload-reset matches
/// <see cref="QuizController"/>.
/// </para>
/// </summary>
internal sealed class PickedProblemFolder
{
    /// <summary>The picked folder's top-level problem files; empty until a folder is picked.</summary>
    public IReadOnlyList<PickedFile> Files { get; private set; } = [];

    /// <summary>The picked folder's leaf name; null until a folder is picked.</summary>
    public string? FolderName { get; private set; }

    /// <summary>
    /// The pick-time stats-saving verdict for this folder. Meaningful only
    /// while <see cref="HasFiles"/>; defaults to
    /// <see cref="StatsSaveCapability.BrowserUnsupported"/> when nothing is
    /// picked (no notice renders then, so the value is never shown).
    /// </summary>
    public StatsSaveCapability Capability { get; private set; } = StatsSaveCapability.BrowserUnsupported;

    /// <summary>True once a folder with at least one problem file has been picked.</summary>
    public bool HasFiles => Files.Count > 0;

    /// <summary>
    /// Monotonic pick counter, bumped by every <see cref="Set"/> and
    /// <see cref="Clear"/>. The parse cache's staleness key: a parse begun
    /// against one pick must not land in the cache once another pick has
    /// superseded it (see <see cref="StoreParsed"/>), and a consumer holding
    /// a generation can tell whether <see cref="ParsedDecisions"/> still
    /// describes <i>its</i> files.
    /// </summary>
    public int PickGeneration { get; private set; }

    /// <summary>
    /// The parse-once cache: every decision parsed from <see cref="Files"/>
    /// with <b>no filters applied</b>, or null when the current pick has not
    /// been parsed yet. Living on the holder makes cache lifecycle equal pick
    /// lifecycle by construction — <see cref="Set"/> and <see cref="Clear"/>
    /// null it (freeing the old parse immediately), so no separate
    /// invalidation wiring exists to forget. Unfiltered so any filter config
    /// reuses it: filters re-apply per Start over the cached decisions
    /// (<c>CachedProblemSetSource</c>), which the filter contracts make
    /// exactly equivalent to filtering during the parse. Written only by
    /// <c>CachedProblemSetSource</c> via <see cref="StoreParsed"/>.
    /// </summary>
    public IReadOnlyList<BgDecisionData>? ParsedDecisions { get; private set; }

    /// <summary>
    /// Store the unfiltered parse of the pick identified by
    /// <paramref name="pickGeneration"/>. Silently dropped when that pick has
    /// been superseded (the generation no longer matches): the in-flight quiz
    /// that parsed the old files keeps its own reference, but a stale parse
    /// must never masquerade as the cache of the <i>new</i> pick.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="decisions"/> is null.</exception>
    public void StoreParsed(int pickGeneration, IReadOnlyList<BgDecisionData> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        if (pickGeneration != PickGeneration) return;
        ParsedDecisions = decisions;
    }

    /// <summary>
    /// A short, human-readable label for the picked folder, derived from
    /// <see cref="FolderName"/> and <see cref="Files"/>: e.g.
    /// <c>"'MyMatches' — 12 problem files"</c>, or <c>null</c> when nothing is
    /// picked. Single source of truth for how a pick describes itself, so a
    /// page re-instantiated by in-app navigation re-derives the same label
    /// from this persisted holder rather than from a transient component field.
    /// </summary>
    public string? Summary => Files.Count switch
    {
        0 => null,
        1 => $"'{FolderName}' — 1 problem file",
        var n => $"'{FolderName}' — {n} problem files",
    };

    /// <summary>
    /// Replace the pick with <paramref name="files"/> from
    /// <paramref name="folderName"/>. Invalidates the parse cache
    /// (<see cref="ParsedDecisions"/>) and bumps <see cref="PickGeneration"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="folderName"/> or <paramref name="files"/> is null.</exception>
    public void Set(string folderName, IReadOnlyList<PickedFile> files, StatsSaveCapability capability)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        ArgumentNullException.ThrowIfNull(files);
        FolderName = folderName;
        Files = files;
        Capability = capability;
        ParsedDecisions = null;
        PickGeneration++;
    }

    /// <summary>
    /// Clear the pick (Clear affordance, or after a failed read). Invalidates
    /// the parse cache and bumps <see cref="PickGeneration"/>, so the parsed
    /// decisions (and, transitively, the picked bytes) free immediately.
    /// </summary>
    public void Clear()
    {
        Files = [];
        FolderName = null;
        Capability = StatsSaveCapability.BrowserUnsupported;
        ParsedDecisions = null;
        PickGeneration++;
    }
}
