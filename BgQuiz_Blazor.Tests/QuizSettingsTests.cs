using BackgammonDiagram_Lib;
using BgDataTypes_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Bunit;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="QuizSettings"/> — the app-scoped user settings and the
/// one localStorage entry behind them. Four things are pinned here: the
/// defaults (the product's own answers, no longer a reproduction of the
/// pre-settings app — see <see cref="FreshSettings_AreTheProductsOwnAnswers"/>),
/// the <b>producer-shaped values</b> the request is actually built from (see
/// <see cref="FreshSettings_AskTheProducerForNothing"/>, and
/// <see cref="HideableLevels_AreTheLevelLadder_WithoutUnknown"/> for the one set
/// this type decides rather than stores), the <b>serialized wire
/// format</b> byte-for-byte (a durable payload with a second reader in another
/// language — see <see cref="Persist_WritesThePinnedWireFormat"/>), and the
/// tolerance rules a format that later legs will extend has to hold. Extends
/// <see cref="BunitContext"/> only for the JSInterop double behind the storage
/// reads/writes and the fold applier.
/// </summary>
public class QuizSettingsTests : BunitContext
{
    public QuizSettingsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // getItem → null unless a test sets a value
    }

    private QuizSettings NewSettings() => new(JSInterop.JSRuntime);

    /// <summary>The JSON last written under the settings key.</summary>
    private string? LastPersisted() =>
        JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == QuizSettings.StorageKey)
            .Arguments[1] as string;

    private void StageStored(string? json) =>
        JSInterop.Setup<string?>("localStorage.getItem", QuizSettings.StorageKey).SetResult(json);

    // -----------------------------------------------------------------------
    //  Defaults
    // -----------------------------------------------------------------------

    [Fact]
    public void FreshSettings_AreTheProductsOwnAnswers()
    {
        // What a user who never opens the Settings page gets. All but one are
        // still the pre-settings app — home board on the right (the producer's
        // own DiagramRequest default), no randomization, navigation panel
        // unfolded, and the solution's candidate list untreated (issues
        // halheinrich/backgammon#150 and halheinrich/backgammon#66 both ship
        // off, which is the only default either could take: the producer pins
        // options-unset as byte-identical to today's rendering, so off is what
        // leaves every existing user's review exactly where they left it).
        // The exception is deliberate: the board is maximized while answering
        // (SPEC-quiz-view.md §3, amended 2026-08-19 by issue
        // halheinrich/backgammon#113). This test used to assert that the defaults
        // REPRODUCED the pre-settings app, which was a migration-safety claim
        // about an installed base that does not exist pre-beta; the default now
        // states the product's answer instead of preserving history.
        var settings = NewSettings();

        Assert.True(settings.HomeBoardOnRight);
        Assert.False(settings.RandomizeSidePerProblem);
        Assert.False(settings.KeepNavigationPanelFolded);
        Assert.True(settings.MaximizeBoardWhileAnswering);
        Assert.False(settings.SortAnalysisByDepthFirst);
        Assert.Null(settings.MaximumHiddenCandidateAnalysisLevel);
    }

    [Fact]
    public async Task Hydrate_NoStoredEntry_LeavesEveryDefaultStanding()
    {
        // Loose mode answers getItem with null — a browser that has never seen
        // this app.
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.True(settings.HomeBoardOnRight);
        Assert.False(settings.RandomizeSidePerProblem);
        Assert.False(settings.KeepNavigationPanelFolded);
        Assert.True(settings.MaximizeBoardWhileAnswering);
        Assert.False(settings.SortAnalysisByDepthFirst);
        Assert.Null(settings.MaximumHiddenCandidateAnalysisLevel);
    }

    // -----------------------------------------------------------------------
    //  The effective-side rule — the one member that composes the two settings
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task EffectiveSide_RandomizeOff_IsTheFixedChoice_WhateverTheRoll(
        bool fixedSide, bool expected)
    {
        // The roll is taken unconditionally by the controller, so "randomize off"
        // has to mean the roll is ignored — not that no roll happened.
        var settings = NewSettings();
        await settings.SetHomeBoardOnRightAsync(fixedSide);

        Assert.Equal(expected, settings.EffectiveHomeBoardOnRight(randomSide: true));
        Assert.Equal(expected, settings.EffectiveHomeBoardOnRight(randomSide: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EffectiveSide_RandomizeOn_IsTheRoll_WhateverTheFixedChoice(bool fixedSide)
    {
        var settings = NewSettings();
        await settings.SetHomeBoardOnRightAsync(fixedSide);
        await settings.SetRandomizeSidePerProblemAsync(true);

        Assert.True(settings.EffectiveHomeBoardOnRight(randomSide: true));
        Assert.False(settings.EffectiveHomeBoardOnRight(randomSide: false));
    }

    // -----------------------------------------------------------------------
    //  The depth treatment (issues halheinrich/backgammon#150 and
    //  halheinrich/backgammon#66). The two settings are deliberately different
    //  shapes and are pinned differently because of it.
    //
    //  The ordering is a checkbox whose two answers are a producer value, so
    //  the MAPPING is the thing worth pinning — it is what a call site would
    //  otherwise restate.
    //
    //  The hide ceiling has no mapping left to pin. It was a checkbox meaning a
    //  fixed AnalysisLevel.Ply5 ("4-ply and below", inclusive-show floor), and
    //  the off-by-one that arithmetic carried was this section's headline
    //  assertion. The producer flip (BackgammonDiagram_Lib 6f41585) replaced the
    //  floor with an inclusive-hide ceiling so the ruled dropdown selection IS
    //  the producer value, and the arithmetic went away rather than moving. What
    //  is left to pin is therefore what this type still decides: WHICH levels it
    //  offers (and that Unknown is not among them), and that a selection reaches
    //  the request and the next session unchanged.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(false, CandidateOrdering.Equity)]
    [InlineData(true, CandidateOrdering.DepthFirst)]
    public async Task DepthFirstSetting_ProjectsToTheRequestsCandidateOrdering(
        bool depthFirst, CandidateOrdering expected)
    {
        // Off is the producer's Equity — its OWN default, which it defines as
        // the caller's list order rendered unchanged. That equality is what
        // lets the call site assign this unconditionally instead of branching
        // on the setting; if off ever stopped meaning Equity, the "passing the
        // default is passing nothing" claim in Quiz.BuildSolutionRequest would
        // become false and this is where it fails.
        var settings = NewSettings();

        await settings.SetSortAnalysisByDepthFirstAsync(depthFirst);

        Assert.Equal(expected, settings.EffectiveCandidateOrdering);
    }

    [Fact]
    public void HideableLevels_AreTheLevelLadder_WithoutUnknown()
    {
        // WHAT the dropdown may offer, which is the one thing about the ceiling
        // this type still decides. Written out rather than re-derived from
        // Enum.GetValues, because a test that re-ran the production expression
        // would agree with any expression at all — including one that let
        // Unknown through.
        //
        // The ORDER is asserted with the membership and is not cosmetic:
        // AnalysisLevel's declaration order is contractual (XG's own menu), the
        // ply and XG Roller families interleave rather than forming two blocks,
        // and the user reads this list as a rigor ladder. A reorder upstream
        // would silently relabel every ceiling's meaning, and this is where it
        // fails.
        //
        // Unknown's absence is the producer's rule, not a UI preference: clause
        // (a) of the level contract puts it outside the rigor scale, and
        // DiagramRequest.Builder.Build throws on it. "Hide nothing" is null.
        Assert.Equal(
            new[]
            {
                AnalysisLevel.Ply1,
                AnalysisLevel.Ply2,
                AnalysisLevel.Ply3Red,
                AnalysisLevel.Ply3,
                AnalysisLevel.XgRoller,
                AnalysisLevel.Ply4,
                AnalysisLevel.XgRollerPlus,
                AnalysisLevel.Ply5,
                AnalysisLevel.Ply6,
                AnalysisLevel.Ply7,
                AnalysisLevel.XgRollerPlusPlus,
            },
            QuizSettings.HideableLevels);

        Assert.DoesNotContain(AnalysisLevel.Unknown, QuizSettings.HideableLevels);
    }

    /// <summary>Every level the dropdown offers, for the theories below.</summary>
    public static TheoryData<AnalysisLevel> EveryHideableLevel() =>
        new(QuizSettings.HideableLevels);

    [Theory]
    [MemberData(nameof(EveryHideableLevel))]
    public async Task EveryOfferedLevel_IsRecordedVerbatim_AndClearsBackToNull(AnalysisLevel level)
    {
        // Verbatim, for every level the dropdown offers — the claim the producer
        // flip was made to support. There is no mapping left between what the
        // user picks and what the request carries, and this is the assertion
        // that would fail the day someone re-introduced one (a successor lookup,
        // an "and below means one up" adjustment of the kind the retired
        // ShallowCandidateFloor constant was).
        //
        // XG Roller++ is in this set and is the case that drove the flip: "show
        // only rollouts" is a real selection, and it is only expressible because
        // the producer's option is an inclusive-HIDE ceiling — an inclusive-show
        // floor would need a member above the top of the ladder to say it.
        var settings = NewSettings();

        await settings.SetMaximumHiddenCandidateAnalysisLevelAsync(level);

        Assert.Equal(level, settings.MaximumHiddenCandidateAnalysisLevel);

        // And back to none — a ceiling that latched would quietly outlive the
        // choice that set it.
        await settings.SetMaximumHiddenCandidateAnalysisLevelAsync(null);

        Assert.Null(settings.MaximumHiddenCandidateAnalysisLevel);
    }

    [Theory]
    [InlineData(AnalysisLevel.Unknown)]
    [InlineData((AnalysisLevel)999)]
    public async Task SettingAnUnusableLevel_IsRefused_RatherThanStored(AnalysisLevel bad)
    {
        // The one setter here that can be handed an invalid argument, and the
        // guard that keeps the stored value inside what the producer accepts:
        // DiagramRequest.Builder.Build throws on both of these, so storing
        // either would turn a settings write into a crash one review later,
        // somewhere else entirely.
        //
        // Unknown is not "the shallowest level" — it means the depth was never
        // recorded (clause (a)), so hiding "through not recorded" is nonsense
        // and null is how you hide nothing.
        var settings = NewSettings();
        await settings.SetMaximumHiddenCandidateAnalysisLevelAsync(AnalysisLevel.Ply4);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => settings.SetMaximumHiddenCandidateAnalysisLevelAsync(bad));

        // Refused, not half-applied: the previous choice is untouched and
        // nothing was persisted over it.
        Assert.Equal(AnalysisLevel.Ply4, settings.MaximumHiddenCandidateAnalysisLevel);
        Assert.Contains("\"maximumHiddenCandidateAnalysisLevel\":\"Ply4\"", LastPersisted());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("Unknown", null)]          // outside the rigor scale — never a threshold
    [InlineData("Ply42", null)]            // not a member
    [InlineData("4-ply", null)]            // the LABEL, not the member name
    [InlineData("6", null)]                // an ordinal — and "6" IS Ply4's number
    [InlineData("ply4", null)]             // the right name, the wrong case
    [InlineData("Ply4", AnalysisLevel.Ply4)]
    [InlineData("XgRollerPlusPlus", AnalysisLevel.XgRollerPlusPlus)]
    public void LevelToken_ReadsMemberNamesOnly_AndFailsToHideNothing(
        string? token, AnalysisLevel? expected)
    {
        // The one token vocabulary, and the only way a string becomes a level.
        // Both its callers read text this app does not control — a devtools-
        // edited storage entry and a browser-posted form value — so every
        // unusable spelling has to land on "hide nothing" rather than throwing
        // or, worse, on a value the producer rejects.
        //
        // The ordinal row is the one worth the ink, and it caught the first cut
        // of this method: Enum.TryParse accepts a number, and "6" is Ply4's —
        // a DEFINED, OFFERED level, so no membership test rejects it. Honouring
        // it would tie this durable payload to enum numbering the ladder's own
        // contract lets move (Ply3Red's insertion moved all of it). Reading the
        // token as the inverse of ToLevelToken rather than as a parse is what
        // closes it, and the case row is the same closure seen from the other
        // side — TryParse is case-insensitive by default, ToLevelToken is not.
        //
        // The label row pins that the token is the member NAME; the label is the
        // producer's UI text and may be reworded without this format moving.
        Assert.Equal(expected, QuizSettings.LevelFromToken(token));
    }

    [Theory]
    [MemberData(nameof(EveryHideableLevel))]
    public void LevelToken_RoundTrips_ForEveryOfferedLevel(AnalysisLevel level)
    {
        // The write half and the read half are one vocabulary, asserted over the
        // whole offered set rather than a sample: the option values the Settings
        // page renders and the payload ToJson writes are both ToLevelToken, and
        // LevelFromToken is the only thing that reads either back.
        Assert.Equal(level, QuizSettings.LevelFromToken(QuizSettings.ToLevelToken(level)));
    }

    [Fact]
    public void LevelToken_HideNothing_IsTheEmptyToken()
    {
        // The empty string, not "null" and not the word Unknown: it is what an
        // HTML <option value=""> posts back, which is why the page can render
        // the "Hide nothing" option without spelling the null itself.
        Assert.Equal(string.Empty, QuizSettings.ToLevelToken(null));
    }

    [Fact]
    public void FreshSettings_AskTheProducerForNothing()
    {
        // The defaults restated as what the RENDERER is asked for, which is the
        // form the no-visual-change promise actually takes: a user who never
        // opens the Settings page produces a request carrying the producer's own
        // Equity/null, i.e. one indistinguishable from a request built before
        // either option existed. Deliberately separate from the bool defaults
        // above — those would stay green if a projection were inverted.
        var settings = NewSettings();

        Assert.Equal(CandidateOrdering.Equity, settings.EffectiveCandidateOrdering);
        Assert.Null(settings.MaximumHiddenCandidateAnalysisLevel);
    }

    // -----------------------------------------------------------------------
    //  Immediate apply + persistence
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EverySetter_RecordsTheValue_AndPersistsImmediately()
    {
        // No Apply button, no draft: the property is the new value the moment
        // the setter returns, and the write has already gone out. This is the
        // half that IS uniform across every one of them — when a change becomes
        // is the fold's own question, pinned in
        // SettingTheFold_ReachesTheApplier_ToUnfoldOnly.
        var settings = NewSettings();

        await settings.SetHomeBoardOnRightAsync(false);
        Assert.False(settings.HomeBoardOnRight);
        Assert.Contains("\"homeBoardOnRight\":false", LastPersisted());

        await settings.SetRandomizeSidePerProblemAsync(true);
        Assert.True(settings.RandomizeSidePerProblem);
        Assert.Contains("\"randomizeSidePerProblem\":true", LastPersisted());

        await settings.SetKeepNavigationPanelFoldedAsync(true);
        Assert.True(settings.KeepNavigationPanelFolded);
        Assert.Contains("\"keepNavigationPanelFolded\":true", LastPersisted());

        await settings.SetMaximizeBoardWhileAnsweringAsync(true);
        Assert.True(settings.MaximizeBoardWhileAnswering);
        Assert.Contains("\"maximizeBoardWhileAnswering\":true", LastPersisted());

        await settings.SetSortAnalysisByDepthFirstAsync(true);
        Assert.True(settings.SortAnalysisByDepthFirst);
        Assert.Contains("\"sortAnalysisByDepthFirst\":true", LastPersisted());

        await settings.SetMaximumHiddenCandidateAnalysisLevelAsync(AnalysisLevel.XgRollerPlusPlus);
        Assert.Equal(AnalysisLevel.XgRollerPlusPlus, settings.MaximumHiddenCandidateAnalysisLevel);
        Assert.Contains(
            "\"maximumHiddenCandidateAnalysisLevel\":\"XgRollerPlusPlus\"", LastPersisted());
    }

    [Fact]
    public async Task Persist_WritesThePinnedWireFormat()
    {
        // THE two-language contract, pinned as literal bytes rather than through
        // the constants that produce them. navFold.js reads
        // "keepNavigationPanelFolded" out of this object with no compiler
        // checking it, so a rename on the C# side has to fail HERE — a test
        // written against the constants would agree with any name at all.
        // Every field is always written, so no reader has to distinguish
        // "absent" from "false".
        //
        // Field order is append-only (see ToJson): maximizeBoardWhileAnswering
        // joined at the END, after the fold field, and the depth-treatment pair
        // after it in turn, however the properties are grouped on the C# side.
        // That is what makes this literal's diff read as "fields were added"
        // rather than "the format moved under the applier".
        //
        // The ceiling is the one field that is not a boolean, and the one
        // position ever reused: hideShallowCandidates was last, so retiring that
        // checkbox for the level dropdown left every other field where it was.
        // Unset is a JSON null — the setting's own default and the producer's,
        // spelled the way JSON spells "no value" rather than as an empty string
        // or the word Unknown.
        var settings = NewSettings();

        await settings.SetRandomizeSidePerProblemAsync(true);

        Assert.Equal(
            """{"homeBoardOnRight":true,"randomizeSidePerProblem":true,"keepNavigationPanelFolded":false,"maximizeBoardWhileAnswering":true,"sortAnalysisByDepthFirst":false,"maximumHiddenCandidateAnalysisLevel":null}""",
            LastPersisted());
    }

    [Fact]
    public async Task Persist_WritesTheChosenLevelAsItsMemberName()
    {
        // The other half of the wire pin: the SET case, which the fresh-settings
        // literal above cannot show. The token is the enum member name — the
        // same spelling the enum's own JsonStringEnumConverter writes — and not
        // its label ("XG Roller++") or its ordinal, either of which would make
        // this entry unreadable after a relabel or a level insertion. Ply3Red's
        // insertion into the middle of the ladder is the precedent: it moved
        // every later ordinal and no token.
        var settings = NewSettings();

        await settings.SetMaximumHiddenCandidateAnalysisLevelAsync(AnalysisLevel.XgRollerPlusPlus);

        Assert.Equal(
            """{"homeBoardOnRight":true,"randomizeSidePerProblem":false,"keepNavigationPanelFolded":false,"maximizeBoardWhileAnswering":true,"sortAnalysisByDepthFirst":false,"maximumHiddenCandidateAnalysisLevel":"XgRollerPlusPlus"}""",
            LastPersisted());
    }

    [Theory]
    [MemberData(nameof(EveryHideableLevel))]
    public async Task PersistedPayload_RoundTripsThroughHydration(AnalysisLevel level)
    {
        // The whole point of the entry: what one app writes, the next app boot
        // reads back identically. Every field is driven AWAY from its own
        // default, without exception — a field left sitting on its default would
        // round-trip green through a reader that ignored the payload entirely.
        // The maximize field was that exception until #113 flipped its default;
        // it now writes false for the same reason the other three write what they
        // write.
        //
        // Run once per offered level rather than on a sample, because the
        // ceiling is the only field whose value space is bigger than a bool: a
        // token written and read through the wrong half of the vocabulary could
        // survive one level and lose another (a case fold, a label, an ordinal),
        // and "the level the user picked last session is the level in force this
        // session" has to hold for all eleven — XG Roller++, the top of the
        // ladder, included.
        var writer = NewSettings();
        await writer.SetHomeBoardOnRightAsync(false);
        await writer.SetRandomizeSidePerProblemAsync(true);
        await writer.SetKeepNavigationPanelFoldedAsync(true);
        await writer.SetMaximizeBoardWhileAnsweringAsync(false);
        await writer.SetSortAnalysisByDepthFirstAsync(true);
        await writer.SetMaximumHiddenCandidateAnalysisLevelAsync(level);

        StageStored(LastPersisted());
        var reader = NewSettings();
        await reader.EnsureHydratedAsync();

        Assert.False(reader.HomeBoardOnRight);
        Assert.True(reader.RandomizeSidePerProblem);
        Assert.True(reader.KeepNavigationPanelFolded);
        Assert.False(reader.MaximizeBoardWhileAnswering);
        Assert.True(reader.SortAnalysisByDepthFirst);
        Assert.Equal(level, reader.MaximumHiddenCandidateAnalysisLevel);
    }

    [Fact]
    public async Task SettingTheFold_ReachesTheApplier_ToUnfoldOnly()
    {
        // THE asymmetry (finding #50), pinned at the seam that carries it.
        //
        // This test used to assert the applier was called in BOTH directions,
        // and that was the shipped contract: "immediate apply" was read as
        // symmetric because every other setting here is. The literal stopped
        // being right when the ruling landed — "keep the navigation panel
        // folded" describes how pages START, so folding the page the user is
        // standing in strands them behind a panel that just vanished. The rule
        // the old assertion protected is intact and still asserted below: a
        // change the user cannot see any other way must reach the applier. Only
        // one direction now qualifies.
        var settings = NewSettings();

        // On: recorded and persisted (asserted elsewhere), but nothing is
        // folded now — navFold.js's enhancedload handler does it on the next
        // navigation, off the value already in storage. No call at all, rather
        // than a call with a defused argument: an apply(true) that "means" defer
        // would be a second, silent contract for the JS side to honour.
        await settings.SetKeepNavigationPanelFoldedAsync(true);

        Assert.DoesNotContain("bgquizNavFold.apply", JSInterop.Invocations.Identifiers);

        // Off: cannot wait. With the panel folded, every navigation that would
        // otherwise apply the new value is behind the folded panel's own links.
        await settings.SetKeepNavigationPanelFoldedAsync(false);

        var invocation = Assert.Single(JSInterop.Invocations["bgquizNavFold.apply"]);
        Assert.Equal(false, invocation.Arguments[0]);
    }

    // -----------------------------------------------------------------------
    //  Hydration lifecycle + tolerance
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EnsureHydrated_IsIdempotent()
    {
        // Both Home (the kick-off) and Quiz (which awaits it before its first
        // board render) call this, plus the Settings page. One read serves all.
        StageStored("""{"homeBoardOnRight":false}""");
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();
        await settings.EnsureHydratedAsync();
        await settings.EnsureHydratedAsync();

        Assert.Single(JSInterop.Invocations["localStorage.getItem"]);
        Assert.False(settings.HomeBoardOnRight);
    }

    [Fact]
    public async Task Hydrate_MissingFields_TakeTheirDefaults()
    {
        // A payload written by an older build — or by a future leg's partial
        // write — must not blank the settings it doesn't mention.
        StageStored("""{"randomizeSidePerProblem":true}""");
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.True(settings.RandomizeSidePerProblem);
        Assert.True(settings.HomeBoardOnRight);          // default, not false
        Assert.False(settings.KeepNavigationPanelFolded);
        Assert.True(settings.MaximizeBoardWhileAnswering);
        Assert.False(settings.SortAnalysisByDepthFirst);
        Assert.Null(settings.MaximumHiddenCandidateAnalysisLevel);
    }

    [Fact]
    public async Task Hydrate_PayloadPredatingTheMaximizeField_RestoresItOn()
    {
        // The tolerance rule in the concrete case it now has: the exact bytes
        // every build before issue #41 wrote. There is no migration and no
        // version stamp — the missing field simply takes its default, and since
        // #113 that default is ON. This is the half of the asymmetry that lets a
        // default change reach the users it should: they never chose, so they get
        // the product's current answer. The other three must come back as
        // written, so this is not a "the payload was ignored" pass.
        //
        // It asserted OFF until #113, by the same rule and the opposite
        // arithmetic — the assertion follows the default, which is the point.
        StageStored(
            """{"homeBoardOnRight":false,"randomizeSidePerProblem":true,"keepNavigationPanelFolded":true}""");
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.True(settings.MaximizeBoardWhileAnswering);
        Assert.False(settings.HomeBoardOnRight);
        Assert.True(settings.RandomizeSidePerProblem);
        Assert.True(settings.KeepNavigationPanelFolded);
    }

    [Fact]
    public async Task Hydrate_PayloadPredatingTheDepthTreatment_RendersExactlyAsBefore()
    {
        // The exact bytes every build before this leg wrote. Both new fields are
        // absent, both take their defaults, and — the part that matters — the
        // projections come out at the producer's own values, so an existing
        // user's stored settings produce a request identical to the one they
        // were already getting. That is the whole no-migration argument, and it
        // holds only because both defaults are off; the maximize field's flip
        // (#113) is the counter-example showing an absent field CAN change what
        // a user sees, which is why this is asserted rather than assumed.
        //
        // The other four must come back as written, so this cannot pass by the
        // payload being ignored wholesale.
        StageStored(
            """{"homeBoardOnRight":false,"randomizeSidePerProblem":true,"keepNavigationPanelFolded":true,"maximizeBoardWhileAnswering":false}""");
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.False(settings.SortAnalysisByDepthFirst);
        Assert.Equal(CandidateOrdering.Equity, settings.EffectiveCandidateOrdering);
        Assert.Null(settings.MaximumHiddenCandidateAnalysisLevel);

        Assert.False(settings.HomeBoardOnRight);
        Assert.True(settings.RandomizeSidePerProblem);
        Assert.True(settings.KeepNavigationPanelFolded);
        Assert.False(settings.MaximizeBoardWhileAnswering);
    }

    [Fact]
    public async Task Hydrate_StoredDepthTreatment_SurvivesIntoWhatTheProducerIsAsked()
    {
        // The round trip that matters to the user: the choice they made last
        // session is what the renderer is asked for this session. The ordering is
        // pinned through its projection rather than its bool, because the bool
        // round-tripping is already asserted above and would stay green if the
        // projection stopped reading it. The ceiling has no projection to go
        // stale — it IS what the producer is asked for.
        StageStored(
            """
            {"sortAnalysisByDepthFirst":true,"maximumHiddenCandidateAnalysisLevel":"XgRoller"}
            """);
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.Equal(CandidateOrdering.DepthFirst, settings.EffectiveCandidateOrdering);
        Assert.Equal(AnalysisLevel.XgRoller, settings.MaximumHiddenCandidateAnalysisLevel);
    }

    [Fact]
    public async Task Hydrate_TheRetiredHideShallowField_IsIgnored()
    {
        // The wire evolution, in the one case that can actually occur: a
        // developer's browser holding an entry this build's predecessor wrote,
        // with the retired hideShallowCandidates checkbox set. It is now just an
        // unknown field — ignored, exactly as any newer build's field would be —
        // so the ceiling comes back at its default and hides nothing.
        //
        // Nothing translates the old true into a Ply5 ceiling, and nothing
        // should: the bool never shipped in a release, so there is no installed
        // base whose choice would be lost, and inventing a migration for one
        // would mean carrying the retired constant's arithmetic forever to serve
        // nobody. The neighbouring field is set to a non-default so this cannot
        // pass by the payload being dropped wholesale.
        StageStored(
            """{"sortAnalysisByDepthFirst":true,"hideShallowCandidates":true}""");
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.Null(settings.MaximumHiddenCandidateAnalysisLevel);
        Assert.True(settings.SortAnalysisByDepthFirst);
    }

    [Theory]
    [InlineData("null")]                    // explicit "hide nothing", as this build writes it
    [InlineData("\"Unknown\"")]             // outside the rigor scale — never a threshold
    [InlineData("\"Ply42\"")]               // not a member
    [InlineData("\"4-ply\"")]               // the label, not the member name
    [InlineData("6")]                       // an ordinal, and not even a string
    [InlineData("true")]                    // the retired checkbox's type
    [InlineData("{\"level\":\"Ply4\"}")]    // a shape from some imagined later leg
    public async Task Hydrate_UnusableCeilingValue_HidesNothing(string storedValue)
    {
        // Per-field tolerance for the one non-boolean field. Every one of these
        // has to land on "hide nothing" rather than throwing or — the failure
        // that would matter — reaching DiagramRequest.Builder.Build as a value
        // it rejects, which would take out the review pane rather than this
        // setting.
        //
        // The "null" row is not a failure but the format's own spelling of an
        // explicit hide-nothing choice; it is here so the tolerant path and the
        // deliberate path are seen to agree while the default is null. The
        // neighbouring field is stored non-default throughout, so none of these
        // can pass by the payload being rejected wholesale.
        StageStored(
            $$"""
            {"sortAnalysisByDepthFirst":true,"maximumHiddenCandidateAnalysisLevel":{{storedValue}}}
            """);
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.Null(settings.MaximumHiddenCandidateAnalysisLevel);
        Assert.True(settings.SortAnalysisByDepthFirst);
    }

    [Fact]
    public async Task Hydrate_StoredFalse_OutranksTheDefault()
    {
        // The OTHER half of the asymmetry, and the one that made changing the
        // default safe at all (#113): a user who went to the Settings page and
        // turned the mode off wrote an explicit false, and an explicit false has
        // to keep winning — a default change is not a licence to overrule a
        // choice somebody made. Nothing in Restore separates "stored false" from
        // "absent" except the field being there, so this pin is what stands
        // between the tolerant fallback and a silent migration.
        //
        // Deliberately not folded into the round-trip test above: that one asks
        // whether this type reads back what it wrote, and would stay green if
        // absent and false ever collapsed to the same answer.
        StageStored(
            """{"homeBoardOnRight":true,"randomizeSidePerProblem":false,"keepNavigationPanelFolded":false,"maximizeBoardWhileAnswering":false}""");
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.False(settings.MaximizeBoardWhileAnswering);
    }

    [Fact]
    public async Task Hydrate_UnknownFields_AreIgnored()
    {
        // The forward half of the same rule: a payload written by a NEWER build
        // (a later leg's tolerance or palette setting) still restores what this
        // build understands, instead of failing wholesale.
        StageStored(
            """{"homeBoardOnRight":false,"equityTolerance":0.02,"palette":"dusk"}""");
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.False(settings.HomeBoardOnRight);
    }

    [Theory]
    [InlineData("}{ not valid json")]
    [InlineData("null")]
    [InlineData("\"a string\"")]
    [InlineData("[true,false,true]")]
    [InlineData("")]
    public async Task Hydrate_MalformedPayload_LeavesEveryDefaultStanding(string stored)
    {
        // Never throws, and never lands a partial restore: a corrupt entry —
        // hand-edited in devtools, truncated, or a JSON value that isn't an
        // object at all — leaves the app exactly as a fresh browser would.
        StageStored(stored);
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.True(settings.HomeBoardOnRight);
        Assert.False(settings.RandomizeSidePerProblem);
        Assert.False(settings.KeepNavigationPanelFolded);
        Assert.True(settings.MaximizeBoardWhileAnswering);
        Assert.False(settings.SortAnalysisByDepthFirst);
        Assert.Null(settings.MaximumHiddenCandidateAnalysisLevel);
    }

    [Fact]
    public async Task Hydrate_NonBooleanFieldValues_TakeTheirDefaults()
    {
        // Per-field tolerance rather than whole-payload rejection: one field
        // written as the wrong type must not cost the user the others.
        // Every unreadable value is the OPPOSITE of its field's default, so
        // falling back is distinguishable from parsing it loosely: the maximize
        // field is staged as the string "false" against a default of true,
        // exactly as randomizeSidePerProblem is staged as 1 against a default of
        // false. (It was staged as "true" until #113 flipped that default, at
        // which point the assertion below would have passed either way.)
        //
        // The ceiling's own wrong-type rows are a theory of their own
        // (Hydrate_UnusableCeilingValue_HidesNothing), because it is the one
        // field where "the wrong type" is a whole vocabulary rather than
        // not-a-bool; the row here is the bare kind check.
        StageStored(
            """
            {"homeBoardOnRight":"yes","randomizeSidePerProblem":1,
             "keepNavigationPanelFolded":true,"maximizeBoardWhileAnswering":"false",
             "sortAnalysisByDepthFirst":"true","maximumHiddenCandidateAnalysisLevel":4}
            """);
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.True(settings.HomeBoardOnRight);            // default
        Assert.False(settings.RandomizeSidePerProblem);    // default
        Assert.True(settings.MaximizeBoardWhileAnswering); // default — "false" is a string
        Assert.False(settings.SortAnalysisByDepthFirst);   // default — "true" is a string
        Assert.Null(settings.MaximumHiddenCandidateAnalysisLevel); // default — 4 is not a token
        Assert.True(settings.KeepNavigationPanelFolded);   // the readable one survives
    }
}
