namespace BgQuiz_Blazor.Client.Quiz;

using System.Buffers;
using System.Text;
using System.Text.Json;
using BackgammonDiagram_Lib;
using BgDataTypes_Lib;
using Microsoft.JSInterop;
using XgFilter_Razor;

/// <summary>
/// The per-app (Scoped, one-per-tab in WASM) <b>user settings</b> the
/// <c>Settings</c> page edits: which side the home board renders on, whether
/// that side is re-rolled per problem, whether the board is maximized while the
/// user answers, how the solution's candidate list is ordered and which shallow
/// evaluations are left out of it, and whether the
/// navigation panel stays folded. Every setting is recorded and persisted the
/// moment it is changed — there is no Apply gesture anywhere in this service.
/// When each becomes <i>visible</i> is a separate question, and the fold answers
/// it differently from every other setting here: see
/// <see cref="SetKeepNavigationPanelFoldedAsync"/>.
///
/// <para>
/// <b>No draft, no commit, no dirty flag — deliberately.</b> Unlike the
/// start-gate state (<see cref="AppliedFilter"/>, <see cref="MixConsent"/> +
/// <see cref="MixDraft"/>), nothing here is composed into a quiz at a Start
/// gesture, so there is no half-edited state to guard against and no gate to
/// derive: a toggle is a complete, immediately valid choice, the same reasoning
/// <see cref="ShuffleOption"/> records. Introducing a draft/commit lifetime split
/// is what produced finding (AK)'s wedge; this service must not grow one.
/// </para>
///
/// <para>
/// <b>No <c>Changed</c> event.</b> The <c>Settings</c> page binds straight to
/// these properties and is the only component that renders them, so there is no
/// second consumer to notify — the state-container pattern
/// <see cref="MixDraft.Changed"/> exists for buys nothing here. Add notify
/// plumbing if (and only if) a real simultaneous consumer appears.
/// </para>
///
/// <para>
/// <b>The producer's vocabulary is spoken here, never at a call site.</b> Two of
/// these settings mean a producer type: the review diagram's candidate ordering
/// and the depth ceiling below which candidates are hidden (issues
/// <c>halheinrich/backgammon#150</c> and <c>halheinrich/backgammon#66</c>). They
/// are deliberately shaped differently, and the difference is the whole lesson.
/// The ordering is a <b>checkbox</b>, so it is exposed twice — the stored
/// <c>bool</c> the control binds to, and the <see cref="DiagramRequest"/>-shaped
/// projection the request is built from
/// (<see cref="EffectiveCandidateOrdering"/>) — for the reason
/// <see cref="EffectiveHomeBoardOnRight"/> exists: the rule that turns a choice
/// into what the renderer is asked for belongs in exactly one place.
/// </para>
///
/// <para>
/// The depth ceiling has <b>no such projection, on purpose</b>. It was a
/// checkbox too, storing a <c>bool</c> that a <c>ShallowCandidateFloor</c>
/// constant turned into the producer's old inclusive-show floor — "4-ply and
/// below" meaning <c>AnalysisLevel.Ply5</c>, an off-by-one this type existed to
/// hold in one place. The 2026-08-29 ruling on
/// <c>halheinrich/backgammon#66</c> replaced the checkbox with a dropdown over
/// the level ladder itself, and the producer was re-cut to match
/// (<see cref="DiagramRequest.MaximumHiddenCandidateAnalysisLevel"/>, an
/// <i>inclusive-hide</i> ceiling): the level the user names is exactly the level
/// the request carries, "show only rollouts" included. So the arithmetic is not
/// centralized here — it is <b>gone</b>, and
/// <see cref="MaximumHiddenCandidateAnalysisLevel"/> is a single member that
/// both the control and the request read. Re-introducing a projection over it
/// would be re-introducing the drift the flip removed.
/// </para>
///
/// <para>
/// <b>Defaults are the product's own answers, not a migration.</b> The home
/// board on the right (the producer's own <c>DiagramRequest.HomeBoardOnRight</c>
/// default), no per-problem randomization, the navigation panel unfolded — and
/// the board maximized while answering, which is the one default that does
/// <i>not</i> reproduce the pre-settings page. It was ruled on 2026-08-19
/// (<c>SPEC-quiz-view.md</c> §3, issue <c>halheinrich/backgammon#113</c>): see
/// <see cref="MaximizeBoardWhileAnswering"/> for why, and for the absent-field
/// asymmetry that lets a default change without a migration.
/// </para>
///
/// <para>
/// <b>Persistence.</b> One localStorage key (<see cref="StorageKey"/>) holding
/// every setting as one JSON object — see <see cref="ToJson"/> for the wire
/// format and why it is pinned by a test. <see cref="EnsureHydratedAsync"/> is
/// idempotent (the <see cref="MixDraft.EnsureHydratedAsync"/> pattern) but needs
/// no stale-read generation guard: settings have no per-setup lifecycle, so
/// there is no <c>Discard</c> for a read in flight to land behind.
/// </para>
///
/// <para>
/// <b>Hydrate before the first board renders.</b> <c>Home</c> kicks hydration off
/// in its init — every quiz starts there — and <c>Quiz</c> awaits the same cached
/// task, which by then is already completed and therefore costs no extra render
/// pass. That ordering is what keeps a board from painting on the default side
/// and flipping a frame later.
/// </para>
/// </summary>
internal sealed class QuizSettings(IJSRuntime js)
{
    /// <summary>
    /// The single localStorage key holding every setting as one JSON object.
    /// <c>camelCase</c> after the <c>xg_</c> prefix, per the key family
    /// <see cref="MixDraft.StorageKey"/> established.
    ///
    /// <para>
    /// <b><c>internal</c>, and named for its siblings</b> — <c>Help</c>'s data
    /// section names this entry to the user and renders it from here, so the name
    /// a reader verifies in devtools cannot drift from the name this type writes
    /// under. Same discipline, and the same deliberate stop short of
    /// <c>public</c>, as <see cref="QuizLiveMarker.StorageKey"/>.
    /// </para>
    /// </summary>
    internal const string StorageKey = "xg_quizSettings";

