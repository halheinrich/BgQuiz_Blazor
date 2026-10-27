namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// How large the decision's notes are when they are open: the <b>size
/// preference</b> of <c>SPEC-quiz-view.md</c> §4, "The notes overlay's size is
/// a remembered preference" (ruled 2026-10-25, amended 2026-10-27, issue
/// <c>halheinrich/backgammon#414</c>). One per browser, beside the
/// <see cref="NotesPlacement"/> in <see cref="NotesPreferences"/>, held by
/// <see cref="NotesPreferencesStore"/>; shown and resized by
/// <see cref="NotesStage"/>.
///
/// <para>
/// <b>Each axis is held on its own</b>, as the overlay's length on that axis
/// in CSS pixels — the unit the stage is measured in and a drag arrives in —
/// or <c>null</c> for an axis no resize has set, which shows the stylesheet's
/// default: <c>min(36rem, …)</c> across, fit to the note and capped down. The
/// held length is what the reader chose in the window of that moment; a
/// different window shows it through <see cref="NotesStage"/>'s clamp, which
/// never changes it.
/// </para>
///
/// <para>
/// <b>Unset is the default</b> — <see cref="Unset"/>, which is also
/// <c>default</c>: both axes <c>null</c>. A size is unset exactly when both
/// axes are.
/// </para>
///
/// <para>
/// <b>Valid by construction</b>, as <see cref="NotesPlacement"/> is: the only
/// ways in are <see cref="Unset"/> and <see cref="Create"/>, which refuses a
/// length that is not a finite positive number; the store's tolerant read
/// goes through <see cref="TryCreate"/>.
/// </para>
/// </summary>
internal readonly record struct NotesSize
{
    private NotesSize(double? width, double? height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>The unset preference: the stylesheet's default size on both axes. The same value as <c>default</c>.</summary>
    public static NotesSize Unset => default;

    /// <summary>The overlay's chosen width in CSS pixels; <c>null</c> when unset.</summary>
    public double? Width { get; }

    /// <summary>The overlay's chosen height in CSS pixels; <c>null</c> when unset.</summary>
    public double? Height { get; }

    /// <summary>True when neither axis has been set — the default, and what Reset returns to.</summary>
    public bool IsUnset => Width is null && Height is null;

    /// <summary>
    /// A size holding <paramref name="width"/> and <paramref name="height"/>,
    /// each a length in CSS pixels or <c>null</c> for an unset axis.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A length is not a finite positive number.</exception>
    public static NotesSize Create(double? width, double? height)
    {
        if (!IsLength(width))
            throw new ArgumentOutOfRangeException(nameof(width), width, LengthRule);
        if (!IsLength(height))
            throw new ArgumentOutOfRangeException(nameof(height), height, LengthRule);
        return new NotesSize(width, height);
    }

    /// <summary>
    /// <see cref="Create"/> for input this app does not control: false, with
    /// <see cref="Unset"/>, where a length is not a finite positive number.
    /// </summary>
    public static bool TryCreate(double? width, double? height, out NotesSize size)
    {
        if (IsLength(width) && IsLength(height))
        {
            size = new NotesSize(width, height);
            return true;
        }
        size = Unset;
        return false;
    }

    /// <summary>This size with the width replaced; the height is kept.</summary>
    /// <exception cref="ArgumentOutOfRangeException">See <see cref="Create"/>.</exception>
    public NotesSize WithWidth(double? width) => Create(width, Height);

    /// <summary>This size with the height replaced; the width is kept.</summary>
    /// <exception cref="ArgumentOutOfRangeException">See <see cref="Create"/>.</exception>
    public NotesSize WithHeight(double? height) => Create(Width, height);

    private const string LengthRule = "A chosen length is a finite number of CSS pixels above 0, or null for an unset axis.";

    private static bool IsLength(double? value) =>
        value is not double length || (double.IsFinite(length) && length > 0);
}
