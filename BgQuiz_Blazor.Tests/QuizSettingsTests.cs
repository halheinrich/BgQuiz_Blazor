using BgQuiz_Blazor.Client.Quiz;
using Bunit;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="QuizSettings"/> — the app-scoped user settings and the
/// one localStorage entry behind them. Three things are pinned here: the
/// defaults (which must reproduce the behavior that shipped before settings
/// existed), the <b>serialized wire format</b> byte-for-byte (a durable payload
/// with a second reader in another language — see
/// <see cref="Persist_WritesThePinnedWireFormat"/>), and the tolerance rules a
/// format that later legs will extend has to hold. Extends
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
    public void FreshSettings_ReproduceThePreSettingsBehavior()
    {
        // The contract that lets this feature ship dark: a user who never opens
        // the Settings page must see exactly the app that shipped before it —
        // home board on the right (the producer's own DiagramRequest default),
        // no randomization, navigation panel unfolded.
        var settings = NewSettings();

        Assert.True(settings.HomeBoardOnRight);
        Assert.False(settings.RandomizeSidePerProblem);
        Assert.False(settings.KeepNavigationPanelFolded);
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
    //  Immediate apply + persistence
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EverySetter_AppliesImmediately_AndPersists()
    {
        // No Apply button, no draft: the property is the new value the moment
        // the setter returns, and the write has already gone out.
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
        var settings = NewSettings();

        await settings.SetRandomizeSidePerProblemAsync(true);

        Assert.Equal(
            """{"homeBoardOnRight":true,"randomizeSidePerProblem":true,"keepNavigationPanelFolded":false}""",
            LastPersisted());
    }

    [Fact]
    public async Task PersistedPayload_RoundTripsThroughHydration()
    {
        // The whole point of the entry: what one app writes, the next app boot
        // reads back identically.
        var writer = NewSettings();
        await writer.SetHomeBoardOnRightAsync(false);
        await writer.SetRandomizeSidePerProblemAsync(true);
        await writer.SetKeepNavigationPanelFoldedAsync(true);

        StageStored(LastPersisted());
        var reader = NewSettings();
        await reader.EnsureHydratedAsync();

        Assert.False(reader.HomeBoardOnRight);
        Assert.True(reader.RandomizeSidePerProblem);
        Assert.True(reader.KeepNavigationPanelFolded);
    }

    [Fact]
    public async Task SettingTheFold_InvokesTheApplierWithTheValue()
    {
        // C# cannot reach the collapse checkbox (uncontrolled, in a statically
        // rendered layout), so the setting would appear to do nothing until the
        // next navigation without this call. The value goes as an argument:
        // the applier must not have to re-read storage, which would make the
        // order of the two calls inside the setter load-bearing.
        var settings = NewSettings();

        await settings.SetKeepNavigationPanelFoldedAsync(true);

        var invocation = Assert.Single(JSInterop.Invocations["bgquizNavFold.apply"]);
        Assert.Equal(true, invocation.Arguments[0]);

        await settings.SetKeepNavigationPanelFoldedAsync(false);
        Assert.Equal(false, JSInterop.Invocations["bgquizNavFold.apply"].Last().Arguments[0]);
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
    }

    [Fact]
    public async Task Hydrate_NonBooleanFieldValues_TakeTheirDefaults()
    {
        // Per-field tolerance rather than whole-payload rejection: one field
        // written as the wrong type must not cost the user the other two.
        StageStored(
            """{"homeBoardOnRight":"yes","randomizeSidePerProblem":1,"keepNavigationPanelFolded":true}""");
        var settings = NewSettings();

        await settings.EnsureHydratedAsync();

        Assert.True(settings.HomeBoardOnRight);          // default
        Assert.False(settings.RandomizeSidePerProblem);  // default
        Assert.True(settings.KeepNavigationPanelFolded); // the readable one survives
    }
}
