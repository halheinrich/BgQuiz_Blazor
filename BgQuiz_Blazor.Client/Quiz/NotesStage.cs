namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The page the decision's notes are placed on, as measured in the browser,
/// and <b>the one home of the placement and size arithmetic</b>
/// (<c>SPEC-quiz-view.md</c> §4, "The notes overlay's placement is a
/// remembered preference", issue <c>halheinrich/backgammon#344</c>, and "The
/// notes overlay's size is a remembered preference", issue
/// <c>halheinrich/backgammon#414</c>): how <see cref="NotesPreferences"/> are
/// shown (<see cref="Show"/>), what a move makes of them (<see cref="Drag"/>,
/// <see cref="Step"/>), and what a resize makes of them (the two
/// <see cref="Resize(NotesPreferences, double, double)"/> overloads). All
/// lengths are CSS pixels, relative to the layout viewport's top-left corner.
///
/// <para>
/// <b>The size, shown.</b> On each axis whose size is set, the overlay is
/// rendered at the held length clamped to the <i>show bounds</i>: at least the
/// minimum (<see cref="MinimumWidth"/>; the title bar plus
/// <see cref="MinimumTextLines"/> lines across), at most the layout viewport
/// less <see cref="Clearance"/> on each side — and where that space is smaller
/// than the minimum, the space wins. The clamp changes what is shown and never
/// what is held, and the rendered length is worked from the held one and the
/// area alone, never from the measured overlay, so the module's report cannot
/// feed back into the size it reports. An unset axis renders nothing (the
/// stylesheet's default applies) and its shown length is the measured one.
/// </para>
///
/// <para>
/// <b>The travel.</b> On each axis the overlay travels within the layout
/// viewport less <see cref="Clearance"/> on each side, less its own shown
/// length, so a position <c>p</c> from 0 to 1 shows it <c>Clearance + p ×
/// travel</c> from the start edge. An unset axis is <c>p = ½</c>, which is the
/// overlay centred — exactly where it opened before the preference existed.
/// An axis whose travel is under <see cref="MinimumTravel"/> (the overlay
/// fills it, as a phone's width does) has none: the overlay shows centred on
/// it whatever is held, and no move changes what is held there.
/// </para>
///
/// <para>
/// <b>The display clamp.</b> Whatever the window, the title bar — and with it
/// the close control — stays inside the layout viewport, the area a fixed
/// overlay is placed in, through changes in window size, orientation,
/// browser page zoom and note length: horizontally the whole overlay, which
/// the title bar spans; vertically down to the title bar's bottom edge.
/// Pinch zoom is not followed: it magnifies the page and pans the reader
/// across it without changing the layout viewport, so the overlay stays
/// where it was placed (<c>SPEC-quiz-view.md</c> §4, ruled 2026-10-06).
/// Where even that cannot fit, the start edge wins (the left, the top). The
/// clamp changes what <see cref="Show"/> returns and never the placement, so a
/// window that grows again shows the user's placement again. With the show
/// bounds and the stylesheet's default caps (the overlay is never larger than
/// the area less the clearance) the travel already keeps it inside, and the
/// clamp acts only where the title bar outgrows a very short overlay.
/// </para>
///
/// <para>
/// <b>A resize keeps the top-left corner.</b> Its bounds are the gesture's,
/// kept apart from the show bounds: on each axis it changes, the new length
/// is the shown length moved by the gesture, at least the minimum and at most
/// the <i>room</i> from the shown top-left corner to the edge clearance on
/// that side — so moving the overlay is how a reader makes more room. The
/// placement becomes the position within the new travel that keeps that
/// corner where it was shown; where the new travel is none, the held
/// placement is kept. Where the room is smaller than the minimum, that axis
/// cannot be resized.
/// </para>
///
/// <para>
/// <b>Gestures start from what is shown</b> and change only an axis they
/// actually changed: an axis with no travel, a drag with no movement along
/// it, a step already at that edge or bound leaves both held values as they
/// were, unset included. So a caller tells a gesture that wrote something
/// from one that could not by comparing the result with what it passed in.
/// </para>
/// </summary>
internal readonly record struct NotesStage
{
    /// <summary>
    /// <b>The move step</b>: one press of a Move step button moves the overlay
    /// an eighth of its travel on that axis. A fixed fraction rather than a
    /// fixed length, because the placement is held in fractions of the
    /// travel: from the centre, four steps reach either edge in any window,
    /// and the steps land on eighths there, which a double holds exactly.
    /// </summary>
    internal const double StepFraction = 1.0 / 8;

    /// <summary>
    /// <b>The size step</b>: one press of a size step button changes the
    /// overlay's length on that axis by an eighth of the axis's range — from
    /// the minimum to the area less the clearance on each side. A fraction of
    /// the range rather than a fixed length, for the move step's reason: eight
    /// presses span the whole range in any window, a phone's included, and no
    /// window makes the step too small to see or too large to aim.
    /// </summary>
    internal const double SizeStepFraction = 1.0 / 8;

    /// <summary>
    /// <b>The minimum width</b> a resize can leave the overlay at (CSS
    /// pixels): the narrowest at which the title bar keeps the heading, the
    /// Adjust control and the close control on one line. Measured in the
    /// published app in Chromium on Windows at the default 16 px root font
    /// (2026-10-27): the heading (41.8), the Adjust and close controls with
    /// their gap (92.9), the gap between (16), the title bar's padding (32)
    /// and the overlay's borders (2) come to 184.7 px. 200 leaves about 15 px
    /// for a system font that renders the two words wider than Segoe UI does.
    /// Below it the title bar would wrap, and the minimum height, which is read
    /// from the title bar's bottom, would grow with every narrowing.
    /// </summary>
    internal const double MinimumWidth = 200;

    /// <summary>
    /// <b>The minimum height</b> a resize can leave the overlay at is the title
    /// bar's bottom plus this many lines at the text's line height. Three,
    /// because the text's own padding (0.75rem above and below, one line's
    /// height at the page's line height of 1.5) takes about one: two lines of
    /// the note stay readable, which is "the title bar plus a few lines of
    /// text", and a reader can still tell a note that scrolls from one that
    /// ends.
    /// </summary>
    internal const int MinimumTextLines = 3;

    /// <summary>
    /// Travel shorter than this (CSS pixels) is none: half a pixel is no
    /// movement a reader could see, and it absorbs the rounding in measuring an
    /// overlay that fills the axis.
    /// </summary>
    internal const double MinimumTravel = 0.5;

    /// <summary>Where an unset axis shows the overlay: the middle of its travel, which is centred.</summary>
    private const double Centre = 0.5;

    private NotesStage(
        double areaWidth, double areaHeight, double clearance,
        double overlayWidth, double overlayHeight, double titleBarBottom, double lineHeight)
    {
        AreaWidth = areaWidth;
        AreaHeight = areaHeight;
        Clearance = clearance;
        OverlayWidth = overlayWidth;
        OverlayHeight = overlayHeight;
        TitleBarBottom = titleBarBottom;
        LineHeight = lineHeight;
    }

    /// <summary>The layout viewport's width: the box the overlay's fixed position is measured in.</summary>
    public double AreaWidth { get; }

    /// <summary>The layout viewport's height.</summary>
    public double AreaHeight { get; }

    /// <summary>The fixed edge clearance kept on each side of the travel.</summary>
    public double Clearance { get; }

    /// <summary>The overlay's width as measured — the shown width where the size's width is unset.</summary>
    public double OverlayWidth { get; }

    /// <summary>The overlay's height as measured — a long note's is taller than a short one's.</summary>
    public double OverlayHeight { get; }

    /// <summary>
    /// The title bar's bottom edge, measured from the overlay's top edge — its
    /// height and the overlay's border above it: what the display clamp keeps
    /// inside the area vertically, and where the minimum height starts.
    /// </summary>
    public double TitleBarBottom { get; }

    /// <summary>The text's computed line height, which the minimum height counts lines in.</summary>
    public double LineHeight { get; }

    /// <summary>The least height a resize can leave the overlay at: the title bar and <see cref="MinimumTextLines"/> lines.</summary>
    public double MinimumHeight => TitleBarBottom + MinimumTextLines * LineHeight;

    /// <summary>
    /// A stage from the browser's measurements, or false where they cannot be
    /// one: a length that is not finite, a negative one, or an area or overlay
    /// with no size (an element that is not laid out).
    /// </summary>
    public static bool TryCreate(
        double areaWidth, double areaHeight, double clearance,
        double overlayWidth, double overlayHeight, double titleBarBottom, double lineHeight,
        out NotesStage stage)
    {
        stage = default;
        if (!IsLength(clearance) || !IsLength(titleBarBottom) || !IsLength(lineHeight)) return false;
        if (!IsSize(areaWidth) || !IsSize(areaHeight)) return false;
        if (!IsSize(overlayWidth) || !IsSize(overlayHeight)) return false;
        stage = new NotesStage(areaWidth, areaHeight, clearance, overlayWidth, overlayHeight, titleBarBottom, lineHeight);
        return true;
    }

    /// <summary>
    /// The box <paramref name="preferences"/> show the overlay as: the size
    /// first, through the show bounds, then the position, by the placement
    /// rule with that size and the display clamp applied.
    /// </summary>
    public NotesBox Show(NotesPreferences preferences)
    {
        var (placement, size) = (preferences.Placement, preferences.Size);
        return new(
            Horizontal.Offset(size.Width, placement.Horizontal),
            Vertical.Offset(size.Height, placement.Vertical),
            Horizontal.Rendered(size.Width),
            Vertical.Rendered(size.Height));
    }

    /// <summary>
    /// The preferences after the overlay, shown as <paramref name="from"/> shows
    /// it, is dragged by its title bar by <paramref name="dx"/> and
    /// <paramref name="dy"/> pixels: on each axis with travel, the shown
    /// position moved by the drag and kept within the travel; elsewhere, and
    /// for the size, what <paramref name="from"/> holds.
    /// </summary>
    public NotesPreferences Drag(NotesPreferences from, double dx, double dy) =>
        new(
            NotesPlacement.Create(
                Horizontal.Dragged(from.Size.Width, from.Placement.Horizontal, dx),
                Vertical.Dragged(from.Size.Height, from.Placement.Vertical, dy)),
            from.Size);

    /// <summary>
    /// The preferences after one press of a Move step button: the shown
    /// position moved <see cref="StepFraction"/> of the travel in
    /// <paramref name="step"/>'s direction, kept within the travel.
    /// <paramref name="from"/> itself where the step cannot move — that axis
    /// has no travel, or the overlay is already at that edge — which is how
    /// the caller knows to write nothing.
    /// </summary>
    public NotesPreferences Step(NotesPreferences from, NotesStep step)
    {
        var (placement, size) = (from.Placement, from.Size);
        var moved = step switch
        {
            NotesStep.Left => placement.WithHorizontal(Horizontal.Stepped(size.Width, placement.Horizontal, -1)),
            NotesStep.Right => placement.WithHorizontal(Horizontal.Stepped(size.Width, placement.Horizontal, +1)),
            NotesStep.Up => placement.WithVertical(Vertical.Stepped(size.Height, placement.Vertical, -1)),
            NotesStep.Down => placement.WithVertical(Vertical.Stepped(size.Height, placement.Vertical, +1)),
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Not a Move step."),
        };
        return new(moved, size);
    }

    /// <summary>
    /// The preferences after the overlay, shown as <paramref name="from"/> shows
    /// it, is resized by its bottom-right corner by <paramref name="dx"/> and
    /// <paramref name="dy"/> pixels: on each axis the drag moved, the size and
    /// the placement the resize rule makes of them (see the type's remarks);
    /// on each other axis, both as <paramref name="from"/> holds them.
    /// </summary>
    public NotesPreferences Resize(NotesPreferences from, double dx, double dy)
    {
        var (width, horizontal) = Horizontal.Resized(from.Size.Width, from.Placement.Horizontal, dx);
        var (height, vertical) = Vertical.Resized(from.Size.Height, from.Placement.Vertical, dy);
        return new(NotesPlacement.Create(horizontal, vertical), NotesSize.Create(width, height));
    }

    /// <summary>
    /// The preferences after one press of a size step button: the resize rule
    /// with a delta of <see cref="SizeStepFraction"/> of the axis's range in
    /// <paramref name="step"/>'s direction. <paramref name="from"/> itself
    /// where the step cannot change the size — at the minimum, at the room
    /// from the corner, or with no room to resize in — which is how the caller
    /// knows to write nothing.
    /// </summary>
    public NotesPreferences Resize(NotesPreferences from, NotesSizeStep step) => step switch
    {
        NotesSizeStep.Wider => Resize(from, Horizontal.SizeStep, 0),
        NotesSizeStep.Narrower => Resize(from, -Horizontal.SizeStep, 0),
        NotesSizeStep.Taller => Resize(from, 0, Vertical.SizeStep),
        NotesSizeStep.Shorter => Resize(from, 0, -Vertical.SizeStep),
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Not a size step."),
    };

    /// <summary>The horizontal axis: the title bar spans the overlay, so the display clamp keeps its whole width.</summary>
    private Axis Horizontal => new(AreaWidth, Clearance, OverlayWidth, MinimumWidth, KeptFromStart: null);

    /// <summary>The vertical axis: the display clamp keeps everything down to the title bar's bottom edge.</summary>
    private Axis Vertical => new(AreaHeight, Clearance, OverlayHeight, MinimumHeight, KeptFromStart: TitleBarBottom);

    private static bool IsLength(double value) => double.IsFinite(value) && value >= 0;

    private static bool IsSize(double value) => double.IsFinite(value) && value > 0;

    /// <summary>
    /// One axis of the stage. The two axes are the same arithmetic over their
    /// own lengths, which is all "held independently" asks of the code. Each
    /// method takes the axis's held length and held position, either of which
    /// may be unset.
    /// </summary>
    /// <param name="Area">The layout viewport's length on this axis.</param>
    /// <param name="Clearance">The edge clearance on each side.</param>
    /// <param name="Measured">The overlay's measured length on this axis — the shown length where the size is unset.</param>
    /// <param name="Minimum">The least length a resize can leave the overlay at.</param>
    /// <param name="KeptFromStart">
    /// How much of the overlay, from its start edge, the display clamp keeps
    /// inside the area; <c>null</c> for all of it.
    /// </param>
    private readonly record struct Axis(double Area, double Clearance, double Measured, double Minimum, double? KeptFromStart)
    {
        /// <summary>The area less the clearance on each side: the show bounds' maximum, never below zero.</summary>
        private double Space => Math.Max(0, Area - 2 * Clearance);

        /// <summary>One size step on this axis: <see cref="SizeStepFraction"/> of the range from the minimum to the space.</summary>
        public double SizeStep => Math.Max(0, Space - Minimum) * SizeStepFraction;

        /// <summary>
        /// The length to render for a held <paramref name="length"/>, clamped
        /// to the show bounds — the minimum, unless the space is smaller, and
        /// the space — or <c>null</c> where it is unset.
        /// </summary>
        public double? Rendered(double? length) =>
            length is double held ? Math.Clamp(held, Math.Min(Minimum, Space), Space) : null;

        /// <summary>The shown offset of the overlay's start edge.</summary>
        public double Offset(double? length, double? position) => On(Shown(length)).Offset(position);

        /// <summary>The drag's result on this axis; <paramref name="position"/> where the drag did not move it.</summary>
        public double? Dragged(double? length, double? position, double pixels) =>
            On(Shown(length)).Dragged(position, pixels);

        /// <summary>The step's result on this axis; <paramref name="position"/> where the step cannot move it.</summary>
        public double? Stepped(double? length, double? position, int direction) =>
            On(Shown(length)).Stepped(position, direction);

        /// <summary>
        /// A resize of <paramref name="pixels"/> on this axis: the new length,
        /// within the gesture's bounds, and the position that keeps the start
        /// edge where it was shown — or both held values where the resize does
        /// not change this axis: no movement along it, a length already at the
        /// bound it moves towards, or less room than the minimum.
        /// </summary>
        public (double? Length, double? Position) Resized(double? length, double? position, double pixels)
        {
            double size = Shown(length);
            double corner = On(size).Offset(position);
            double room = Area - Clearance - corner;
            if (pixels == 0 || room < Minimum) return (length, position);

            double to = Math.Clamp(size + pixels, Minimum, room);
            // A resize moves the length the gesture's way or not at all: a
            // shorter step from below the minimum (an unset note shorter than
            // it) never grows the overlay to it.
            if (Math.Sign(to - size) != Math.Sign(pixels) || !(to > 0)) return (length, position);

            var resized = On(to);
            return (to, resized.HasTravel ? resized.PositionAt(corner) : position);
        }

        /// <summary>The overlay's length on this axis as shown: the rendered one where set, the measured one where not.</summary>
        private double Shown(double? length) => Rendered(length) ?? Measured;

        private Line On(double size) => new(Area, Clearance, size, KeptFromStart ?? size);
    }

    /// <summary>
    /// One axis with the overlay's shown length fixed: the placement rule.
    /// </summary>
    /// <param name="Area">The layout viewport's length on this axis.</param>
    /// <param name="Clearance">The edge clearance on each side.</param>
    /// <param name="Size">The overlay's shown length on this axis.</param>
    /// <param name="Kept">How much of the overlay, from its start edge, the display clamp keeps inside the area.</param>
    private readonly record struct Line(double Area, double Clearance, double Size, double Kept)
    {
        private double Travel => Area - 2 * Clearance - Size;

        public bool HasTravel => Travel >= MinimumTravel;

        /// <summary>The shown offset of the overlay's start edge.</summary>
        public double Offset(double? held) => Clamp(Wanted(held));

        /// <summary>The drag's result on this axis; <paramref name="held"/> where the drag did not move it.</summary>
        public double? Dragged(double? held, double pixels) =>
            HasTravel ? MovedBy(held, pixels / Travel) : held;

        /// <summary>The step's result on this axis; <paramref name="held"/> where the step cannot move it.</summary>
        public double? Stepped(double? held, int direction) =>
            HasTravel ? MovedBy(held, direction * StepFraction) : held;

        /// <summary>The position within the travel that shows the start edge at <paramref name="offset"/>, kept within it. Only meaningful with travel.</summary>
        public double PositionAt(double offset) => Math.Clamp((offset - Clearance) / Travel, 0, 1);

        /// <summary>
        /// The shown position moved by <paramref name="fraction"/> of the
        /// travel and kept within it — or <paramref name="held"/>, unchanged,
        /// where that is where it already is.
        /// </summary>
        private double? MovedBy(double? held, double fraction)
        {
            double from = ShownPosition(held);
            double to = Math.Clamp(from + fraction, 0, 1);
            return to == from ? held : to;
        }

        /// <summary>
        /// The position within the travel the overlay is shown at: the held
        /// one (centred if unset), unless the clamp moved it, in which case
        /// the position the clamped offset stands for. Only meaningful with
        /// travel.
        /// </summary>
        private double ShownPosition(double? held)
        {
            double position = held ?? Centre;
            double wanted = Clearance + position * Travel;
            double shown = Clamp(wanted);
            return shown == wanted ? position : PositionAt(shown);
        }

        private double Wanted(double? held) =>
            HasTravel ? Clearance + (held ?? Centre) * Travel : (Area - Size) / 2;

        private double Clamp(double offset) => Math.Clamp(offset, 0, Math.Max(0, Area - Kept));
    }
}
