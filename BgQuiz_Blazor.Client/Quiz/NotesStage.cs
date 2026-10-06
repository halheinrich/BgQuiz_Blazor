namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The page the decision's notes are placed on, as measured in the browser,
/// and <b>the one home of the placement arithmetic</b> (<c>SPEC-quiz-view.md</c>
/// §4, "The notes overlay's placement is a remembered preference", issue
/// <c>halheinrich/backgammon#344</c>): where a <see cref="NotesPlacement"/> is
/// shown (<see cref="Show"/>), what a drag makes of it (<see cref="Drag"/>),
/// and what a step of the Move control makes of it (<see cref="Step"/>). All
/// lengths are CSS pixels, relative to the visible area's top-left corner.
///
/// <para>
/// <b>The travel.</b> On each axis the overlay travels within the visible
/// area less <see cref="Clearance"/> on each side, less its own size, so a
/// position <c>p</c> from 0 to 1 shows it <c>Clearance + p × travel</c> from
/// the start edge. An unset axis is <c>p = ½</c>, which is the overlay centred
/// — exactly where it opened before the preference existed. An axis whose
/// travel is under <see cref="MinimumTravel"/> (the overlay fills it, as a
/// phone's width does) has none: the overlay shows centred on it whatever is
/// held, and no move changes what is held there.
/// </para>
///
/// <para>
/// <b>The display clamp.</b> Whatever the window, the title bar — and with it
/// the close control — stays inside the visible area: horizontally the whole
/// overlay, which the title bar spans; vertically down to the title bar's
/// bottom edge.
/// Where even that cannot fit, the start edge wins (the left, the top). The
/// clamp changes what <see cref="Show"/> returns and never the placement, so a
/// window that grows again shows the user's placement again. With the
/// stylesheet's size caps (the overlay is never larger than the area less the
/// clearance) the travel already keeps it inside, and the clamp acts only
/// where the title bar outgrows a very short overlay.
/// </para>
///
/// <para>
/// <b>Moves start from what is shown.</b> <see cref="Drag"/> and
/// <see cref="Step"/> begin at the position the overlay is shown at — the held
/// position, unless the clamp moved it — and change only an axis that the move
/// actually moved: an axis with no travel, a drag with no movement along it,
/// or a step already at that edge leaves the held value as it was, unset
/// included. So a caller tells a move that wrote something from one that
/// could not by comparing the result with what it passed in.
/// </para>
/// </summary>
internal readonly record struct NotesStage
{
    /// <summary>
    /// <b>The step size</b>: one press of a Move step button moves the overlay
    /// an eighth of its travel on that axis. A fixed fraction rather than a
    /// fixed length, because the placement is held in fractions of the
    /// travel: from the centre, four steps reach either edge in any window,
    /// and the steps land on eighths there, which a double holds exactly.
    /// </summary>
    internal const double StepFraction = 1.0 / 8;

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
        double overlayWidth, double overlayHeight, double titleBarBottom)
    {
        AreaWidth = areaWidth;
        AreaHeight = areaHeight;
        Clearance = clearance;
        OverlayWidth = overlayWidth;
        OverlayHeight = overlayHeight;
        TitleBarBottom = titleBarBottom;
    }

    /// <summary>The visible area's width: the box the overlay's fixed position is measured in.</summary>
    public double AreaWidth { get; }

    /// <summary>The visible area's height.</summary>
    public double AreaHeight { get; }

    /// <summary>The fixed edge clearance kept on each side of the travel.</summary>
    public double Clearance { get; }

    /// <summary>The overlay's width as shown.</summary>
    public double OverlayWidth { get; }

    /// <summary>The overlay's height as shown — a long note's is taller than a short one's.</summary>
    public double OverlayHeight { get; }

    /// <summary>
    /// The title bar's bottom edge, measured from the overlay's top edge — its
    /// height and the overlay's border above it: what the display clamp keeps
    /// inside the area vertically.
    /// </summary>
    public double TitleBarBottom { get; }

    /// <summary>
    /// A stage from the browser's measurements, or false where they cannot be
    /// one: a length that is not finite, a negative one, or an area or overlay
    /// with no size (an element that is not laid out).
    /// </summary>
    public static bool TryCreate(
        double areaWidth, double areaHeight, double clearance,
        double overlayWidth, double overlayHeight, double titleBarBottom,
        out NotesStage stage)
    {
        stage = default;
        if (!IsLength(clearance) || !IsLength(titleBarBottom)) return false;
        if (!IsSize(areaWidth) || !IsSize(areaHeight)) return false;
        if (!IsSize(overlayWidth) || !IsSize(overlayHeight)) return false;
        stage = new NotesStage(areaWidth, areaHeight, clearance, overlayWidth, overlayHeight, titleBarBottom);
        return true;
    }

    /// <summary>Where <paramref name="placement"/> shows the overlay's top-left corner, the display clamp applied.</summary>
    public NotesPosition Show(NotesPlacement placement) =>
        new(Horizontal.Show(placement.Horizontal), Vertical.Show(placement.Vertical));

    /// <summary>
    /// The placement after the overlay, shown at <paramref name="from"/>, is
    /// dragged by <paramref name="dx"/> and <paramref name="dy"/> pixels: on
    /// each axis with travel, the shown position moved by the drag and kept
    /// within the travel; elsewhere what <paramref name="from"/> holds.
    /// </summary>
    public NotesPlacement Drag(NotesPlacement from, double dx, double dy) =>
        NotesPlacement.Create(
            Horizontal.Dragged(from.Horizontal, dx),
            Vertical.Dragged(from.Vertical, dy));

    /// <summary>
    /// The placement after one press of a Move step button: the shown position
    /// moved <see cref="StepFraction"/> of the travel in <paramref name="step"/>'s
    /// direction, kept within the travel. <paramref name="from"/> itself where
    /// the step cannot move — that axis has no travel, or the overlay is
    /// already at that edge — which is how the caller knows to write nothing.
    /// </summary>
    public NotesPlacement Step(NotesPlacement from, NotesStep step) => step switch
    {
        NotesStep.Left => from.WithHorizontal(Horizontal.Stepped(from.Horizontal, -1)),
        NotesStep.Right => from.WithHorizontal(Horizontal.Stepped(from.Horizontal, +1)),
        NotesStep.Up => from.WithVertical(Vertical.Stepped(from.Vertical, -1)),
        NotesStep.Down => from.WithVertical(Vertical.Stepped(from.Vertical, +1)),
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Not a Move step."),
    };

    /// <summary>The horizontal axis: the title bar spans the overlay, so the clamp keeps its whole width.</summary>
    private Axis Horizontal => new(AreaWidth, Clearance, OverlayWidth, Kept: OverlayWidth);

    /// <summary>The vertical axis: the clamp keeps everything down to the title bar's bottom edge.</summary>
    private Axis Vertical => new(AreaHeight, Clearance, OverlayHeight, Kept: TitleBarBottom);

    private static bool IsLength(double value) => double.IsFinite(value) && value >= 0;

    private static bool IsSize(double value) => double.IsFinite(value) && value > 0;

    /// <summary>
    /// One axis of the stage. The two axes are the same arithmetic over their
    /// own lengths, which is all "held independently" asks of the code.
    /// </summary>
    /// <param name="Area">The visible area's length on this axis.</param>
    /// <param name="Clearance">The edge clearance on each side.</param>
    /// <param name="Size">The overlay's length on this axis.</param>
    /// <param name="Kept">How much of the overlay, from its start edge, the display clamp keeps inside the area.</param>
    private readonly record struct Axis(double Area, double Clearance, double Size, double Kept)
    {
        private double Travel => Area - 2 * Clearance - Size;

        private bool HasTravel => Travel >= MinimumTravel;

        /// <summary>The shown offset of the overlay's start edge.</summary>
        public double Show(double? held) => Clamp(Wanted(held));

        /// <summary>The drag's result on this axis; <paramref name="held"/> where the drag did not move it.</summary>
        public double? Dragged(double? held, double pixels) =>
            HasTravel ? MovedBy(held, pixels / Travel) : held;

        /// <summary>The step's result on this axis; <paramref name="held"/> where the step cannot move it.</summary>
        public double? Stepped(double? held, int direction) =>
            HasTravel ? MovedBy(held, direction * StepFraction) : held;

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
            return shown == wanted ? position : Math.Clamp((shown - Clearance) / Travel, 0, 1);
        }

        private double Wanted(double? held) =>
            HasTravel ? Clearance + (held ?? Centre) * Travel : (Area - Size) / 2;

        private double Clamp(double offset) => Math.Clamp(offset, 0, Math.Max(0, Area - Kept));
    }
}
