namespace BgQuiz_Blazor.Client.Quiz;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BgGame_Lib;

/// <summary>
/// The single source of truth for how the persistent lifetime-stats document is
/// stored in the user's picked folder: its file name, the names a retired
/// document is set aside under, and the one serializer contract
/// (<see cref="DocumentTypeInfo"/>) every read and write of it goes through.
///
/// <para>
/// The wire shape itself is pinned by BgGame_Lib's bundled
/// <c>ProblemStatsDocumentJsonConverter</c> (fixed property names, canonical
/// key ordering — consumers register nothing), so the only serialization choice
/// this app owns is whitespace. <see cref="SerializerOptions"/> makes that
/// choice once: indented, because the file lives beside the user's corpus and
/// should be human-readable and diff-friendly.
/// </para>
///
/// <para>
/// Every name is passed <i>into</i> the JS folder module per call and rendered
/// by Help and the notices from this type — neither the JS nor the prose
/// restates one, so a documented name and a written name cannot drift (the
/// same page/rule discipline as <see cref="PickedFileLimits"/>).
/// </para>
/// </summary>
internal static class QuizStatsFile
{
    /// <summary>
    /// Name of the stats document written into (and read from) the picked
    /// folder, beside the quizzed <c>.xg</c> / <c>.xgp</c> files.
    /// </summary>
    internal const string FileName = "bgquiz-stats.json";

    /// <summary>
    /// Name a document of retired schema version <paramref name="schemaVersion"/>
    /// is set aside under when a quiz binds against one
    /// (SPEC-stats-identity.md §3): its bytes are copied there unparsed and a
    /// fresh current-version document takes <see cref="FileName"/>. It exists
    /// so the clean break destroys nothing the user had, and nothing reads a
    /// set-aside file back — with one ruled exception: the set-aside of the
    /// version that is current again (<c>RetiredNameFor(3)</c>, written by the
    /// interim v4 build) is the base the fold path reads, see
    /// <see cref="MergedNameFor"/>.
    ///
    /// <para>
    /// <b>The name carries the version it came from, because more than one
    /// version retires.</b> Every schema below the current one is retired now,
    /// not version 1 alone, and a tester who skipped a release meets two
    /// retirements in sequence in the same folder — so a fixed name would put
    /// the second set-aside on top of the first and destroy the very file the
    /// first one existed to preserve. Deriving the name from the version the
    /// document declared makes that collision unrepresentable.
    /// </para>
    ///
    /// <para>
    /// A bare file name, deliberately path-free: every name in this type
    /// crosses to the browser's folder module as a name <i>within</i> the picked
    /// directory, and a separator would be a path the module has no business
    /// resolving. Formatted invariantly, like every other string this app puts
    /// on disk.
    /// </para>
    /// </summary>
    /// <param name="schemaVersion">
    /// The schema version the retired document declared — the value
    /// <c>RetiredStatsSchemaException.SchemaVersion</c> carries. Schema versions
    /// start at 1.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="schemaVersion"/> is below 1 — not a version any document
    /// can declare, so a caller that reached here with one has a bug rather than
    /// a file to preserve.
    /// </exception>
    internal static string RetiredNameFor(int schemaVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        return $"bgquiz-stats.v{schemaVersion.ToString(CultureInfo.InvariantCulture)}.json";
    }

    /// <summary>
    /// Name a document of <b>foldable</b> schema version
    /// <paramref name="schemaVersion"/> is renamed aside under once its
    /// records have been folded into the current document (SPEC-stats-identity.md
    /// §3, amended 2026-09-02; halheinrich/backgammon#187): the one version —
    /// the interim v4 that never shipped — whose tallies are carried forward
    /// rather than set aside unread. The bytes are copied there unparsed, as a
    /// retired document's are, so nothing the user had is destroyed; the
    /// current document that replaces <see cref="FileName"/> is the merge of
    /// the folded records into the set-aside base of the version now current
    /// (<c>RetiredNameFor(3)</c>, if that sibling exists), else the folded
    /// records alone.
    ///
    /// <para>
    /// A distinct name from <see cref="RetiredNameFor"/> for the same version
    /// number, on purpose: the two spell two different dispositions. A
    /// <c>.v4.json</c> would read as "set aside, contents discarded" beside the
    /// <c>.v1.json</c> / <c>.v2.json</c> it might share a folder with;
    /// <c>.merged</c> says the contents live on in the current file. Derived
    /// from the version rather than fixed for the same reason the retired name
    /// is, and bare and path-free for the same reason.
    /// </para>
    /// </summary>
    /// <param name="schemaVersion">
    /// The schema version the folded document declared — the value
    /// <c>FoldableStatsSchemaException.SchemaVersion</c> carries. Schema
    /// versions start at 1.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="schemaVersion"/> is below 1.
    /// </exception>
    internal static string MergedNameFor(int schemaVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        return $"bgquiz-stats.v{schemaVersion.ToString(CultureInfo.InvariantCulture)}.merged.json";
    }

    /// <summary>
    /// The one fixed options instance behind <see cref="DocumentTypeInfo"/>.
    /// Whitespace is the only aspect the converter leaves to options;
    /// everything else about the format is converter-pinned. The resolver is
    /// BgGame_Lib's source-generated <see cref="BgGameJsonContext"/> — the
    /// producer's own trim-safe metadata for the document it defines
    /// (halheinrich/backgammon#129), replacing the reflection resolver the
    /// serializer would otherwise fall back to. Private: callers go through
    /// the type info below, never these options, so there is no overload in
    /// this app the trim analyzer has to take on faith.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = BgGameJsonContext.Default,
    };

    /// <summary>
    /// <b>The one serializer contract for the stats document</b> — every read
    /// and write in <see cref="QuizStatsStore"/> names this to the type-info
    /// overloads of <see cref="JsonSerializer"/>, which is what makes the
    /// stats wire statically analyzable: the reflection-based overloads are
    /// annotated unsafe-for-trimming wholesale, options content
    /// notwithstanding, so routing the calls through compile-time metadata is
    /// the shape that lets the publish gate trim safety
    /// (halheinrich/backgammon#129 leg 5).
    ///
    /// <para>
    /// The mechanism changes, the bytes and the signals do not.
    /// <c>ProblemStatsDocumentJsonConverter</c> still does all the writing and
    /// all the reading — the metadata only locates it — so output stays
    /// byte-identical to the reflection path (pinned here and in BgGame_Lib's
    /// own context tests) and <c>RetiredStatsSchemaException</c> still escapes
    /// reads exactly as the store's bind and probe rely on.
    /// </para>
    ///
    /// <para>
    /// Reads share it with writes, deliberately: they previously passed no
    /// options at all, and the one option carried here
    /// (<see cref="JsonSerializerOptions.WriteIndented"/>) has no read half —
    /// so one contract serves both directions and "how a stats document is
    /// read out of a folder" keeps a single spelling in this app.
    /// </para>
    /// </summary>
    internal static readonly JsonTypeInfo<ProblemStatsDocument> DocumentTypeInfo =
        (JsonTypeInfo<ProblemStatsDocument>)SerializerOptions.GetTypeInfo(typeof(ProblemStatsDocument));
}
