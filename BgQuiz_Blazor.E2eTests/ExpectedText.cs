namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// Every user-facing phrase this suite pins in more than one test, spelled once
/// (issue <c>halheinrich/backgammon#4</c>, deliverable 1), each entry naming the
/// app-side surface that owns the words.
///
/// <para>
/// <b>These are still independent literals, and that is the point.</b> The
/// umbrella's copy-pin SSOT ruling (2026-07-25) says this suite must not import
/// the app's constants: the app constant encodes <i>what we tell users</i>, the
/// literal here encodes <i>what a user must be able to read on screen</i>, and
/// importing the one into the other makes an assertion that passes when the
/// constant is emptied. Nothing changes about that. What changes is that a
/// phrase two tests both depend on is now <b>one expectation in one place</b>
/// instead of the same sentence typed twice — the shape the suite already used
/// for the file names it keeps (<c>FsAccessFakeTestBase</c>), extended
/// to the prose it used to inline.
/// </para>
///
/// <para>
/// <b>This file is compiled into the unit project too</b>, linked rather than
/// referenced (<c>BgQuiz_Blazor.Tests.csproj</c>). That project references both
/// app assemblies, which this one deliberately does not, so it is where a
/// <i>drift tripwire</i> can ask whether an app-side constant still contains
/// what this suite pins (<c>CopyDriftTripwireTests</c>, deliverable 2). Nothing
/// here may depend on Playwright or on any other e2e type, or the link stops
/// compiling.
/// </para>
///
/// <para>
/// <b>What belongs here:</b> a phrase reached by more than one test. A phrase
/// only one test pins stays inline where a reader of that test can see it. The
/// owner comments are not a contract the compiler keeps — they are checked by
/// hand, and the unit suite's drift tripwire (halheinrich/backgammon#4,
/// deliverable 2) is what notices a load-bearing one going stale.
/// </para>
///
/// <para>
/// <b>What does not belong here:</b> file names. The stats and saved-filters
/// names stay on <see cref="FsAccessFakeTestBase"/>, whose fake directory is
/// built out of them — they are that fake's vocabulary, not copy, and their
/// occasional appearance in a notice is the app naming a file rather than this
/// suite pinning a sentence. Fixture staging names stay with the fixtures for
/// the same reason.
/// </para>
///
/// <para>
/// <b>Two entries may hold the same words.</b> Where two different app-side
/// surfaces happen to share a fragment — the stats file and the saved-filters
/// file are each reported as "couldn't be read" — they are two expectations,
/// not one, and each gets its own entry and its own owner. Collapsing them
/// would tie a pin on one surface to a reword of the other.
/// </para>
/// </summary>
internal static class ExpectedText
{
    // ------------------------------------------------------------------
    //  Setting up a quiz — Home, and the filter panel it hosts
    // ------------------------------------------------------------------

    /// <summary>
    /// The pick summary's file count, as <c>PickedProblemFolder.Summary</c>
    /// composes it and Home renders it under the <c>Problem folder:</c> caption
    /// (<c>Client/Quiz/PickedProblemFolder.cs</c>). Singular and plural are one
    /// expectation because the app's own singular/plural branch is one rule.
    /// </summary>
    internal static string ProblemFiles(int count) =>
        count == 1 ? "1 problem file" : $"{count} problem files";

    /// <summary>The filter panel's commit button — XgFilter_Razor's <c>FilterPanel.razor</c>.</summary>
    internal const string ApplyFilterButton = "Apply Filter";

    /// <summary>Home's Start hint while no filter is applied (<c>Home.razor</c>).</summary>
    internal const string ApplyFiltersHint = "Apply the filters above to enable Start";

    /// <summary>Home's Start button (<c>Home.razor</c>).</summary>
    internal const string StartQuizButton = "Start Quiz";

    /// <summary>
    /// Home's <b>Clear</b> affordance beside the pick summary, which ends the
    /// current setup (<c>Home.razor</c>). Not the filter panel's "Clear
    /// filters", which is on screen at the same time — pin it with
    /// <c>Exact = true</c>, since Playwright matches accessible names by
    /// substring.
    /// </summary>
    internal const string ClearPickButton = "Clear";

    /// <summary>
    /// The applied-filter count line, as Home composes it — the singular and
    /// plural branch is the app's own (<c>Home.razor</c>). It counts
    /// <i>decisions</i> after dedupe; what that means is the source stack's
    /// contract, not this suite's.
    /// </summary>
    internal static string DecisionsMatchYourFilters(int count) =>
        count == 1 ? "1 decision matches your filters" : $"{count} decisions match your filters";

    /// <summary>The answer-type breakdown's heading, beside that count (<c>Home.razor</c>).</summary>
    internal const string AnswerTypeHeading = "By answer type";

