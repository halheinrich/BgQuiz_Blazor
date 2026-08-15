namespace BgQuiz_Blazor.Client.Quiz;

using System.Text.Json;

/// <summary>
/// The single source of truth for how the persistent lifetime-stats document is
/// stored in the user's picked folder: its file name and the one fixed
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
/// The file name is passed <i>into</i> the JS folder module per call and
/// rendered by Help from this constant — neither the JS nor the help prose
/// restates it, so the documented name and the written name cannot drift (the
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
    /// Name the retired schema-version-1 document is set aside under when a
    /// quiz binds against one (SPEC-stats-identity.md §3): its bytes are copied
    /// here unparsed and a fresh current-version document takes
    /// <see cref="FileName"/>. Nothing ever reads this file — it exists so the
    /// clean break destroys nothing the user had.
    ///
    /// <para>
    /// A bare file name, deliberately path-free: every name in this type
    /// crosses to the browser's folder module as a name <i>within</i> the
    /// picked directory, and a separator would be a path the module has no
    /// business resolving. The version is baked into the literal because only
    /// version 1 is ever retired — a future retirement is a new spec, and would
    /// bring its own name.
    /// </para>
    /// </summary>
    internal const string RetiredFileName = "bgquiz-stats.v1.json";

    /// <summary>
    /// The one fixed options instance every stats write uses. Whitespace is the
    /// only aspect the converter leaves to options; everything else about the
    /// format is converter-pinned.
    /// </summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
}
