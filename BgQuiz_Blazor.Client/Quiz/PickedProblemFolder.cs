namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using BgFolderAccess_Razor;
using XgFilter_Lib;

/// <summary>
/// Per-app holder for the user's picked problem-set folder: its top-level
/// <c>.xg</c> / <c>.xgp</c> files (buffered) plus the two pick-time verdicts
/// about them — the <see cref="FolderWriteCapability"/> and the per-kind
/// report of the <see cref="Sample"/>, which says what the count caps left
/// unread. The
/// <see cref="PickedFile"/>s are BgFolderAccess_Razor's: extension-bearing
/// names (the producer's <c>DecisionId</c> stamping discriminates the format
/// from them — see <c>XgFileStream.FileName</c>) over fully-buffered,
/// immutable bytes, which <see cref="PickedFile.OpenRead"/> reads afresh for
/// each enumeration so the source stays re-iterable.
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
/// The files are buffered bytes rather than open handles: bytes are read once
/// at pick time, so the source can re-enumerate (Restart) by opening a fresh
/// read of them each time. The folder's browser-side directory handle is
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
///
/// <para>
/// <b>Every collection it holds is immutable</b> (halheinrich/backgammon#273's
/// collection rider): the sample is the pick outcome's own immutable record,
/// and the parse cache's decisions are an immutable array too
/// (<see cref="ParsedProblemSet.Decisions"/>). A holder that kept whatever
/// list it was handed would hand a caller's live list back out behind a
/// read-only interface, and the parse cache used to be exactly that — the
/// parsing source's own <see cref="List{T}"/>.
/// </para>
/// </summary>
internal sealed class PickedProblemFolder
{
    /// <summary>The picked folder's top-level problem files — <see cref="Sample"/>'s; empty until a folder is picked.</summary>
    public ImmutableArray<PickedFile> Files => Sample.Files;

    /// <summary>The picked folder's leaf name; null until a folder is picked.</summary>
    public string? FolderName { get; private set; }

    /// <summary>
    /// The pick-time stats-saving verdict for this folder. Meaningful only
    /// while <see cref="HasFiles"/>; defaults to
    /// <see cref="FolderWriteCapability.BrowserUnsupported"/> when nothing is
    /// picked (no notice renders then, so the value is never shown).
    /// </summary>
    public FolderWriteCapability Capability { get; private set; } = FolderWriteCapability.BrowserUnsupported;

    /// <summary>
    /// The pick's draw of the folder (BgFolderAccess_Razor's
    /// <see cref="FolderSample"/>): the admitted files, buffered, and the
    /// per-kind report of how many matching files of each kind the draw found
    /// and admitted (issue halheinrich/backgammon#59). Held here for the same
    /// reason as <see cref="Capability"/>: the report is a fact about the
    /// folder being <i>held</i>, not about the gesture that fetched it, so
    /// Home's notice must survive navigate-back — which a component field
    /// would not. The no-folder outcomes (cancelled, empty) keep their
    /// per-visit flags on the page precisely because they describe a gesture
    /// that left nothing behind to describe.
    /// </summary>
    public FolderSample Sample { get; private set; } = NoSample;

    /// <summary>The sample held while no folder is: no files, and no kinds reported.</summary>
    private static readonly FolderSample NoSample = new(Files: [], Kinds: []);

    /// <summary>True once a folder with at least one problem file has been picked.</summary>
    public bool HasFiles => !Files.IsEmpty;

    /// <summary>
    /// Monotonic pick counter, bumped by every <see cref="Set"/> and
    /// <see cref="Clear"/>. The parse cache's staleness key: a parse begun
    /// against one pick must not land in the cache once another pick has
    /// superseded it (see <see cref="StoreParsed"/>), and a consumer holding
    /// a generation can tell whether <see cref="Parsed"/> still
    /// describes <i>its</i> files.
    /// </summary>
    public int PickGeneration { get; private set; }

    /// <summary>
    /// <b>The identity of the pick this holder currently describes</b> — a token
    /// with no content whatsoever, whose only meaning is that it is or is not
    /// the same object as one held earlier. Replaced exactly where
    /// <see cref="PickGeneration"/> bumps (<see cref="Set"/> and
    /// <see cref="Clear"/>), so token identity and generation name the same
    /// pick by construction.
    ///
    /// <para>
    /// It exists for <c>Home</c>'s dismissible pick-outcome notices
    /// (<see cref="QuizNoticeDismissal"/>), which need to distinguish "the user
    /// dismissed the notice about <i>this</i> pick" from "the notice is off": a
    /// re-pick is a new occurrence and must show its notices fresh, while
    /// navigating away and back must not resurrect a dismissal.
    /// </para>
    ///
    /// <para>
    /// Deliberately an opaque object rather than the generation counter it
    /// shadows: the dismissal holder keys every notice by
    /// <see cref="object.ReferenceEquals"/> identity — one rule, one kind of
    /// token (see <see cref="QuizStatsStore.StatusOccurrence"/>, the precedent)
    /// — and a boxed <see langword="int"/> would be a token whose <i>value</i>
    /// invites comparison by a second rule.
    /// </para>
    /// </summary>
    public object PickOccurrence { get; private set; } = new();

