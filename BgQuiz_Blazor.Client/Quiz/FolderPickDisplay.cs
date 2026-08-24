namespace BgQuiz_Blazor.Client.Quiz;

using System.Globalization;
using BgFolderAccess_Razor;

/// <summary>
/// The one home for user-facing folder-pick wording shared across more than one
/// surface — the folder-pick sibling of <see cref="MixDisplay"/>. Keeping such a
/// phrase here, rather than as a literal per render site, is what stops the two
/// renderings of the same fact from drifting apart.
///
/// <para>
/// Deliberately <b>not</b> a home for every string on the pick surface: a phrase
/// earns a constant here only once a second surface renders it. Help's prose
/// stays prose — it explains the same rules at length and in its own voice, so
/// pinning it to these constants would fight the page rather than serve it. The
/// one exception is <see cref="SupportedBrowsers"/>, which Help's "before you
/// start" lead renders <i>verbatim</i> rather than restating: it is a single
/// sentence of fact, not an explanation, and the two surfaces must agree
/// exactly — see its own remarks.
/// </para>
///
/// <para>
/// <b>The folder-<i>load</i> refusal lives here too</b>, on a second rule the
/// two-surface one does not cover:
/// <see cref="MalformedForQuizzing(string, int)"/> is composed deep inside the
/// source stack — where no user-facing wording otherwise lives — and travels to
/// the page as an exception message. Left at its throw site the copy would sit
/// inside a decorator, and the test that pins it could only re-type it. One
/// surface renders it; the reason it is here is the distance between where it
/// is written and where it is read.
/// </para>
///
/// <para>
/// <b>Never quote a browser's prompt text.</b> Chrome and Edge word the File
/// System Access prompts differently, and Edge interpolates the folder's own
/// name into the write prompt, so any string claiming to be what the user will
/// read is wrong somewhere. Wording here describes the <i>grant</i> being asked
/// for, hedged ("your browser asks whether…"), and asserts no browser's exact
/// string.
/// </para>
/// </summary>
internal static class FolderPickDisplay
{
    /// <summary>
    /// What the folder pick actually needs from a browser, in one sentence pair
    /// that reads correctly standing alone (Home, beside the pick button) and
    /// inside a prerequisites list (Help's "before you start" lead).
    ///
    /// <para>
    /// It exists because the pick is a <b>dead entry point</b> where the browser
    /// can serve neither mechanism: with no <c>showDirectoryPicker</c> the hidden
    /// <c>webkitdirectory</c> input is all that is left, and a browser may accept
    /// that attribute while its picker never honors it — observed on a tablet,
    /// where the button raised no chooser of any kind
    /// (halheinrich/backgammon#108). The visitor is left on an unchanged page
    /// with no account of why — the same silence the cancelled-pick notice was
    /// added to end, except that no code path here can even report the cause.
    /// Only a statement made <i>before</i> the gesture can cover that case.
    /// </para>
    ///
    /// <para>
    /// Home renders it <b>ungated by the File System Access probe</b>, unlike the
    /// two-step permission guidance: the readers it is written for are exactly the
    /// ones the probe excludes. It hides once a folder is held (the pick worked;
    /// the caution is moot), the same "no folder held" window its neighbour uses.
    /// </para>
    ///
    /// <para>
    /// <b>The claims are device-free on purpose, bar one.</b> This sentence once
    /// read "on phones, choosing a folder may not work at all", which is now
    /// false in <i>both</i> directions: Chrome for Android completes the pick
    /// (read-only — halheinrich/backgammon#109), while a WebView-wrapping browser
    /// on a tablet could not pick at all (halheinrich/backgammon#108). A dead
    /// pick gesture is <b>capability</b>-shaped, not screen-shaped, so the
    /// sentence names capabilities and hedges what it cannot observe from here:
    /// "often" run the quiz, "some" can't open a folder. That is the
    /// never-quote-a-prompt discipline above, extended to device classes.
    /// </para>
    ///
    /// <para>
    /// The one vendor pair it still names is scoped to <b>desktop</b>, and the
    /// scope is load-bearing rather than stylistic: desktop Chromium is where
    /// <i>both</i> grants are observed to land, and Chrome for Android is
    /// precisely a Chrome that cannot write (the write request is refused for
    /// want of transient activation — see <see cref="WriteAccessNotGranted"/>).
    /// An unscoped "Chrome and Edge can save" would be false on that browser.
    /// The names stay because "a browser that can open a folder" is not
    /// something a reader can check, and two names are: the rule is capabilities
    /// over vendors <i>where possible</i>, not vendors never.
    /// </para>
    /// </summary>
    internal const string SupportedBrowsers =
        "BgQuiz needs a browser that can open a folder of your .xg/.xgp files "
        + "— desktop Chrome and Edge do, and can also save your lifetime stats "
        + "back into that folder. Other browsers often run the quiz but save "
        + "nothing, and some can't open a folder at all.";

    /// <summary>
    /// What the absence of write access to the picked folder actually costs, in
    /// one clause that reads correctly mid-sentence on either surface that
    /// renders it: Home's pre-pick permission guidance (forward-looking —
    /// "without it, …") and Home's <see cref="FolderWriteCapability.PermissionDenied"/>
    /// outcome notice (after the fact — "the quiz runs, but …").
    ///
    /// <para>
    /// The point it exists to make: the loss is not an abstract "stats file",
    /// it is the lifetime record of <i>which problems the user finds hard</i> —
    /// the thing the weighted mix and the lifetime scoreboard are built from.
    /// The bare "stats won't be saved" this replaced understated that.
    /// </para>
    /// </summary>
    internal const string WriteAccessConsequence =
        "BgQuiz can't keep its lifetime record of which problems give you difficulty";

