namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The single source of truth for the saved-filters document's file name in the
/// user's picked folder — the filters sibling of <see cref="QuizStatsFile"/>,
/// living beside it and the corpus.
///
/// <para>
/// Unlike <see cref="QuizStatsFile"/> there is <b>no</b>
/// <c>JsonSerializerOptions</c> here: the saved-filters document
/// (<see cref="XgFilter_Lib.Filtering.NamedFilterCollection"/>) owns its wire
/// format end to end through a type-level <c>[JsonConverter]</c>, so the app
/// round-trips it via the document's own <c>ToJson</c> / <c>TryFromJson</c> and
/// chooses nothing about the serialization — there is no whitespace-or-anything
/// knob left for this app to own. (That the collection carries its own
/// converter is exactly why the producer library exposes those methods rather
/// than raw options.)
/// </para>
///
/// <para>
/// The file name is passed <i>into</i> the JS folder module per call and never
/// restated JS-side, the same page/rule no-drift discipline as
/// <see cref="QuizStatsFile"/> and <see cref="PickedFileLimits"/>.
/// </para>
/// </summary>
internal static class QuizFiltersFile
{
    /// <summary>
    /// Name of the saved-filters document written into (and read from) the
    /// picked folder, beside the quizzed <c>.xg</c> / <c>.xgp</c> files and the
    /// <see cref="QuizStatsFile.FileName">stats document</see>.
    /// </summary>
    internal const string FileName = "bgquiz-filters.json";
}
