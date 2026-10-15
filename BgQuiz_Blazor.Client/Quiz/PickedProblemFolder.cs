namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using BgFolderAccess_Razor;
using XgFilter_Lib;

/// <summary>
/// Per-app holder for the user's picked problem-set folder: the <b>setup</b> a
/// pick begins — the folder's name, the pick-time
/// <see cref="FolderWriteCapability"/> verdict and the pick's identity
/// (<see cref="SetupIdentity"/>) — and the <b>sample</b> it holds: the folder's
/// top-level <c>.xg</c> / <c>.xgp</c> files the draw admitted (buffered), the
/// per-kind report of what the count caps left unread, the sample's identity
/// (<see cref="SampleIdentity"/>) and the parse-once cache of its files
/// (<c>SPEC-filtering.md</c> §4, "The sample, the limits and Resample"). The
/// <see cref="PickedFile"/>s are BgFolderAccess_Razor's: extension-bearing
/// names (the producer's <c>DecisionId</c> stamping discriminates the format
/// from them — see <c>XgFileStream.FileName</c>) over fully-buffered,
/// immutable bytes, which <see cref="PickedFile.OpenRead"/> reads afresh for
/// each enumeration so the source stays re-iterable.
///
/// <para>
/// <b>Two identities, because a resample keeps one and replaces the other.</b>
/// A pick (<see cref="Set"/>) begins a setup and draws its first sample; a
/// resample (<see cref="Resample"/>) draws another sample of the same setup;
/// <see cref="Clear"/> ends both. The filter's applied-ness and the mix follow
/// the setup's identity; the parse cache, the match count, the pick-time
/// stats probe and the pick-time notices follow the sample's. Each identity
/// is a type of its own, so nothing keyed by the one can be keyed by the
/// other by mistake.
/// </para>
///
/// <para>
/// Lifetime: <b>Scoped</b> — in the WebAssembly client that resolves to one
/// instance per loaded app (one tab), the same lifetime as
/// <see cref="QuizController"/>. It shuttles the picked files from
/// <c>Home.razor</c> (the writer) to the <see cref="ProblemSetSourceFactory"/>
/// registered in <c>Program.cs</c> (the reader). Carrying
/// <see cref="Capability"/> and the sample's report here rather than in
/// component fields keeps Home's pick-time notices alive across
/// navigate-back — the same holder-vs-field rationale as the start gate.
/// </para>
///
/// <para>
/// The files are buffered bytes rather than open handles: bytes are read once
/// when the sample is drawn, so the source can re-enumerate (Restart) by
/// opening a fresh read of them each time. The folder's browser-side
/// directory handle is <i>not</i> here — it lives in the JS module behind
/// <see cref="IFolderAccess"/> (handles can't cross the interop boundary), in
/// the <i>picked</i> slot this holder mirrors.
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
    /// <summary>The sample held while no folder is: no files, and no kinds reported.</summary>
    private static readonly FolderSample NoSample = new(Files: [], Kinds: []);

    /// <summary>
    /// The last sequence number given an identity, setup and sample alike, so
    /// no two identities this holder mints describe themselves the same way.
    /// </summary>
    private int _sequence;

    public PickedProblemFolder() => SampleIdentity = NextSampleIdentity();

    /// <summary>The picked folder's top-level problem files — <see cref="Sample"/>'s; empty while no folder is held.</summary>
    public ImmutableArray<PickedFile> Files => Sample.Files;

    /// <summary>The picked folder's leaf name; null while no folder is held.</summary>
    public string? FolderName { get; private set; }

    /// <summary>
    /// The pick-time stats-saving verdict for this folder — the setup's, so a
    /// resample keeps it: drawing the held folder again asks the browser for
    /// nothing. Meaningful only while <see cref="HasFiles"/>; defaults to
    /// <see cref="FolderWriteCapability.BrowserUnsupported"/> when nothing is
    /// picked (no notice renders then, so the value is never shown).
    /// </summary>
    public FolderWriteCapability Capability { get; private set; } = FolderWriteCapability.BrowserUnsupported;

    /// <summary>
    /// The held sample (BgFolderAccess_Razor's <see cref="FolderSample"/>): the
    /// admitted files, buffered, and the per-kind report of how many matching
    /// files of each kind the draw found and admitted (issue
    /// halheinrich/backgammon#59). Held here for the same reason as
    /// <see cref="Capability"/>: the report is a fact about the folder being
    /// <i>held</i>, not about the gesture that fetched it, so Home's notice
    /// must survive navigate-back — which a component field would not. The
    /// no-folder outcomes (cancelled, empty) keep their per-visit flags on the
    /// page precisely because they describe a gesture that left nothing
    /// behind to describe.
    /// </summary>
    public FolderSample Sample { get; private set; } = NoSample;

    /// <summary>True while a folder with at least one problem file is held.</summary>
    public bool HasFiles => !Files.IsEmpty;

    /// <summary>
    /// Whether another draw could hold different files: a folder is held, and
    /// its sample is not the only one the limits could draw — some kind was
    /// over its limit (<see cref="FolderSample.HoldsEveryMatchingFile"/>, the
    /// producer's derivation from the per-kind report). Home offers Resample
    /// exactly while this holds (<c>SPEC-filtering.md</c> §4).
    /// </summary>
    public bool CanResample => HasFiles && !Sample.HoldsEveryMatchingFile;

    /// <summary>
    /// The identity of the setup held — the explicit pick that began it — or
    /// null while no folder is held. Replaced by every <see cref="Set"/>, even
    /// of the same folder; kept by <see cref="Resample"/>; dropped by
    /// <see cref="Clear"/>. What Home reports to the filter setup's owner, and
    /// what the mix panel's mount is keyed by.
    /// </summary>
    public SetupIdentity? SetupIdentity { get; private set; }

    /// <summary>
    /// The identity of the sample held. Replaced by every <see cref="Set"/>,
    /// <see cref="Resample"/> and <see cref="Clear"/>, never by anything else,
    /// so a value read earlier says whether <see cref="Parsed"/> and
    /// <see cref="Files"/> still describe the files it was read with.
    ///
    /// <para>
    /// It keys the parse cache (<see cref="StoreParsed"/>), the match count,
    /// the pick-time stats probe, and is the occurrence of <c>Home</c>'s
    /// pick-time notices (<see cref="QuizNoticeDismissal"/>): a re-pick and a
    /// resample are new occurrences and show those notices fresh, while
    /// navigating away and back must not resurrect a dismissal. Compared by
    /// reference, the one rule the dismissal holder keys every notice by.
    /// </para>
    /// </summary>
    public SampleIdentity SampleIdentity { get; private set; }

    /// <summary>
    /// The parse-once cache: the one completed parse of <see cref="Files"/>
    /// with <b>no filters applied</b> — its decisions and, inseparably, the
    /// <see cref="SourceReport"/> of the walk that produced them, naming the
    /// files that could not be read (halheinrich/backgammon#368) — or null
    /// when the sample held has not been parsed yet. Living on the holder
    /// makes cache lifecycle equal sample lifecycle by construction —
    /// <see cref="Set"/>, <see cref="Resample"/> and <see cref="Clear"/> null
    /// it (freeing the old parse immediately), so no separate invalidation
    /// wiring exists to forget, and the rejection record is retired with the
    /// sample it describes and by nothing else. Unfiltered so any filter
    /// config reuses it: filters re-apply per Start over the cached decisions
    /// (<c>CachedProblemSetSource</c>), which the filter contracts make
    /// exactly equivalent to filtering during the parse. Written only by
    /// <c>CachedProblemSetSource</c> via <see cref="StoreParsed"/>.
    /// </summary>
    public ParsedProblemSet? Parsed { get; private set; }

    /// <summary>
    /// Store the unfiltered parse of the sample identified by
    /// <paramref name="sample"/> — decisions and their report as one value,
    /// so neither half can be stored without the other. Silently dropped when
    /// that sample has been replaced: the parse that read the old files keeps
    /// its own reference, but a stale parse must never masquerade as the
    /// cache of the <i>new</i> sample.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="sample"/> or <paramref name="parsed"/> is null.</exception>
    public void StoreParsed(SampleIdentity sample, ParsedProblemSet parsed)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(parsed);
        if (!ReferenceEquals(sample, SampleIdentity)) return;
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
    /// Hold a new pick: <paramref name="sample"/>, drawn from
    /// <paramref name="folderName"/>. Begins a new setup — a new
    /// <see cref="SetupIdentity"/>, even for the folder held now — and installs
    /// the sample, as <see cref="Resample"/> does.
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
    /// <exception cref="ArgumentException">
    /// <paramref name="sample"/> holds no file. An empty draw holds no folder:
    /// <see cref="Clear"/> is how nothing is held.
    /// </exception>
    public void Set(string folderName, FolderWriteCapability capability, FolderSample sample)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        RequireFiles(sample);
        FolderName = folderName;
        Capability = capability;
        SetupIdentity = new SetupIdentity(++_sequence);
        Install(sample);
    }

    /// <summary>
    /// Replace the held sample with <paramref name="sample"/>, another draw of
    /// the same folder, and keep the setup: the folder's name, its capability
    /// and <see cref="SetupIdentity"/> stand, while the sample's identity, its
    /// report and its parse cache are new (<c>SPEC-filtering.md</c> §4).
    /// </summary>
    /// <param name="sample">The resample's draw.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sample"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="sample"/> holds no file. A resample that finds none
    /// replaces no sample, so the caller keeps the one held.
    /// </exception>
    /// <exception cref="InvalidOperationException">No folder is held, so there is no setup to draw another sample of.</exception>
    public void Resample(FolderSample sample)
    {
        RequireFiles(sample);
        if (!HasFiles)
            throw new InvalidOperationException("No folder is held, so there is nothing to resample.");
        Install(sample);
    }

    /// <summary>
    /// Hold nothing (the Clear affordance, the start of a pick, or a pick that
    /// failed): the setup ends and the sample goes, with its parse cache, so
    /// the parsed decisions (and, transitively, the picked bytes) are no
    /// longer this holder's to keep.
    /// </summary>
    public void Clear()
    {
        FolderName = null;
        Capability = FolderWriteCapability.BrowserUnsupported;
        SetupIdentity = null;
        Install(NoSample);
    }

    /// <summary>
    /// The one writer of the sample and its identity: the old sample's files,
    /// report and parse cache are let go together, and a parse begun against
    /// the old identity can no longer be stored (<see cref="StoreParsed"/>).
    /// </summary>
    private void Install(FolderSample sample)
    {
        Sample = sample;
        Parsed = null;
        SampleIdentity = NextSampleIdentity();
    }

    private SampleIdentity NextSampleIdentity() => new(++_sequence);

    private static void RequireFiles(FolderSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.Files.IsEmpty)
            throw new ArgumentException("A sample with no file holds no folder.", nameof(sample));
    }
}
