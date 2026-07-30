using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="AppliedMix"/> — the committed-mix half of Home's start
/// gate. Blank is the valid default (no "never applied" blocking state, unlike
/// <see cref="AppliedFilter"/>). The holder stores <b>no dirtiness</b>: the
/// gate derives from <see cref="MixDraft.Matches"/> against
/// <see cref="Current"/> (see <see cref="MixDraftTests"/>), so this type is
/// nothing but the commitment.
/// </summary>
public class AppliedMixTests
{
    [Fact]
    public void Defaults_BlankMix()
    {
        Assert.True(new AppliedMix().Current.IsPassthrough);
    }

    [Fact]
    public void Apply_SetsCurrent()
    {
        var holder = new AppliedMix();
        var mix = new QuizMix([new QuizMixEntry(QuizCategory.NeverSeen, 100)]);

        holder.Apply(mix);

        Assert.Same(mix, holder.Current);
    }

    [Fact]
    public void Reset_ReturnsToPassthrough()
    {
        var holder = new AppliedMix();
        holder.Apply(new QuizMix([new QuizMixEntry(QuizCategory.GotWrong, 100)]));

        holder.Reset();

        Assert.True(holder.Current.IsPassthrough);
    }

    [Fact]
    public void Apply_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AppliedMix().Apply(null!));
    }
}
