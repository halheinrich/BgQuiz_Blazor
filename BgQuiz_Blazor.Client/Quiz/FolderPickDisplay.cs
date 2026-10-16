namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The sentences Home says about picking a folder, kept in one place so the page, Help and
/// the tests agree. Nothing here quotes a browser's prompt text: Chrome and Edge word the
/// File System Access prompts differently.
/// </summary>
internal static class FolderPickDisplay
{
    internal const string SupportedBrowsers =
        "BgQuiz needs a browser that can open a folder of your .xg/.xgp files "
        + "— desktop Chrome and Edge do, and can also save your lifetime stats "
        + "back into that folder. Other browsers often run the quiz but save "
        + "nothing, and some can't open a folder at all.";

    internal const string WriteAccessConsequence =
        "BgQuiz can't keep its lifetime record of which problems give you difficulty";

    /// <summary>Cause-agnostic, deliberately: the browser can refuse to ask, so "you declined" would be false on that path.</summary>
    internal const string WriteAccessNotGranted = "Write access wasn't granted";

    internal const string DeadPickVerdict =
        "this browser can't hand BgQuiz a folder of files";

    /// <summary>The pick-time forecast for a stats file that exists and cannot be read; Help quotes it verbatim.</summary>
    internal const string StatsUnreadableForecast =
        "This folder's " + QuizStatsFile.FileName + " can't be read, so the quiz will "
        + "run but record nothing — and the file will be left untouched.";

    /// <summary>The pick-time forecast for a stats file the browser will not write, naming only what the probe can detect; Help quotes it verbatim.</summary>
    internal const string StatsUnwritableForecast =
        "This folder's " + QuizStatsFile.FileName + " can't be written — it's marked "
        + "read-only, or the browser isn't allowed to write it — so the quiz will run but "
        + "record nothing.";
}