    // The wire property names. Fixed strings, never a naming policy: this
    // payload is read by two languages (this service and the navFold.js
    // applier) and the exact bytes are pinned by a test — see ToJson.
    private const string HomeBoardOnRightField = "homeBoardOnRight";
    private const string RandomizeSidePerProblemField = "randomizeSidePerProblem";
    private const string KeepNavigationPanelFoldedField = "keepNavigationPanelFolded";
    private const string MaximizeBoardWhileAnsweringField = "maximizeBoardWhileAnswering";
    private const string SortAnalysisByDepthFirstField = "sortAnalysisByDepthFirst";
    private const string MaximumHiddenCandidateAnalysisLevelField =
        "maximumHiddenCandidateAnalysisLevel";

    // The defaults, named once so the property initializers and the
    // missing-field fallbacks in Restore cannot disagree.
    private const bool DefaultHomeBoardOnRight = true;
    private const bool DefaultRandomizeSidePerProblem = false;
    private const bool DefaultKeepNavigationPanelFolded = false;
    private const bool DefaultMaximizeBoardWhileAnswering = true;
    private const bool DefaultSortAnalysisByDepthFirst = false;

    // static readonly rather than const only because a nullable enum cannot be
    // const; it is named for the same reason its five neighbours are.
    private static readonly AnalysisLevel? DefaultMaximumHiddenCandidateAnalysisLevel = null;

