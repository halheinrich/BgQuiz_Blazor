namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// One addressable entry in <c>/help</c>'s outline — a part or a section — as
/// the page's headings, its contents block and its pins all read it: where the
/// entry lives (<see cref="AnchorId"/>) and what its heading says
/// (<see cref="Heading"/>), and nothing else.
///
/// <para>
/// Two types rather than one because a part <i>holds</i> sections and a section
/// does not (<see cref="HelpPart.Sections"/>); the pair of values they share,
/// and the anchor-id rule over it, live here so neither can drift from the
/// other. Constructed only by <see cref="HelpSections"/>: these describe one
/// fixed document, so nothing outside it has an entry to mint.
/// </para>
/// </summary>
internal abstract class HelpEntry
{
    /// <param name="anchorId">
    /// The entry's stable, hand-named anchor id, carrying
    /// <see cref="HelpSections.AnchorIdPrefix"/>.
    /// </param>
    /// <param name="heading">The heading text the page renders for the entry.</param>
    private protected HelpEntry(string anchorId, string heading)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(anchorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(heading);

        // SPEC-help.md §3's namespace rule, enforced rather than stated: /help
        // renders XgFilter_Razor's FilterHelp inside one of its own sections, so
        // the two id namespaces share a document and a collision would be a
        // silently mis-landing link. Checking at construction makes an id that
        // forgot the prefix a startup failure in every test that renders the
        // page, rather than a defect only a browser could show.
        if (!anchorId.StartsWith(HelpSections.AnchorIdPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Help anchor ids must start with '{HelpSections.AnchorIdPrefix}': '{anchorId}'.",
                nameof(anchorId));
        }

        AnchorId = anchorId;
        Heading = heading;
    }

    /// <summary>
    /// The entry's anchor id — the <c>id</c> its heading carries and the fragment
    /// every contents entry and deep link into it is built from.
    ///
    /// <para>
    /// Hand-named and <b>stable</b>, never derived from <see cref="Heading"/>:
    /// the two change for different reasons, and a reword must not break a
    /// reader's bookmark (SPEC-help.md §3).
    /// </para>
    /// </summary>
    internal string AnchorId { get; }

    /// <summary>
    /// The entry's heading text — rendered by the page, by the contents block,
    /// and by nothing else. The one spelling of these words in this repo.
    /// </summary>
    internal string Heading { get; }
}

/// <summary>
/// One section of <c>/help</c> — an <c>h3</c> under the part that owns it.
/// </summary>
internal sealed class HelpSection : HelpEntry
{
    internal HelpSection(string anchorId, string heading)
        : base(anchorId, heading) { }
}

/// <summary>
/// One part of <c>/help</c> — an <c>h2</c> grouping the sections a reader meets
/// at the same stage of the journey (SPEC-help.md §1).
/// </summary>
internal sealed class HelpPart : HelpEntry
{
    internal HelpPart(string anchorId, string heading, params HelpSection[] sections)
        : base(anchorId, heading)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (sections.Length == 0)
            throw new ArgumentException("A help part holds at least one section.", nameof(sections));

        Sections = [.. sections];
    }

    /// <summary>The part's sections, in document order.</summary>
    internal IReadOnlyList<HelpSection> Sections { get; }
}

/// <summary>
/// The structure of the <c>/help</c> page: five parts and the fourteen sections
/// beneath them, in document order, each with its anchor id and its heading text
/// (SPEC-help.md §§1, 3 — the authoritative model for this page's information
/// architecture).
///
/// <para>
/// This is the <b>single source</b> for that structure. <c>Help.razor</c>
/// renders its headings and its contents block from this table, and
/// <c>PageTests</c> reads the same table, so nothing in the page or the tests
/// restates a heading string or an id: a section cannot be listed in the
/// contents and missing from the document, or renamed in one place only. The
/// e2e suite deliberately keeps hardcoded literals instead — it references no
/// app assembly, which is what makes it the half of the copy-pin split that
/// says <i>which</i> words are right.
/// </para>
///
/// <para>
/// The order is the journey the page has always taught, unchanged by the
/// grouping: a reader meets these in the order Home presents them, and forward
/// references between sections are the page's idiom rather than a defect in the
/// order.
/// </para>
///
/// <para>
/// It mirrors the contract <c>XgFilter_Razor</c>'s <c>FilterHelp</c> already
/// exports for its storage section
/// (<c>StorageSectionAnchorId</c> / <c>StorageSectionHeading</c>, which this
/// page's data section consumes) — the page adopting the producer's idiom for
/// its own sections. <see cref="AnchorIdPrefix"/> is what keeps the two
/// namespaces apart on the one document they share.
/// </para>
/// </summary>
internal static class HelpSections
{
    /// <summary>
    /// The prefix every anchor id on this page carries — distinct from
    /// <c>FilterHelp</c>'s <c>fh-</c>, which renders inside <i>Choose filters</i>
    /// on the same document. Enforced by <see cref="HelpEntry"/>'s constructor.
    /// </summary>
    internal const string AnchorIdPrefix = "help-";

