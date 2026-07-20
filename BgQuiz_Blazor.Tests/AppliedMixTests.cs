using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="AppliedMix"/> — the committed-mix half of Home's start
/// gate. Blank is the valid default (no "never applied" blocking state, unlike
/// <see cref="AppliedFilter"/>); only dirtiness gates.
/// </summary>
public class AppliedMixTests
{
    [Fact]
    public void Defaults_BlankMix_NotDirty()
    {
        var holder = new AppliedMix();

        Assert.True(holder.Current.IsPassthrough);
        Assert.False(holder.IsDirty);
    }

    [Fact]
    public void Apply_SetsCurrent_AndClearsDirty()
    {
        var holder = new AppliedMix();
        holder.MarkDirty();
        var mix = new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)]);

        holder.Apply(mix);

        Assert.Same(mix, holder.Current);
        Assert.False(holder.IsDirty);
    }

    [Fact]
    public void MarkDirty_SetsDirty_KeepsCurrent()
    {
        var holder = new AppliedMix();
        var mix = new QuizMix([new QuizMixEntry(QuizCategory.GotWrong, 100)]);
        holder.Apply(mix);

        holder.MarkDirty();

        Assert.True(holder.IsDirty);
        Assert.Same(mix, holder.Current); // dirty edits never rewrite the committed mix
    }

    [Fact]
    public void Apply_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AppliedMix().Apply(null!));
    }
}
