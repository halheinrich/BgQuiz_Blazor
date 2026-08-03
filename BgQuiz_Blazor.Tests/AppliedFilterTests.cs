using BgQuiz_Blazor.Client.Quiz;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="AppliedFilter"/> — the holder-owned filter half of
/// Home's start gate, plus the pick stamp Home's Apply Mix gate reads. Holding
/// "applied at least once" here (rather than in a transient page field) is what
/// keeps the gate honest across in-app navigation; <see cref="PageTests"/> pins
/// the page-render half.
/// </summary>
public class AppliedFilterTests
{
    [Fact]
    public void NotApplied_ByDefault()
    {
        var holder = new AppliedFilter();

        Assert.False(holder.IsApplied);
        Assert.Null(holder.Config);
    }

    [Fact]
    public void Set_MarksAppliedAndHoldsConfig()
    {
        var holder = new AppliedFilter();
        var cfg = new FilterConfig { Players = ["Alice"] };

        holder.Set(cfg, pickGeneration: 1);

        Assert.True(holder.IsApplied);
        Assert.Same(cfg, holder.Config);
    }

    [Fact]
    public void Clear_DropsAppliedState()
    {
        var holder = new AppliedFilter();
        holder.Set(new FilterConfig(), pickGeneration: 1);

        holder.Clear();

        Assert.False(holder.IsApplied);
        Assert.Null(holder.Config);
    }

    [Fact]
    public void Set_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new AppliedFilter().Set(null!, 1));

    // ── The pick stamp (Home's Apply Mix gate, issue #45) ────────────────────

    [Fact]
    public void WasAppliedFor_FalseForEveryGeneration_BeforeAnyApply()
    {
        var holder = new AppliedFilter();

        // Including 0, the generation a freshly-constructed PickedProblemFolder
        // reports: "never applied" must not read as "applied for pick 0".
        Assert.False(holder.WasAppliedFor(0));
        Assert.False(holder.WasAppliedFor(1));
    }

    [Fact]
    public void WasAppliedFor_TrueOnlyForTheStampedGeneration()
    {
        var holder = new AppliedFilter();

        holder.Set(new FilterConfig(), pickGeneration: 3);

        Assert.True(holder.WasAppliedFor(3));
        Assert.False(holder.WasAppliedFor(2));
        Assert.False(holder.WasAppliedFor(4));
    }

    [Fact]
    public void WasAppliedFor_SurvivesClear()
    {
        // The ruling: a filter *edit* un-applies the config (Start re-gates) but
        // does not revoke the mix gate — the corpus has still been filtered.
        var holder = new AppliedFilter();
        holder.Set(new FilterConfig(), pickGeneration: 3);

        holder.Clear();

        Assert.False(holder.IsApplied);
        Assert.True(holder.WasAppliedFor(3));
    }

    [Fact]
    public void WasAppliedFor_ReStampsOnReApply()
    {
        // A pick bumps the folder's generation, so the old stamp expires with no
        // reset call; the next Apply stamps the new pick.
        var holder = new AppliedFilter();
        holder.Set(new FilterConfig(), pickGeneration: 3);

        holder.Set(new FilterConfig(), pickGeneration: 5);

        Assert.False(holder.WasAppliedFor(3));
        Assert.True(holder.WasAppliedFor(5));
    }
}