    /// <summary>
    /// Every level a user may name as the hide ceiling, in the
    /// <see cref="AnalysisLevel"/> declaration order — which that enum makes
    /// <b>contractual</b>, so this is XG's own rigor ladder with the ply and XG
    /// Roller families interleaved (…3-ply, XG Roller, 4-ply, XG Roller+,
    /// 5-ply…) rather than two blocks. Enumerated from the enum rather than
    /// listed, so a level added to the ladder is offered here the day it lands.
    ///
    /// <para>
    /// <see cref="AnalysisLevel.Unknown"/> is the one exclusion, and it is the
    /// producer's rule rather than a UI preference: clause (a) of the level
    /// contract puts Unknown <i>outside</i> the rigor scale — it means "level
    /// not recorded", so it is never a threshold, and
    /// <see cref="DiagramRequest.Builder.Build"/> rejects it outright. "Hide
    /// nothing" is spelled <c>null</c>, never Unknown.
    /// </para>
    ///
    /// <para>
    /// This is the dropdown's whole content and the only place the offered set
    /// is decided: the page renders it and <see cref="LevelFromToken"/> is bound
    /// by it, so neither can drift from the other or from the enum.
    /// </para>
    /// </summary>
    public static IReadOnlyList<AnalysisLevel> HideableLevels { get; } =
        Enum.GetValues<AnalysisLevel>().Where(l => l != AnalysisLevel.Unknown).ToArray();

    /// <summary>
    /// The global the <c>navFold.js</c> applier publishes — the only way to move
    /// the fold without a navigation. C# cannot reach the control itself (an
    /// uncontrolled checkbox in the statically rendered host layout, see
    /// <c>MainLayoutTests</c>), so this seam exists at all for the one direction
    /// that must not wait: unfolding. The DOM selector stays in the JS module
    /// rather than being restated here.
    /// </summary>
    private const string NavFoldApplyFunction = "bgquizNavFold.apply";

    /// <summary>
    /// True when the on-roll player's home board renders on the right — the
    /// producer's <c>DiagramRequest.HomeBoardOnRight</c>. Governs the board only
    /// while <see cref="RandomizeSidePerProblem"/> is off; compose the two through
    /// <see cref="EffectiveHomeBoardOnRight"/>, never by hand.
    /// </summary>
    public bool HomeBoardOnRight { get; private set; } = DefaultHomeBoardOnRight;

    /// <summary>
    /// True when each problem draws its own side, so a position the user has seen
    /// before can come back mirrored (the anti-memorization ask, and closer to
    /// live play, where the home board's side is not yours to choose).
    /// </summary>
    public bool RandomizeSidePerProblem { get; private set; } = DefaultRandomizeSidePerProblem;

    /// <summary>
    /// True when the user wants the board given as much of the page as it can
    /// take <b>while they answer</b> — the low-vision ask behind issue
    /// <c>halheinrich/backgammon#41</c>, and the one setting here that reverses a
    /// documented invariant rather than choosing between equals.
    ///
    /// <para>
    /// This service records the <i>choice</i> only. What it composes into — the
    /// suppressed score panel and status strip, and the board-only diagram canvas
    /// — is the <c>Quiz</c> page's derivation from this and the answering/review
    /// fact, and lives there in one member. Nothing anywhere stores "currently
    /// maximized": the view mode is a pure derivation
    /// (<c>SPEC-quiz-view.md</c> §6), so a second copy of it would be a
    /// divergence from the model, not an implementation detail.
    /// </para>
    ///
    /// <para>
    /// <b>A choice, never consent</b> (<c>SPEC-filtering.md</c> §4's vocabulary):
    /// it survives navigation, reload, and quiz boundaries alike, and no gesture
    /// expires it.
    /// </para>
    ///
    /// <para>
    /// <b>Default on</b> (<c>SPEC-quiz-view.md</c> §3, amended 2026-08-19 by
    /// issue <c>halheinrich/backgammon#113</c>). It shipped default <i>off</i>,
    /// on the grounds that off reproduced the pre-arc page exactly — a
    /// migration-safety argument with no installed base to protect. With that
    /// reason gone the default states the product's own answer to <i>how large
    /// should the board be while you answer</i>: as large as possible, which is
    /// the legibility the mode was built for.
    /// </para>
    ///
    /// <para>
    /// <b>The flip reaches only the users who never chose.</b>
    /// <see cref="Restore"/> maps an <i>absent</i> field to this default, so a
    /// stored explicit <c>false</c> keeps winning and no migration runs — which
    /// is what made the default safe to change at all. The asymmetry is the
    /// feature, and it is pinned from both sides in <c>QuizSettingsTests</c>.
    /// </para>
    /// </summary>
    public bool MaximizeBoardWhileAnswering { get; private set; } =
        DefaultMaximizeBoardWhileAnswering;

