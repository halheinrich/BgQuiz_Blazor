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
/// the <b>producer-shaped projections</b> the request is actually built from
/// (see <see cref="HideShallowSetting_ProjectsToAFloorOfFivePly_BecauseTheFloorIsInclusive"/>
/// for the off-by-one they exist to hold in one place), the <b>serialized wire
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
        Assert.False(settings.HideShallowCandidates);
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
        Assert.False(settings.HideShallowCandidates);
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
    //  The depth treatment's projections — the OTHER derivation this service
    //  owns (issues halheinrich/backgammon#150 and halheinrich/backgammon#66).
    //  Each is a checkbox whose two answers are a producer value, so the
    //  mapping is the thing worth pinning: it is what a call site would
    //  otherwise restate, and what a level picker would later re-point.
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
    public async Task HideShallowSetting_ProjectsToAFloorOfFivePly_BecauseTheFloorIsInclusive()
    {
        // The off-by-one, pinned where it is decided rather than trusted.
        // The checkbox says "4-ply and below" and the request carries Ply5,
        // because the producer's floor is INCLUSIVE — a candidate evaluated AT
        // the floor still renders, so the lowest level that must survive is
        // 5-ply. The label and this constant are two halves of one claim; a
        // reworded label without a re-reasoned floor (or the reverse) is the
        // exact drift this asserts against.
        var settings = NewSettings();

        Assert.Null(settings.EffectiveMinimumCandidateAnalysisLevel); // off: show all

        await settings.SetHideShallowCandidatesAsync(true);

        Assert.Equal(AnalysisLevel.Ply5, settings.EffectiveMinimumCandidateAnalysisLevel);

        // And back off again — a floor that latched would quietly outlive the
        // choice that set it.
        await settings.SetHideShallowCandidatesAsync(false);

        Assert.Null(settings.EffectiveMinimumCandidateAnalysisLevel);
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
        Assert.Null(settings.EffectiveMinimumCandidateAnalysisLevel);
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

        await settings.SetHideShallowCandidatesAsync(true);
        Assert.True(settings.HideShallowCandidates);
        Assert.Contains("\"hideShallowCandidates\":true", LastPersisted());
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
        var settings = NewSettings();

        await settings.SetRandomizeSidePerProblemAsync(true);

        Assert.Equal(
            """{"homeBoardOnRight":true,"randomizeSidePerProblem":true,"keepNavigationPanelFolded":false,"maximizeBoardWhileAnswering":true,"sortAnalysisByDepthFirst":false,"hideShallowCandidates":false}""",
            LastPersisted());
    }

    [Fact]
    public async Task PersistedPayload_RoundTripsThroughHydration()
    {
        // The whole point of the entry: what one app writes, the next app boot
        // reads back identically. Every field is driven AWAY from its own
        // default, without exception — a field left sitting on its default would
        // round-trip green through a reader that ignored the payload entirely.
        // The maximize field was that exception until #113 flipped its default;
        // it now writes false for the same reason the other three write what they
        // write.
        var writer = NewSettings();
        await writer.SetHomeBoardOnRightAsync(false);
        await writer.SetRandomizeSidePerProblemAsync(true);
        await writer.SetKeepNavigationPanelFoldedAsync(true);
        await writer.SetMaximizeBoardWhileAnsweringAsync(false);
        await writer.SetSortAnalysisByDepthFirstAsync(true);
        await writer.SetHideShallowCandidatesAsync(true);

        StageStored(LastPersisted());
        var reader = NewSettings();
        await reader.EnsureHydratedAsync();

        Assert.False(reader.HomeBoardOnRight);
        Assert.True(reader.RandomizeSidePerProblem);
        Assert.True(reader.KeepNavigationPanelFolded);
        Assert.False(reader.MaximizeBoardWhileAnswering);
        Assert.True(reader.SortAnalysisByDepthFirst);
        Assert.True(reader.HideShallowCandidates);
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
        Assert.False(settings.HideShallowCandidates);
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
        Assert.False(settings.HideShallowCandidates);
        Assert.Equal(CandidateOrdering.Equity, settings.EffectiveCandidateOrdering);
        Assert.Null(settings.EffectiveMinimumCandidateAnalysisLevel);

        Assert.False(settings.HomeBoardOnRight);
        Assert.True(settings.RandomizeSidePerProblem);
        Assert.True(settings.KeepNavigationPanelFolded);
        Assert.False(settings.MaximizeBoardWhileAnswering);
    }

    [Fact]
    public async Task Hydrate_StoredDepthTreatment_SurvivesIntoTheProjections()
    {
        // The round trip that matters to the user: the choice they made last
        // session is what the renderer is asked for this session. Pinned through
        // the projections rather than the bools, because the bools round-tripping
        // is already asserted above and would stay green if a projection stopped
        // reading them.
        StageStored(
            """{"sortAnalysisByDepthFirst":true,"hideShallowCandidates":true}""");
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.Equal(CandidateOrdering.DepthFirst, settings.EffectiveCandidateOrdering);
        Assert.Equal(AnalysisLevel.Ply5, settings.EffectiveMinimumCandidateAnalysisLevel);
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
        Assert.False(settings.HideShallowCandidates);
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
        StageStored(
            """
            {"homeBoardOnRight":"yes","randomizeSidePerProblem":1,
             "keepNavigationPanelFolded":true,"maximizeBoardWhileAnswering":"false",
             "sortAnalysisByDepthFirst":"true","hideShallowCandidates":1}
            """);
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.True(settings.HomeBoardOnRight);            // default
        Assert.False(settings.RandomizeSidePerProblem);    // default
        Assert.True(settings.MaximizeBoardWhileAnswering); // default — "false" is a string
        Assert.False(settings.SortAnalysisByDepthFirst);   // default — "true" is a string
        Assert.False(settings.HideShallowCandidates);      // default — 1 is not a bool
        Assert.True(settings.KeepNavigationPanelFolded);   // the readable one survives
    }
}
