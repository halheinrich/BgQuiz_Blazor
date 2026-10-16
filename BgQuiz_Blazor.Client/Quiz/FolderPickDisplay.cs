namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>The sentences Home says about picking a folder, kept in one place so the page and its tests agree.</summary>
internal static class FolderPickDisplay
{
    internal const string SupportedBrowsers =
        "BgQuiz needs a browser that can open a folder of your .xg/.xgp files "
        + "— desktop Chrome and Edge do, and can also save your lifetime stats "
        + "back into that folder. Other browsers often run the quiz but save "
        + "nothing, and some can't open a folder at all.";

    internal const string WriteAccessConsequence =
        "BgQuiz can't keep its lifetime record of which problems give you difficulty";

    internal const string WriteAccessNotGranted = "Write access wasn't granted";

    internal const string DeadPickVerdict =
        "this browser can't hand BgQuiz a folder of files";
}