    /// <summary>
    /// True when the user wants the solution's candidate list ordered by how
    /// deeply each play was analyzed rather than by equity — the reviewer's ask
    /// behind issue <c>halheinrich/backgammon#150</c>. Someone who rolls out the
    /// best play of each thematic category then has to hunt those rollouts back
    /// out of an equity order that scatters them; depth-first puts the analysis
    /// they came to read at the top.
    ///
    /// <para>
    /// The stored choice only. What the request carries is
    /// <see cref="EffectiveCandidateOrdering"/>, and no call site maps between
    /// the two.
    /// </para>
    /// </summary>
    public bool SortAnalysisByDepthFirst { get; private set; } =
        DefaultSortAnalysisByDepthFirst;

    /// <summary>
    /// The deepest evaluation the user wants left out of the solution's
    /// candidate list — the ask behind issue <c>halheinrich/backgammon#66</c>,
    /// and <b>verbatim</b> the review request's
    /// <see cref="DiagramRequest.MaximumHiddenCandidateAnalysisLevel"/>. The
    /// producer's ceiling is <i>inclusive</i>: the named level and every lesser
    /// one on the rigor ladder are hidden. <c>null</c> — the default and the
    /// producer's own — hides nothing.
    ///
    /// <para>
    /// <b>A dropdown, not a checkbox</b> (ruled 2026-08-28/29). It shipped as a
    /// checkbox meaning a fixed "4-ply and below", and the level ladder is the
    /// choice the ask was actually about: <i>show only rollouts</i> — the top of
    /// the ladder, <see cref="AnalysisLevel.XgRollerPlusPlus"/> — is a selection
    /// a checkbox cannot offer at all. It is also the selection that decided the
    /// producer's shape: an inclusive-hide ceiling can express it, an
    /// inclusive-show floor cannot (there is no member above XG Roller++ to
    /// stand as the floor). The offered set is
    /// <see cref="HideableLevels"/>.
    /// </para>
    ///
    /// <para>
    /// <b>No projection, and none wanted.</b> The producer flip is what makes
    /// the ruled UI semantics <i>be</i> the producer semantics, so this is the
    /// stored choice and the request value at once — see the type's own docs for
    /// why re-introducing an <c>Effective…</c> member here would put back the
    /// drift the flip removed. Nothing but <c>null</c> and a member of
    /// <see cref="HideableLevels"/> can ever be in here:
    /// <see cref="SetMaximumHiddenCandidateAnalysisLevelAsync"/> refuses
    /// anything else, so a stored value can never be one the producer would
    /// throw on.
    /// </para>
    ///
    /// <para>
    /// <b>What it can never hide is the producer's contract, not this
    /// service's.</b> The best play, the play actually recorded, and the user's
    /// own answer stay visible whatever their depth, as do rollout-family and
    /// unstamped candidates — so this setting can thin the list but cannot cost
    /// the user the rows a review exists to show, at the top of the ladder
    /// included. Nothing here re-states that; the fine print on the Settings
    /// page tells the user, and the producer enforces it.
    /// </para>
    /// </summary>
    public AnalysisLevel? MaximumHiddenCandidateAnalysisLevel { get; private set; } =
        DefaultMaximumHiddenCandidateAnalysisLevel;

    /// <summary>
    /// True when the user wants the navigation panel to stay folded. This service
    /// owns and persists the value; it cannot restore the fold itself — that is
    /// <c>navFold.js</c>'s job, for the reasons in <see cref="NavFoldApplyFunction"/>.
    /// </summary>
    public bool KeepNavigationPanelFolded { get; private set; } = DefaultKeepNavigationPanelFolded;

    /// <summary>
    /// <b>The one place the two side settings compose.</b> The side a board
    /// actually renders on is this and nothing else: the per-problem roll when
    /// the user asked for randomization, the fixed choice otherwise. Both of
    /// <c>Quiz</c>'s request builders read a single property that calls this, so
    /// no call site re-states the rule and no render branch can drift — a board
    /// that flipped in some views but not others would read as a bug.
    /// </summary>
    /// <param name="randomSide">
    /// The current problem's roll — <see cref="QuizController.RandomHomeBoardOnRight"/>,
    /// which the controller takes unconditionally per problem and holds steady
    /// across submit, review, and redo. Passing it in is what keeps the
    /// controller free of any knowledge of this service.
    /// </param>
    public bool EffectiveHomeBoardOnRight(bool randomSide) =>
        RandomizeSidePerProblem ? randomSide : HomeBoardOnRight;

