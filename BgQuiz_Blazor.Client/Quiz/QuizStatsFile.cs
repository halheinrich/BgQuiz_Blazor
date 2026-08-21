namespace BgQuiz_Blazor.Client.Quiz;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// The single source of truth for how the persistent lifetime-stats document is
/// stored in the user's picked folder: its file name, the names a retired
/// document is set aside under, and the one fixed
/// <see cref="JsonSerializerOptions"/> it is written with.
///
/// <para>
/// The wire shape itself is pinned by BgGame_Lib's bundled
/// <c>ProblemStatsDocumentJsonConverter</c> (fixed property names, canonical
/// key ordering — consumers register nothing), so the only serialization choice
/// this app owns is whitespace. <see cref="SerializerOptions"/> makes that
/// choice once: indented, because the file lives beside the user's corpus and
/// should be human-readable and diff-friendly. Every write goes through these
/// options; reads need none (the converter is type-level).
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
    /// fresh current-version document takes <see cref="FileName"/>. Nothing ever
    /// reads a set-aside file — it exists so the clean break destroys nothing
    /// the user had.
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
    /// The one fixed options instance every stats write uses. Whitespace is the
    /// only aspect the converter leaves to options; everything else about the
    /// format is converter-pinned.
    /// </summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
}
