namespace BgQuiz_Blazor.Client.Quiz;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BgGame_Lib;

/// <summary>
/// The one source for how the lifetime-stats document is stored in the user's folder: its
/// file name, the names a retired or folded document is set aside under, and the serializer
/// contract every read and write of it goes through. The wire shape is BgGame_Lib's
/// (<c>ProblemStatsDocumentJsonConverter</c>); the only choice this app owns is whitespace,
/// indented because the file lives beside the user's corpus.
/// </summary>
internal static class QuizStatsFile
{
    /// <summary>The stats document's name in the picked folder, beside the problem files.</summary>
    internal const string FileName = "bgquiz-stats.json";

    /// <summary>
    /// The name a document of retired schema version <paramref name="schemaVersion"/> is set
    /// aside under at the bind (SPEC-stats-identity.md §3): its bytes copied there unparsed
    /// before a fresh document takes <see cref="FileName"/>. The name carries the version
    /// because more than one version retires, and a fixed name would put a later set-aside
    /// over the only surviving copy of an earlier one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="schemaVersion"/> is below 1.</exception>
    internal static string RetiredNameFor(int schemaVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        return $"bgquiz-stats.v{schemaVersion.ToString(CultureInfo.InvariantCulture)}.json";
    }

    /// <summary>
    /// The name a document of foldable schema version <paramref name="schemaVersion"/> (the
    /// interim v4) is kept under once its records have been folded into the current document
    /// (SPEC-stats-identity.md §3, amended 2026-09-02): a different spelling from
    /// <see cref="RetiredNameFor"/>, since <c>.merged</c> says the contents live on.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="schemaVersion"/> is below 1.</exception>
    internal static string MergedNameFor(int schemaVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        return $"bgquiz-stats.v{schemaVersion.ToString(CultureInfo.InvariantCulture)}.merged.json";
    }

    // Resolved by BgGame_Lib's source-generated context, never by reflection, so the stats
    // wire survives the client's trim (halheinrich/backgammon#129).
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = BgGameJsonContext.Default,
    };

    /// <summary>The one serializer contract for the stats document, for the type-info overloads of <see cref="JsonSerializer"/>.</summary>
    internal static readonly JsonTypeInfo<ProblemStatsDocument> DocumentTypeInfo =
        (JsonTypeInfo<ProblemStatsDocument>)SerializerOptions.GetTypeInfo(typeof(ProblemStatsDocument));
}