    /// <summary>
    /// <see cref="SortAnalysisByDepthFirst"/> as the review request's
    /// <see cref="DiagramRequest.CandidateOrdering"/> — the
    /// <see cref="EffectiveHomeBoardOnRight"/> discipline applied to a setting
    /// whose two answers are a producer enum.
    ///
    /// <para>
    /// Off is <see cref="CandidateOrdering.Equity"/>, which the producer defines
    /// as the caller's list order rendered unchanged. So a request built from
    /// this with the setting off is byte-identical to one that never mentioned
    /// ordering, and the call site needs no "leave it alone" branch — passing
    /// the default <i>is</i> passing nothing.
    /// </para>
    /// </summary>
    public CandidateOrdering EffectiveCandidateOrdering =>
        SortAnalysisByDepthFirst ? CandidateOrdering.DepthFirst : CandidateOrdering.Equity;

    /// <summary>
    /// The <see cref="MaximumHiddenCandidateAnalysisLevel"/> token vocabulary's
    /// write half: a level as its <b>member name</b>, and "hide nothing" as the
    /// empty string. Two readers share it — the <c>&lt;select&gt;</c> on the
    /// Settings page (option values and the current selection) and
    /// <see cref="ToJson"/> (which spells the empty case as JSON <c>null</c>,
    /// the idiom of its own medium) — so the spelling is decided here once and
    /// read back by <see cref="LevelFromToken"/> alone.
    ///
    /// <para>
    /// The member name, deliberately, and not the label or the ordinal. The
    /// label is UI text the producer may reword; the ordinal moves whenever a
    /// level is inserted into the ladder (as <see cref="AnalysisLevel.Ply3Red"/>
    /// was). The name is the same token the enum's own
    /// <c>JsonStringEnumConverter</c> writes, and it is stable across both.
    /// </para>
    /// </summary>
    internal static string ToLevelToken(AnalysisLevel? level) =>
        level?.ToString() ?? string.Empty;

    /// <summary>
    /// The read half of <see cref="ToLevelToken"/>, and the <b>only</b> way a
    /// token becomes a level. Tolerant by contract, because both its callers
    /// read text this app does not control: a stored payload a devtools session
    /// may have edited, and a form value a browser may post. Anything that is
    /// not a token <see cref="ToLevelToken"/> itself would have written — the
    /// empty string, <c>null</c>, an unrecognized word, a numeric ordinal, a
    /// display label, a differently-cased name, or <c>"Unknown"</c> — reads as
    /// "hide nothing" rather than throwing.
    ///
    /// <para>
    /// <b>Defined as the inverse of the write half</b>, by searching the offered
    /// levels for the one whose token this is, rather than by parsing. That is
    /// what makes the two halves one vocabulary instead of two that agree by
    /// inspection, and it closes what parsing leaves open:
    /// <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/> accepts a numeric
    /// ordinal and is case-insensitive by default, so <c>"6"</c> parses as
    /// <see cref="AnalysisLevel.Ply4"/> — a <i>defined, offered</i> level, which
    /// no membership test would reject. Silently honouring an ordinal would
    /// re-couple this durable payload to enum numbering that the ladder's own
    /// contract lets move (<see cref="AnalysisLevel.Ply3Red"/> moved all of it).
    /// </para>
    /// </summary>
    internal static AnalysisLevel? LevelFromToken(string? token) =>
        HideableLevels.Cast<AnalysisLevel?>().FirstOrDefault(l => ToLevelToken(l) == token);

    /// <summary>
    /// The completed (or in-flight) hydration, so <see cref="EnsureHydratedAsync"/>
    /// reads localStorage once per app however many callers ask.
    /// </summary>
    private Task? _hydration;

