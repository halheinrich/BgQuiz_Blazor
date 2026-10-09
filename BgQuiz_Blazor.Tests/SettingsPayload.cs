namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The settings entry's payload as <c>QuizSettings</c> writes it — every
/// field, in the pinned order — for a test that declares a settings write on a
/// <c>BrowserStoragePlan</c>, which matches a write by its exact value. This
/// suite's one restatement of the wire format beside the byte-literal pins in
/// <c>QuizSettingsTests</c> (<c>Persist_WritesThePinnedWireFormat</c> and its
/// sibling), which it must agree with or every write declared through it
/// fails. The defaults are the settings' own.
/// </summary>
internal static class SettingsPayload
{
    /// <summary>The payload holding the given values.</summary>
    /// <param name="maximumHiddenCandidateAnalysisLevel">The ceiling's member-name token, or <see langword="null"/> to hide nothing.</param>
    public static string Of(
        bool homeBoardOnRight = true,
        bool randomizeSidePerProblem = false,
        bool keepNavigationPanelFolded = false,
        bool maximizeBoardWhileAnswering = true,
        bool sortAnalysisByDepthFirst = false,
        string? maximumHiddenCandidateAnalysisLevel = null,
        bool weightQuizzesByStats = false)
    {
        static string B(bool value) => value ? "true" : "false";
        var level = maximumHiddenCandidateAnalysisLevel is null ? "null" : $"\"{maximumHiddenCandidateAnalysisLevel}\"";
        return $$"""{"homeBoardOnRight":{{B(homeBoardOnRight)}},"randomizeSidePerProblem":{{B(randomizeSidePerProblem)}},"keepNavigationPanelFolded":{{B(keepNavigationPanelFolded)}},"maximizeBoardWhileAnswering":{{B(maximizeBoardWhileAnswering)}},"sortAnalysisByDepthFirst":{{B(sortAnalysisByDepthFirst)}},"maximumHiddenCandidateAnalysisLevel":{{level}},"weightQuizzesByStats":{{B(weightQuizzesByStats)}}}""";
    }
}