    // -----------------------------------------------------------------------
    //  The sections, in document order. Declared before the parts that hold
    //  them: static initializers run in textual order.
    // -----------------------------------------------------------------------

    /// <summary>
    /// The prerequisites list. Named <i>What you need</i> rather than
    /// <i>Before you start</i> — the words it shipped under — because the part
    /// that holds it keeps the journey framing and two headings with one
    /// accessible name make a role-based lookup ambiguous (SPEC-help.md §1,
    /// ruled). Only the heading changed; the prose did not.
    /// </summary>
    internal static HelpSection WhatYouNeed { get; } = new("help-what-you-need", "What you need");

    /// <summary>The data-ownership and storage account.</summary>
    internal static HelpSection YourDataStaysYours { get; } =
        new("help-your-data", "Your data stays yours");

    /// <summary>The folder pick and its per-format caps.</summary>
    internal static HelpSection PickYourFolder { get; } = new("help-pick-folder", "Pick your folder");

    /// <summary>
    /// The app-level filter framing, and the section that embeds
    /// <c>FilterHelp</c>.
    /// </summary>
    internal static HelpSection ChooseFilters { get; } = new("help-choose-filters", "Choose filters");

    /// <summary>The named saved-filter sets and the file they live in.</summary>
    internal static HelpSection SaveFilters { get; } =
        new("help-saved-filters", "Save filters you use often");

    /// <summary>The stats-weighted mix.</summary>
    internal static HelpSection WeightedMix { get; } =
        new("help-weighted-mix", "Weight the quiz by your lifetime stats");

    /// <summary>The two problem kinds and how each is answered.</summary>
    internal static HelpSection AnswerThePosition { get; } =
        new("help-answer-the-position", "Answer the position");

    /// <summary>The click vocabulary of play entry.</summary>
    internal static HelpSection MakingACheckerPlay { get; } =
        new("help-checker-play", "Making a checker play");

    /// <summary>How a submitted answer is scored.</summary>
    internal static HelpSection Scoring { get; } = new("help-scoring", "Scoring");

    /// <summary>The solution view, Continue and Redo.</summary>
    internal static HelpSection ReviewTheSolution { get; } =
        new("help-review-solution", "Review the solution");

    /// <summary>The mid-quiz scoreboard and the summary page.</summary>
    internal static HelpSection StatsAndFinishing { get; } =
        new("help-stats-and-finishing", "Stats and finishing");

    /// <summary>The across-sessions record and what it needs.</summary>
    internal static HelpSection LifetimeStats { get; } = new("help-lifetime-stats", "Lifetime stats");

    /// <summary>The semantics a user cannot discover by clicking around.</summary>
    internal static HelpSection ThingsWorthKnowing { get; } =
        new("help-things-worth-knowing", "Things worth knowing");

    /// <summary>The beta feedback affordance.</summary>
    internal static HelpSection SendFeedback { get; } = new("help-send-feedback", "Send feedback");

    // -----------------------------------------------------------------------
    //  The parts, in document order.
    // -----------------------------------------------------------------------

    /// <summary>What a reader needs in hand, and what BgQuiz does with it.</summary>
    internal static HelpPart BeforeYouStart { get; } =
        new("help-before-you-start", "Before you start", WhatYouNeed, YourDataStaysYours);

    /// <summary>Everything between opening the app and pressing Start.</summary>
    internal static HelpPart SettingUpAQuiz { get; } =
        new("help-setting-up", "Setting up a quiz",
            PickYourFolder, ChooseFilters, SaveFilters, WeightedMix);

    /// <summary>The problem in front of the reader.</summary>
    internal static HelpPart Answering { get; } =
        new("help-answering", "Answering", AnswerThePosition, MakingACheckerPlay, Scoring);

    /// <summary>What follows a submitted answer, and a finished run.</summary>
    internal static HelpPart AfterTheQuiz { get; } =
        new("help-after-the-quiz", "After the quiz",
            ReviewTheSolution, StatsAndFinishing, LifetimeStats);

    /// <summary>What belongs to no single step.</summary>
    internal static HelpPart Reference { get; } =
        new("help-reference", "Reference", ThingsWorthKnowing, SendFeedback);

    /// <summary>
    /// The page's parts, in document order — the table the headings, the
    /// contents block and the structural pins all render or read from.
    /// </summary>
    internal static IReadOnlyList<HelpPart> Parts { get; } =
        [BeforeYouStart, SettingUpAQuiz, Answering, AfterTheQuiz, Reference];
}
