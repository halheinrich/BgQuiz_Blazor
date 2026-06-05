using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Tests for <see cref="PickedProblemSet.Summary"/> — the holder-owned label
/// that is the single source of truth for how a picked set describes itself.
/// Deriving it here (rather than in a transient page field) is what keeps the
/// summary honest across in-app navigation; <see cref="PageTests"/> pins the
/// page-render half.
/// </summary>
public class PickedProblemSetTests
{
    [Fact]
    public void Summary_NoFiles_IsNull() =>
        Assert.Null(new PickedProblemSet().Summary);

    [Fact]
    public void Summary_SingleFile_IsThatFileName()
    {
        var set = new PickedProblemSet();
        set.Set([new PickedFile("match.xg", [1, 2, 3])]);

        Assert.Equal("match.xg", set.Summary);
    }

    [Fact]
    public void Summary_MultipleFiles_IsCountPicked()
    {
        var set = new PickedProblemSet();
        set.Set([new PickedFile("a.xg", []), new PickedFile("b.xgp", [])]);

        Assert.Equal("2 files picked", set.Summary);
    }

    [Fact]
    public void Summary_AfterClear_IsNullAgain()
    {
        var set = new PickedProblemSet();
        set.Set([new PickedFile("match.xg", [])]);
        set.Clear();

        Assert.Null(set.Summary);
    }
}
