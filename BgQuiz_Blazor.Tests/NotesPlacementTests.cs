using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="NotesPlacement"/>, the notes overlay's placement preference as a
/// value (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's placement is a
/// remembered preference"; issue <c>halheinrich/backgammon#344</c>): unset is
/// the default, each axis is held on its own, and nothing outside 0 to 1 gets
/// in.
/// </summary>
public class NotesPlacementTests
{
    [Fact]
    public void Unset_IsTheDefault_BothAxesCentred()
    {
        Assert.Equal(default, NotesPlacement.Unset);
        Assert.True(NotesPlacement.Unset.IsUnset);
        Assert.Null(NotesPlacement.Unset.Horizontal);
        Assert.Null(NotesPlacement.Unset.Vertical);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(0.5, null)]
    [InlineData(null, 0.25)]
    public void Create_HoldsEachAxisAsGiven(double? horizontal, double? vertical)
    {
        var placement = NotesPlacement.Create(horizontal, vertical);

        Assert.Equal(horizontal, placement.Horizontal);
        Assert.Equal(vertical, placement.Vertical);
        Assert.False(placement.IsUnset);
    }

    [Fact]
    public void Create_WithBothAxesUnset_IsUnset() =>
        Assert.Equal(NotesPlacement.Unset, NotesPlacement.Create(null, null));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Create_RefusesAPositionOutsideTheTravel_OnEitherAxis(double outside)
    {
        var horizontal = Assert.Throws<ArgumentOutOfRangeException>(() => NotesPlacement.Create(outside, 0.5));
        Assert.Equal("horizontal", horizontal.ParamName);

        var vertical = Assert.Throws<ArgumentOutOfRangeException>(() => NotesPlacement.Create(0.5, outside));
        Assert.Equal("vertical", vertical.ParamName);

        Assert.False(NotesPlacement.TryCreate(outside, 0.5, out var refused));
        Assert.Equal(NotesPlacement.Unset, refused);
        Assert.False(NotesPlacement.TryCreate(0.5, outside, out refused));
        Assert.Equal(NotesPlacement.Unset, refused);
    }

    [Fact]
    public void TryCreate_AcceptsWhatCreateAccepts()
    {
        Assert.True(NotesPlacement.TryCreate(0.125, null, out var placement));
        Assert.Equal(NotesPlacement.Create(0.125, null), placement);
    }

    [Fact]
    public void ReplacingOneAxis_KeepsTheOther_UnsetIncluded()
    {
        var placement = NotesPlacement.Create(0.75, null);

        Assert.Equal(NotesPlacement.Create(0.75, 0.25), placement.WithVertical(0.25));
        Assert.Equal(NotesPlacement.Create(0.0, null), placement.WithHorizontal(0.0));
        Assert.Equal(NotesPlacement.Unset, placement.WithHorizontal(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => placement.WithVertical(2));
    }
}