    /// <summary>
    /// One row of that breakdown, as Home renders it — <c>label: count</c>
    /// (<c>Home.razor</c>). The labels are <c>AnswerTypeDisplay</c>'s for the
    /// checker row and the cube label home's for the rest, so the row is
    /// composed here from whichever this suite is naming.
    /// </summary>
    internal static string AnswerTypeCount(string answerType, int count) => $"{answerType}: {count}";

    /// <summary>The checker-play bucket's label (<c>Client/Quiz/AnswerTypeDisplay.cs</c>).</summary>
    internal const string CheckerPlaysType = "Checker plays";

    /// <summary>The weighted mix's category select (<c>MixPanel.razor</c>, its aria-label).</summary>
    internal const string MixCategoryLabel = "Category";

    // ------------------------------------------------------------------
    //  Answering and reviewing — the quiz page and the producers it renders
    // ------------------------------------------------------------------

    /// <summary>The quiz page's answer commit (<c>Quiz.razor</c>).</summary>
    internal const string SubmitButton = "Submit";

    /// <summary>Past a problem without answering it (<c>Quiz.razor</c>).</summary>
    internal const string SkipButton = "Skip";

    /// <summary>On to the next problem, at review (<c>Quiz.razor</c>).</summary>
    internal const string ContinueButton = "Continue";

    /// <summary>The practice retry, at review (<c>Quiz.razor</c>).</summary>
    internal const string RedoButton = "Redo";

    /// <summary>
    /// The decision's notes control, at review when the decision carries a
    /// comment (<c>Components/DecisionNotes.razor</c>, which also heads its
    /// overlay with the same word). Exact-match it: the overlay's close button
    /// is named "Close notes".
    /// </summary>
    internal const string NotesButton = "Notes";

    /// <summary>Ending a run early (<c>Quiz.razor</c>).</summary>
    internal const string EndQuizButton = "End quiz";

    /// <summary>
    /// The three cube pills this suite presses. Their words are the label
    /// home's — <c>CubeLabels</c> in BackgammonDiagram_Lib, rendered by
    /// BgDiag_Razor's <c>BackgammonCubeActions</c> — and they are pinned here as
    /// consumer literals <b>by ruling</b>: a re-wording at that home must arrive
    /// as a deliberate edit rather than pass through unseen, so they are never
    /// re-sourced from it (see <c>E2eTestBase.AnswerCubeAsync</c>).
    /// </summary>
    internal const string NoDoublePill = "No double";

    /// <inheritdoc cref="NoDoublePill"/>
    internal const string DoubleTakePill = "Double / Take";

    /// <inheritdoc cref="NoDoublePill"/>
    internal const string TooGoodPill = "Too good";

    /// <summary>
    /// The review verdict for a fully correct (No double, Take) cube answer —
    /// composed by <c>Quiz.razor.cs</c>'s <c>CubeVerdict</c> over the same label
    /// home, so the pill's words appear inside it.
    /// </summary>
    internal const string CubeVerdictNoDoubleAndTakeCorrect = "No double: correct · Take: correct";

    /// <summary>The review verdict for the best checker play (<c>Quiz.razor.cs</c>).</summary>
    internal const string BestPlayVerdict = "Correct — you found the best play.";

    /// <summary>
    /// The solution diagram's banner for a (No double, Take) position —
    /// producer copy (<c>DiagramRenderer</c> over <c>CubeLabels</c>), pinned
    /// here because it is what a user reads off this app's board.
    /// </summary>
    internal const string SolutionBestNoDouble = "Best: No double";

    // ------------------------------------------------------------------
    //  Finishing — the score panel, Done, and the way back
    // ------------------------------------------------------------------

    /// <summary>Done's problem count (<c>Done.razor</c>).</summary>
    internal static string TotalProblemsShown(int count) => $"Total problems shown: {count}";

    /// <summary>The score panel's submitted count (<c>ScorePanel.razor</c>).</summary>
    internal static string Submitted(int count) => $"Submitted: {count}";

    /// <summary>The score panel's skipped count (<c>ScorePanel.razor</c>).</summary>
    internal static string Skipped(int count) => $"Skipped: {count}";

    /// <summary>Done's navigation back to setup (<c>Done.razor</c>).</summary>
    internal const string BackToSetupButton = "Back to setup";

    /// <summary>
    /// The way back into a running quiz, offered by every page that can be
    /// reached mid-quiz (<c>ReturnControl.razor</c> on Stats, Settings and
    /// Help; <c>Home.razor</c>'s own).
    /// </summary>
    internal const string BackToQuizButton = "Back to quiz";

    /// <summary>
    /// The same control with no quiz live: the way back to the setup page,
    /// named as the navigation panel names it (<c>ReturnControl.razor</c>).
    /// </summary>
    internal const string BackToHomeButton = "Back to Home";

    // ------------------------------------------------------------------
    //  The layout and the settings page
    // ------------------------------------------------------------------

    /// <summary>Navigation links (<c>NavMenu.razor</c>).</summary>
    internal const string HomeNavLink = "Home";

    /// <inheritdoc cref="HomeNavLink"/>
    internal const string SettingsNavLink = "Settings";