    /// <summary>
    /// The cause-agnostic premise every "no write access" surface opens with —
    /// the lead-in that <see cref="WriteAccessConsequence"/> and the
    /// saved-filters load-only reason each complete in their own words.
    ///
    /// <para>
    /// <b>It deliberately does not say the user declined.</b>
    /// <see cref="FolderWriteCapability.PermissionDenied"/> has two causes and
    /// cannot tell them apart: the user answered no, <i>or</i> the browser
    /// <b>refused to ask</b>. That refusal is spec'd, not a quirk:
    /// <c>requestPermission</c> requires transient user activation and
    /// <i>throws</i> <c>SecurityError</c> when there is none — it never
    /// resolves "denied" — and <c>folderAccess.js</c>'s <c>beginPick</c>
    /// catches exactly that throw and degrades onto this rung. It is what
    /// Chrome for Android does on <i>every</i> pick, a second fresh gesture
    /// included, because the picker leaves no live activation behind
    /// (halheinrich/backgammon#109). On that second path no second prompt is
    /// ever shown, so "you declined write access" attributes a decision the
    /// user never made. Same discipline as the cancelled-pick notice: true
    /// under both causes, and non-accusatory.
    /// </para>
    ///
    /// <para>
    /// Rendered by two surfaces — Home's PermissionDenied outcome notice and
    /// Home's <c>SavedFiltersDisabledReason</c> — which is exactly the bar for
    /// living here. It earned the constant the hard way: the two carried the
    /// same false "you declined" premise and one was corrected without the
    /// other, which is the drift this class exists to prevent.
    /// </para>
    /// </summary>
    internal const string WriteAccessNotGranted = "Write access wasn't granted";

    /// <summary>
    /// The dead-pick verdict — what it means when the pick gesture raises no
    /// chooser at all — in one clause that reads correctly mid-sentence inside
    /// either surface's own conditional: the pre-pick silent-gesture account
    /// ("If choosing a folder opens nothing at all, … — there's nothing more to
    /// try here", issue #105) and the cancelled-pick notice's post-gesture hedge
    /// ("If no folder chooser opened, …", issue #116).
    ///
    /// <para>
    /// The clause is only ever asserted <b>under an antecedent the reader can
    /// check</b> ("opens nothing" / "no chooser opened"), never flatly: no code
    /// path can detect a dead gesture (§ <see cref="SupportedBrowsers"/> for the
    /// absent-mechanism case; issue #116's present-but-inert
    /// <c>showDirectoryPicker</c> for the other — it aborts without ever opening,
    /// indistinguishable from a user cancel). Each surface supplies the
    /// antecedent for the moment it renders in; sharing the verdict is what
    /// keeps the two accounts of the same dead end from drifting.
    /// </para>
    /// </summary>
    internal const string DeadPickVerdict =
        "this browser can't hand BgQuiz a folder of files";

    /// <summary>
    /// Why a picked folder will not load: it holds a money-game position whose
    /// file does not say which Jacoby rule was in force, so BgQuiz cannot tell
    /// that problem apart from the one played under the other rule
    /// (<c>../SPEC-stats-identity.md</c> §2, amended 2026-08-24; issue
    /// <c>halheinrich/backgammon#142</c>). Composed by
    /// <see cref="JacobyStampedProblemSetSource"/> as its exception message and
    /// rendered verbatim by Home's start-error banner, which supplies its own
    /// "Could not start quiz:" lead — so this reads as the cause and the
    /// remedy, never as a second headline.
    ///
    /// <para>
    /// <b>It names one file and counts the rest.</b> The alternative — listing
    /// every offending file — was rejected: the shape is emittable by no
    /// producer in the ecosystem, so reaching it at all means a converting
    /// parser wrote a whole folder's money records without the fact, and a list
    /// would then be hundreds of names long inside a one-paragraph banner while
    /// saying nothing the first name and the count do not. One name is what
    /// makes the report actionable (open that file, quote it); the count is
    /// what tells the reader whether removing one file will do.
    /// </para>
    ///
    /// <para>
    /// The wording says <i>malformed for quizzing</i> rather than blaming the
    /// user's files in general: they are perfectly good XG records, and the
    /// only recourse the reader has from here is to take the file out of the
    /// folder — so that is the only instruction given.
    /// </para>
    /// </summary>
    /// <param name="sourceFile">
    /// Bare name of the first offending file, as the record's
    /// <c>DecisionId.Filename</c> spells it.
    /// </param>
    /// <param name="otherFileCount">
    /// How many <i>further</i> distinct files hold the same defect; zero when
    /// <paramref name="sourceFile"/> is the only one.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="sourceFile"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="otherFileCount"/> is negative.</exception>
    internal static string MalformedForQuizzing(string sourceFile, int otherFileCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        ArgumentOutOfRangeException.ThrowIfNegative(otherFileCount);

        if (otherFileCount == 0)
        {
            return $"“{sourceFile}” holds a money-game position that doesn't state "
                + "whether the Jacoby rule is in force, so that file is malformed "
                + "for quizzing. Remove it from the folder, then pick the folder again.";
        }

        var others = otherFileCount == 1
            ? "1 other file"
            : $"{otherFileCount.ToString(CultureInfo.InvariantCulture)} other files";
        return $"“{sourceFile}” and {others} hold money-game positions that don't "
            + "state whether the Jacoby rule is in force, so those files are "
            + "malformed for quizzing. Remove them from the folder, then pick the "
            + "folder again.";
    }
}