    /// <summary>
    /// Load the stored settings — once per app. The first caller runs the read;
    /// later callers get the cached task back, which (being already completed)
    /// an <c>OnInitializedAsync</c> can await without provoking a second render.
    /// A missing key leaves the defaults standing, and a malformed payload does
    /// the same rather than throwing — see <see cref="Restore"/>.
    /// </summary>
    public Task EnsureHydratedAsync() => _hydration ??= HydrateAsync();

    private async Task HydrateAsync()
    {
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        Restore(stored);
    }

    /// <summary>Record the home-board side, applying and persisting immediately.</summary>
    public Task SetHomeBoardOnRightAsync(bool value)
    {
        HomeBoardOnRight = value;
        return PersistAsync();
    }

    /// <summary>Record the randomize-per-problem choice, applying and persisting immediately.</summary>
    public Task SetRandomizeSidePerProblemAsync(bool value)
    {
        RandomizeSidePerProblem = value;
        return PersistAsync();
    }

    /// <summary>
    /// Record the maximize-while-answering choice, applying and persisting
    /// immediately. Like the two side settings and unlike the fold, there is
    /// nothing to defer: the next quiz render derives the view mode from the new
    /// value, and no page the user is standing in gets pulled out from under
    /// them by it.
    /// </summary>
    public Task SetMaximizeBoardWhileAnsweringAsync(bool value)
    {
        MaximizeBoardWhileAnswering = value;
        return PersistAsync();
    }

    /// <summary>
    /// Record the depth-first ordering choice, applying and persisting
    /// immediately. Like every setting but the fold there is nothing to defer:
    /// the next solution the user reads is built from the new value.
    /// </summary>
    public Task SetSortAnalysisByDepthFirstAsync(bool value)
    {
        SortAnalysisByDepthFirst = value;
        return PersistAsync();
    }

    /// <summary>
    /// Record the hide ceiling, applying and persisting immediately — the same
    /// non-deferral as <see cref="SetSortAnalysisByDepthFirstAsync"/>.
    /// </summary>
    /// <param name="value">
    /// A member of <see cref="HideableLevels"/>, or <c>null</c> to hide nothing.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="value"/> is <see cref="AnalysisLevel.Unknown"/> or an
    /// undefined enum value. This is the one setter here that can be handed an
    /// unusable argument — the others take a <c>bool</c>, which has no invalid
    /// value — and refusing it is what keeps
    /// <see cref="MaximumHiddenCandidateAnalysisLevel"/> unable to hold anything
    /// <see cref="DiagramRequest.Builder.Build"/> would throw on. Unreachable
    /// from the Settings page, whose every option comes from
    /// <see cref="HideableLevels"/> and is read back through
    /// <see cref="LevelFromToken"/>; that is what a guard should look like.
    /// </exception>
    public Task SetMaximumHiddenCandidateAnalysisLevelAsync(AnalysisLevel? value)
    {
        if (value is AnalysisLevel level && !HideableLevels.Contains(level))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Not a level a candidate list can be thinned to; use null to hide nothing.");
        }