    /// <summary>
    /// The parse-once cache: the one completed parse of <see cref="Files"/>
    /// with <b>no filters applied</b> — its decisions and, inseparably, the
    /// <see cref="SourceReport"/> of the walk that produced them, naming the
    /// files that could not be read (halheinrich/backgammon#368) — or null
    /// when the current pick has not been parsed yet. Living on the holder
    /// makes cache lifecycle equal pick lifecycle by construction —
    /// <see cref="Set"/> and <see cref="Clear"/> null it (freeing the old
    /// parse immediately), so no separate invalidation wiring exists to
    /// forget, and the rejection record is retired with the selection it
    /// describes and by nothing else. Unfiltered so any filter config reuses
    /// it: filters re-apply per Start over the cached decisions
    /// (<c>CachedProblemSetSource</c>), which the filter contracts make
    /// exactly equivalent to filtering during the parse. Written only by
    /// <c>CachedProblemSetSource</c> via <see cref="StoreParsed"/>.
    /// </summary>
    public ParsedProblemSet? Parsed { get; private set; }

    /// <summary>
    /// Store the unfiltered parse of the pick identified by
    /// <paramref name="pickGeneration"/> — decisions and their report as one
    /// value, so neither half can be stored without the other. Silently
    /// dropped when that pick has been superseded (the generation no longer
    /// matches): the in-flight quiz that parsed the old files keeps its own
    /// reference, but a stale parse must never masquerade as the cache of the
    /// <i>new</i> pick.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="parsed"/> is null.</exception>
    public void StoreParsed(int pickGeneration, ParsedProblemSet parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        if (pickGeneration != PickGeneration) return;
        Parsed = parsed;
    }

    /// <summary>
    /// How the picked folder's name displays: <see cref="FolderName"/> in
    /// single quotes, e.g. <c>'MyMatches'</c>, or <c>null</c> when nothing is
    /// picked. The one spelling of that quoting in the app
    /// (<c>halheinrich/backgammon#199</c>): <see cref="Summary"/> composes from
    /// it, and the stats pages show it bare, so a folder reads the same on
    /// every surface that names it.
    /// </summary>
    public string? DisplayName => FolderName is { } name ? $"'{name}'" : null;

    /// <summary>
    /// A short, human-readable label for the picked folder, composed from
    /// <see cref="DisplayName"/> and the count of <see cref="Files"/>: e.g.
    /// <c>"'MyMatches' — 12 problem files"</c>, or <c>null</c> when nothing is
    /// picked. Single source of truth for how a pick describes itself, so a
    /// page re-instantiated by in-app navigation re-derives the same label
    /// from this persisted holder rather than from a transient component field.
    /// </summary>
    public string? Summary => Files.Length switch
    {
        0 => null,
        1 => $"{DisplayName} — 1 problem file",
        var n => $"{DisplayName} — {n} problem files",
    };

    /// <summary>
    /// Replace the pick with <paramref name="sample"/>, drawn from
    /// <paramref name="folderName"/>. Invalidates the parse cache
    /// (<see cref="Parsed"/>) and bumps <see cref="PickGeneration"/>.
    /// </summary>
    /// <param name="folderName">The picked folder's leaf name.</param>
    /// <param name="capability">Whether lifetime stats can be saved into this folder.</param>
    /// <param name="sample">
    /// The pick's draw: the folder's top-level problem files, buffered, and
    /// the per-kind report of what the count caps left unread — see
    /// <see cref="Sample"/>. Taken whole, so a caller that has the report
    /// cannot drop it on the floor.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="folderName"/> or <paramref name="sample"/> is null.</exception>
    public void Set(string folderName, FolderWriteCapability capability, FolderSample sample)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        ArgumentNullException.ThrowIfNull(sample);
        FolderName = folderName;
        Sample = sample;
        Capability = capability;
        Parsed = null;
        PickGeneration++;
        PickOccurrence = new object();
    }

    /// <summary>
    /// Clear the pick (Clear affordance, or after a failed read). Invalidates
    /// the parse cache and bumps <see cref="PickGeneration"/>, so the parsed
    /// decisions (and, transitively, the picked bytes) free immediately.
    /// </summary>
    public void Clear()
    {
        Sample = NoSample;
        FolderName = null;
        Capability = FolderWriteCapability.BrowserUnsupported;
        Parsed = null;
        PickGeneration++;
        PickOccurrence = new object();
    }
}