    /// <inheritdoc cref="HomeNavLink"/>
    internal const string HelpNavLink = "Help";

    /// <summary>The navigation panel's collapse control, by its accessible name (<c>MainLayout.razor</c>).</summary>
    internal const string HideNavigationPanelCheckbox = "Hide navigation panel";

    /// <summary>
    /// The setting that persists that fold (<c>Settings.razor</c>); Help names
    /// it too, which is why both suites pin the same words.
    /// </summary>
    internal const string KeepNavigationPanelFoldedSetting = "Keep the navigation panel folded";

    /// <summary>The depth-ceiling dropdown's "hide nothing" option (<c>Settings.razor</c>).</summary>
    internal const string HideNothingOption = "Hide nothing";

    /// <summary>
    /// The deepest option in that dropdown — producer words, from XgFilter_Lib's
    /// enum labels (<c>EnumLabel.ToLabel</c>), listed by <c>Settings.razor</c>.
    /// </summary>
    internal const string XgRollerPlusPlusOption = "XG Roller++";

    /// <summary>The home-board side radio (<c>Settings.razor</c>).</summary>
    internal const string HomeBoardLeftRadio = "Left";

    // ------------------------------------------------------------------
    //  Notices: the pick, the stats file, the reload
    // ------------------------------------------------------------------

    /// <summary>
    /// The fragment of Home's silent-gesture account both halves of the
    /// dead-pick pair key on (<c>Home.razor</c>, issue
    /// <c>halheinrich/backgammon#105</c>). Shared deliberately, and the reason
    /// generalizes to everything in this class: an <b>absence</b> assertion
    /// written against its own copy of a sentence goes <i>vacuously green</i>
    /// the moment the notice is reworded — it stops matching, keeps passing, and
    /// proves nothing. One entry forces the pair to move together. It is also
    /// the minimum discriminating substring, so a polish elsewhere in the
    /// sentence does not break it spuriously.
    /// </summary>
    internal const string SilentPickGestureAccount = "opens nothing at all";

    /// <summary>The FS-Access branch's pre-gesture advisory (<c>Home.razor</c>).</summary>
    internal const string BrowserWillAskAboutTheFolder = "Your browser will ask about the selected folder";

    /// <summary>Home's failed-pick banner (<c>Home.razor</c>).</summary>
    internal const string CouldNotReadTheFolder = "Could not read the folder";

    /// <summary>
    /// The write-access consequence, the load-bearing half of the stats-enabled
    /// / stats-denied pair (<c>Client/Quiz/FolderPickDisplay.cs</c>) — a
    /// data-protection message behind a permission path, which is why the unit
    /// suite's drift tripwire covers this one.
    /// </summary>
    internal const string LifetimeRecordConsequence = "which problems give you difficulty";

    /// <summary>Home's stats-enabled notice (<c>Home.razor</c>).</summary>
    internal const string StatsWillBeSaved = "stats will be saved";

    /// <summary>
    /// Home's pick-time forecast for a stats file that can't be read
    /// (<c>Client/Quiz/FolderPickDisplay.cs</c>, <c>StatsUnreadableForecast</c>).
    /// </summary>
    internal const string StatsUnreadableForecast = "can't be read, so the quiz will run but record nothing";

    /// <summary>
    /// Home's pick-time forecast for a stats file that can't be written
    /// (<c>Client/Quiz/FolderPickDisplay.cs</c>, <c>StatsUnwritableForecast</c>).
    /// </summary>
    internal const string StatsUnwritableForecast = "can't be written";

    /// <summary>
    /// The retirement report before it happens, on Home (<c>Home.razor</c>) …
    /// </summary>
    internal const string StatsFileWillBeSetAside = "will be set aside as";

    /// <summary>… and after it has, on Quiz and Done (<c>Quiz.razor</c>, <c>Done.razor</c>).</summary>
    internal const string StatsFileHasBeenSetAside = "has been set aside as";

    /// <summary>
    /// The stats file reported unreadable (<c>Quiz.razor</c>, <c>Done.razor</c>,
    /// and <c>MixDisplay</c>'s refusal reason). Same words as
    /// <see cref="FiltersFileUnreadable"/> and a different expectation — see
    /// this class's remarks.
    /// </summary>
    internal const string StatsFileUnreadable = "couldn't be read";

    /// <summary>
    /// The saved-filters file reported unreadable — producer copy
    /// (XgFilter_Razor's <c>FilterSurface.razor</c>).
    /// </summary>
    internal const string FiltersFileUnreadable = "couldn't be read";

    /// <summary>Home's one-shot reload-reset notice (<c>Home.razor</c>).</summary>
    internal const string QuizResetByReload = "Your previous quiz was reset by the page reload";

    // ------------------------------------------------------------------
    //  Help
    // ------------------------------------------------------------------

    /// <summary>The scoring section's heading (<c>Client/Components/Pages/HelpSections.cs</c>).</summary>
    internal const string HelpScoringSection = "Scoring";
}
