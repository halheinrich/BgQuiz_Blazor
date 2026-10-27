using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="NotesSize"/>, the notes overlay's size preference as a value
/// (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's size is a remembered
/// preference"; issue <c>halheinrich/backgammon#414</c>), and
/// <see cref="NotesPreferences"/>, the two preferences together: unset is the
/// default, each axis is held on its own, and no length that is not a finite
/// positive number gets in.
/// </summary>
public class NotesSizeTests
{
    [Fact]
    public void Unset_IsTheDefault_BothAxesAtTheStylesheetsSize()
    {
        Assert.Equal(default, NotesSize.Unset);
        Assert.True(NotesSize.Unset.IsUnset);
        Assert.Null(NotesSize.Unset.Width);
        Assert.Null(NotesSize.Unset.Height);
    }

    [Theory]
    [InlineData(400.0, 300.0)]
    [InlineData(0.5, null)]
    [InlineData(null, 1e6)]
    public void Create_HoldsEachAxisAsGiven(double? width, double? height)
    {
        var size = NotesSize.Create(width, height);

        Assert.Equal(width, size.Width);
        Assert.Equal(height, size.Height);
        Assert.False(size.IsUnset);
    }

    [Fact]
    public void Create_WithBothAxesUnset_IsUnset() => Assert.Equal(NotesSize.Unset, NotesSize.Create(null, null));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Create_RefusesALengthThatIsNotFiniteAndPositive_OnEitherAxis(double bad)
    {
        var width = Assert.Throws<ArgumentOutOfRangeException>(() => NotesSize.Create(bad, 300));
        Assert.Equal("width", width.ParamName);

        var height = Assert.Throws<ArgumentOutOfRangeException>(() => NotesSize.Create(400, bad));
        Assert.Equal("height", height.ParamName);

        Assert.False(NotesSize.TryCreate(bad, 300, out var refused));
        Assert.Equal(NotesSize.Unset, refused);
        Assert.False(NotesSize.TryCreate(400, bad, out refused));
        Assert.Equal(NotesSize.Unset, refused);
    }

    [Theory]
    [InlineData(400.0, 300.0)]
    [InlineData(null, 0.25)]
    [InlineData(null, null)]
    public void TryCreate_AcceptsWhatCreateAccepts(double? width, double? height)
    {
        Assert.True(NotesSize.TryCreate(width, height, out var size));
        Assert.Equal(NotesSize.Create(width, height), size);
    }

    [Fact]
    public void ReplacingOneAxis_KeepsTheOther_UnsetIncluded()
    {
        var size = NotesSize.Create(400, null);

        Assert.Equal(NotesSize.Create(400, 300), size.WithHeight(300));
        Assert.Equal(NotesSize.Create(500, null), size.WithWidth(500));
        Assert.Equal(NotesSize.Unset, size.WithWidth(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => size.WithHeight(0));
    }

    [Fact]
    public void ThePreferences_AreUnsetOnlyWhenBothAre()
    {
        Assert.Equal(default, NotesPreferences.Unset);
        Assert.True(NotesPreferences.Unset.IsUnset);
        Assert.False(new NotesPreferences(NotesPlacement.Create(0.5, null), NotesSize.Unset).IsUnset);
        Assert.False(new NotesPreferences(NotesPlacement.Unset, NotesSize.Create(null, 300)).IsUnset);
    }
}
