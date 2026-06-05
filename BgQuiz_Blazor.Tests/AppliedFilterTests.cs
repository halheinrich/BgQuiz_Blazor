using BgQuiz_Blazor.Client.Quiz;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="AppliedFilter"/> — the holder-owned filter half of
/// Home's start gate. Holding "applied at least once" here (rather than in a
/// transient page field) is what keeps the gate honest across in-app
/// navigation; <see cref="PageTests"/> pins the page-render half.
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

        holder.Set(cfg);

        Assert.True(holder.IsApplied);
        Assert.Same(cfg, holder.Config);
    }

    [Fact]
    public void Clear_DropsAppliedState()
    {
        var holder = new AppliedFilter();
        holder.Set(new FilterConfig());

        holder.Clear();

        Assert.False(holder.IsApplied);
        Assert.Null(holder.Config);
    }

    [Fact]
    public void Set_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new AppliedFilter().Set(null!));
}