        MaximumHiddenCandidateAnalysisLevel = value;
        return PersistAsync();
    }

    /// <summary>
    /// Record the keep-folded choice and persist it — then <b>unfold now, but
    /// never fold now</b>. The asymmetry is the whole contract, and it is
    /// deliberate (finding #50).
    ///
    /// <para>
    /// <b>Turning it on defers to the next navigation.</b> "Keep the navigation
    /// panel folded" describes how pages <i>start</i>, not a fold to perform on
    /// the spot; folding the page the user is standing in strands them — the
    /// panel they just used to get here vanishes, and the checkbox they are
    /// looking at is the only thing that could tell them why. Nothing extra is
    /// needed to defer it: the choice is already in storage, and
    /// <c>navFold.js</c>'s <c>enhancedload</c> handler applies it on the next
    /// navigation, which is also where it self-demonstrates. The checkbox is the
    /// confirmation in the meantime.
    /// </para>
    ///
    /// <para>
    /// <b>Turning it off cannot wait.</b> The user is asking for the panel back,
    /// and with the panel folded there is no navigation available to them that
    /// would deliver it — the links are folded away with it. So the unfold goes
    /// through the applier immediately; without it the setting would be a
    /// one-way door.
    /// </para>
    ///
    /// <para>
    /// The applier is handed its argument <b>explicitly</b> rather than left to
    /// re-read localStorage, deliberately: the seam then carries no dependency on
    /// this method's write having landed first, so the persist and the unfold
    /// below cannot be reordered into a silent bug. It is passed the literal
    /// <c>false</c> rather than <c>value</c> — inside that branch they are the
    /// same bool, and the literal is the one that says <i>unfold</i> at the call
    /// site. (The applier's <i>own</i> storage read stays where it belongs — on
    /// the navigation path, where no C# is running.)
    /// </para>
    /// </summary>
    public async Task SetKeepNavigationPanelFoldedAsync(bool value)
    {
        KeepNavigationPanelFolded = value;
        await PersistAsync();
        if (!value)
        {
            await js.InvokeVoidAsync(NavFoldApplyFunction, false);
        }
    }

    private async Task PersistAsync() =>
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, ToJson());

    /// <summary>
    /// Serialize every setting as one JSON object, hand-written with fixed
    /// property names — the same posture <c>QuizMixJsonConverter</c> takes, and
    /// for a stronger reason here: this payload is a <b>two-language contract</b>.
    /// The <c>navFold.js</c> applier reads
    /// <see cref="KeepNavigationPanelFoldedField"/> out of this object with no
    /// compiler to check it, so the serialized shape — field names included — is
    /// pinned by a unit test asserting the literal bytes. That test, not this
    /// method, is the name's single source of truth for the JS side; renaming a
    /// field here without updating the applier fails there rather than in
    /// production. Every field is always written, so a reader never has to
    /// distinguish "absent" from "false".
    ///
    /// <para>
    /// <b>Field order is append-only.</b> A new setting is written after the
    /// existing ones however the properties above are grouped — the payload is a
    /// durable format two readers share, so the bytes an older build wrote stay a
    /// prefix of the bytes a newer one writes. (Order carries no meaning to
    /// <see cref="Restore"/>, which reads by name; it matters only to the pinned
    /// literal, and keeping it stable is what makes that pin's diff say
    /// "a field was added" rather than "the format changed".)
    /// </para>
    ///
    /// <para>
    /// <b>The one field ever retired</b> is <c>hideShallowCandidates</c>, the
    /// checkbox <see cref="MaximumHiddenCandidateAnalysisLevel"/> replaced. It
    /// was the last field written, so every other field kept its position and
    /// the nullable level took its place at the end. Retiring it cost no
    /// migration for two independent reasons: it never shipped in a release, so
    /// no user's browser holds one; and <see cref="Restore"/> ignores fields it
    /// does not know, so a developer's leftover entry restores to the current
    /// default — hide nothing — exactly as an absent field would. A field with
    /// an installed base would need the opposite treatment (read the old name,
    /// write the new), and this note is here so the next retirement asks that
    /// question rather than copying this one.
    /// </para>
    /// </summary>
    private string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean(HomeBoardOnRightField, HomeBoardOnRight);
            writer.WriteBoolean(RandomizeSidePerProblemField, RandomizeSidePerProblem);
            writer.WriteBoolean(KeepNavigationPanelFoldedField, KeepNavigationPanelFolded);
            writer.WriteBoolean(MaximizeBoardWhileAnsweringField, MaximizeBoardWhileAnswering);
            writer.WriteBoolean(SortAnalysisByDepthFirstField, SortAnalysisByDepthFirst);
            // The only non-boolean field, and the only one whose "unset" is a
            // JSON null rather than a false: the setting's own default is the
            // producer's null, and "hide nothing" has no level to name. Written
            // as the level's member name (ToLevelToken), never its label or its
            // ordinal.
            if (MaximumHiddenCandidateAnalysisLevel is AnalysisLevel ceiling)
            {
                writer.WriteString(
                    MaximumHiddenCandidateAnalysisLevelField, ToLevelToken(ceiling));
            }
            else
            {
                writer.WriteNull(MaximumHiddenCandidateAnalysisLevelField);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Project a stored payload onto the settings — <b>tolerantly, and never
    /// throwing</b>. A durable format with two readers that later legs will
    /// extend cannot afford a fail-loud restore: a payload written by a newer
    /// build (extra fields), an older one (missing fields), or a hand-edited
    /// devtools session must all leave the app usable. So a missing or
    /// wrongly-typed field falls back to that setting's default, an unknown
    /// field is ignored — including <c>hideShallowCandidates</c>, the one field
    /// this format has retired (see <see cref="ToJson"/>) — and anything that is
    /// not a JSON object, including outright malformed text, leaves every
    /// default standing. Values are read into locals first so a partial read can
    /// never assign a mix of stored and default state.
    /// </summary>
    private void Restore(string? json)
    {
        if (json is null) return; // never written — the defaults already stand

        bool homeBoardOnRight;
        bool randomizeSidePerProblem;
        bool keepNavigationPanelFolded;
        bool maximizeBoardWhileAnswering;
        bool sortAnalysisByDepthFirst;
        AnalysisLevel? maximumHiddenCandidateAnalysisLevel;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            homeBoardOnRight = ReadBool(root, HomeBoardOnRightField, DefaultHomeBoardOnRight);
            randomizeSidePerProblem =
                ReadBool(root, RandomizeSidePerProblemField, DefaultRandomizeSidePerProblem);
            keepNavigationPanelFolded =
                ReadBool(root, KeepNavigationPanelFoldedField, DefaultKeepNavigationPanelFolded);
            // Absent from every payload written before this setting existed, and
            // therefore the field the tolerance rule above exists for: an old
            // entry restores with the CURRENT default, whatever that is. That is
            // what let the default flip to on (#113) with no migration and no
            // version stamp — the flip reaches exactly the users who never chose,
            // while an explicit stored false keeps winning.
            maximizeBoardWhileAnswering =
                ReadBool(root, MaximizeBoardWhileAnsweringField, DefaultMaximizeBoardWhileAnswering);
            // Both absent from every payload written before the depth treatment,
            // and both defaulting to "untreated" — so an entry from an older
            // build restores to exactly today's rendering, which is what lets
            // these ship with no migration and no version stamp.
            sortAnalysisByDepthFirst =
                ReadBool(root, SortAnalysisByDepthFirstField, DefaultSortAnalysisByDepthFirst);
            maximumHiddenCandidateAnalysisLevel = ReadLevel(
                root,
                MaximumHiddenCandidateAnalysisLevelField,
                DefaultMaximumHiddenCandidateAnalysisLevel);
        }
        catch (JsonException)
        {
            return; // malformed — every default stands, exactly as on a fresh browser
        }

        HomeBoardOnRight = homeBoardOnRight;
        RandomizeSidePerProblem = randomizeSidePerProblem;
        KeepNavigationPanelFolded = keepNavigationPanelFolded;
        MaximizeBoardWhileAnswering = maximizeBoardWhileAnswering;
        SortAnalysisByDepthFirst = sortAnalysisByDepthFirst;
        MaximumHiddenCandidateAnalysisLevel = maximumHiddenCandidateAnalysisLevel;
    }

    private static bool ReadBool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    /// <summary>
    /// <see cref="ReadBool"/>'s counterpart for the one non-boolean field,
    /// holding the same two rules — an unusable value falls back to the
    /// setting's default, and an explicit stored choice outranks that default.
    /// The second rule is what makes the JSON-null case its own branch rather
    /// than another failure: <c>null</c> is how this format spells the user's
    /// explicit "hide nothing", and it has to keep winning if the default ever
    /// stops being null, exactly as an explicit stored <c>false</c> does for the
    /// maximize field. Everything else unusable — absent, a non-string, or a
    /// string <see cref="LevelFromToken"/> does not recognize — is the default.
    /// </summary>
    private static AnalysisLevel? ReadLevel(
        JsonElement root, string name, AnalysisLevel? fallback)
    {
        if (!root.TryGetProperty(name, out var value)) return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => LevelFromToken(value.GetString()) ?? fallback,
            _ => fallback,
        };
    }
}
