namespace BgQuiz_Blazor.Client.Quiz;

using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;

/// <summary>
/// The per-app (Scoped, one-per-tab in WASM) <b>user settings</b> the
/// <c>Settings</c> page edits: which side the home board renders on, whether
/// that side is re-rolled per problem, and whether the navigation panel stays
/// folded. Every setting is recorded and persisted the moment it is changed —
/// there is no Apply gesture anywhere in this service. When each becomes
/// <i>visible</i> is a separate question, and the fold answers it differently
/// from the other two: see <see cref="SetKeepNavigationPanelFoldedAsync"/>.
///
/// <para>
/// <b>No draft, no commit, no dirty flag — deliberately.</b> Unlike the
/// start-gate holders (<see cref="AppliedFilter"/> / <see cref="AppliedMix"/> +
/// <see cref="MixDraft"/>), nothing here is composed into a quiz at a Start
/// gesture, so there is no half-edited state to guard against and no gate to
/// derive: a toggle is a complete, immediately valid choice, the same reasoning
/// <see cref="ShuffleOption"/> records. Introducing a draft/commit lifetime split
/// is what produced finding (AK)'s wedge; this service must not grow one.
/// </para>
///
/// <para>
/// <b>No <c>Changed</c> event.</b> The <c>Settings</c> page binds straight to
/// these properties and is the only component that renders them, so there is no
/// second consumer to notify — the state-container pattern
/// <see cref="MixDraft.Changed"/> exists for buys nothing here. Add notify
/// plumbing if (and only if) a real simultaneous consumer appears.
/// </para>
///
/// <para>
/// <b>Defaults reproduce the behavior that shipped before this existed:</b>
/// the home board on the right (the producer's own
/// <c>DiagramRequest.HomeBoardOnRight</c> default), no per-problem randomization,
/// and the navigation panel unfolded.
/// </para>
///
/// <para>
/// <b>Persistence.</b> One localStorage key (<see cref="StorageKey"/>) holding
/// all three settings as one JSON object — see <see cref="ToJson"/> for the wire
/// format and why it is pinned by a test. <see cref="EnsureHydratedAsync"/> is
/// idempotent (the <see cref="MixDraft.EnsureHydratedAsync"/> pattern) but needs
/// no stale-read generation guard: settings have no per-setup lifecycle, so
/// there is no <c>Discard</c> for a read in flight to land behind.
/// </para>
///
/// <para>
/// <b>Hydrate before the first board renders.</b> <c>Home</c> kicks hydration off
/// in its init — every quiz starts there — and <c>Quiz</c> awaits the same cached
/// task, which by then is already completed and therefore costs no extra render
/// pass. That ordering is what keeps a board from painting on the default side
/// and flipping a frame later.
/// </para>
/// </summary>
internal sealed class QuizSettings(IJSRuntime js)
{
    /// <summary>
    /// The single localStorage key holding every setting as one JSON object.
    /// <c>camelCase</c> after the <c>xg_</c> prefix, per the key family
    /// <see cref="MixDraft.StorageKey"/> established.
    ///
    /// <para>
    /// <b><c>internal</c>, and named for its siblings</b> — <c>Help</c>'s data
    /// section names this entry to the user and renders it from here, so the name
    /// a reader verifies in devtools cannot drift from the name this type writes
    /// under. Same discipline, and the same deliberate stop short of
    /// <c>public</c>, as <see cref="QuizLiveMarker.StorageKey"/>.
    /// </para>
    /// </summary>
    internal const string StorageKey = "xg_quizSettings";

    // The wire property names. Fixed strings, never a naming policy: this
    // payload is read by two languages (this service and the navFold.js
    // applier) and the exact bytes are pinned by a test — see ToJson.
    private const string HomeBoardOnRightField = "homeBoardOnRight";
    private const string RandomizeSidePerProblemField = "randomizeSidePerProblem";
    private const string KeepNavigationPanelFoldedField = "keepNavigationPanelFolded";

    // The defaults, named once so the property initializers and the
    // missing-field fallbacks in Restore cannot disagree.
    private const bool DefaultHomeBoardOnRight = true;
    private const bool DefaultRandomizeSidePerProblem = false;
    private const bool DefaultKeepNavigationPanelFolded = false;

    /// <summary>
    /// The global the <c>navFold.js</c> applier publishes — the only way to move
    /// the fold without a navigation. C# cannot reach the control itself (an
    /// uncontrolled checkbox in the statically rendered host layout, see
    /// <c>MainLayoutTests</c>), so this seam exists at all for the one direction
    /// that must not wait: unfolding. The DOM selector stays in the JS module
    /// rather than being restated here.
    /// </summary>
    private const string NavFoldApplyFunction = "bgquizNavFold.apply";

    /// <summary>
    /// True when the on-roll player's home board renders on the right — the
    /// producer's <c>DiagramRequest.HomeBoardOnRight</c>. Governs the board only
    /// while <see cref="RandomizeSidePerProblem"/> is off; compose the two through
    /// <see cref="EffectiveHomeBoardOnRight"/>, never by hand.
    /// </summary>
    public bool HomeBoardOnRight { get; private set; } = DefaultHomeBoardOnRight;

    /// <summary>
    /// True when each problem draws its own side, so a position the user has seen
    /// before can come back mirrored (the anti-memorization ask, and closer to
    /// live play, where the home board's side is not yours to choose).
    /// </summary>
    public bool RandomizeSidePerProblem { get; private set; } = DefaultRandomizeSidePerProblem;

    /// <summary>
    /// True when the user wants the navigation panel to stay folded. This service
    /// owns and persists the value; it cannot restore the fold itself — that is
    /// <c>navFold.js</c>'s job, for the reasons in <see cref="NavFoldApplyFunction"/>.
    /// </summary>
    public bool KeepNavigationPanelFolded { get; private set; } = DefaultKeepNavigationPanelFolded;

    /// <summary>
    /// <b>The one place the two side settings compose.</b> The side a board
    /// actually renders on is this and nothing else: the per-problem roll when
    /// the user asked for randomization, the fixed choice otherwise. Both of
    /// <c>Quiz</c>'s request builders read a single property that calls this, so
    /// no call site re-states the rule and no render branch can drift — a board
    /// that flipped in some views but not others would read as a bug.
    /// </summary>
    /// <param name="randomSide">
    /// The current problem's roll — <see cref="QuizController.RandomHomeBoardOnRight"/>,
    /// which the controller takes unconditionally per problem and holds steady
    /// across submit, review, and redo. Passing it in is what keeps the
    /// controller free of any knowledge of this service.
    /// </param>
    public bool EffectiveHomeBoardOnRight(bool randomSide) =>
        RandomizeSidePerProblem ? randomSide : HomeBoardOnRight;

    /// <summary>
    /// The completed (or in-flight) hydration, so <see cref="EnsureHydratedAsync"/>
    /// reads localStorage once per app however many callers ask.
    /// </summary>
    private Task? _hydration;

    /// <summary>
    /// Load the stored settings — once per app. The first caller runs the read;
    /// later callers get the cached task back, which (being already completed)
    /// an <c>OnInitializedAsync</c> can await without provoking a second render.
    /// A missing key leaves the defaults standing, and a malformed payload does
    /// the same rather than throwing — see <see cref="Restore"/>.
    /// </summary>
    public Task EnsureHydratedAsync() => _hydration ??= HydrateAsync();

    private async Task HydrateAsync()
    {
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        Restore(stored);
    }

    /// <summary>Record the home-board side, applying and persisting immediately.</summary>
    public Task SetHomeBoardOnRightAsync(bool value)
    {
        HomeBoardOnRight = value;
        return PersistAsync();
    }

    /// <summary>Record the randomize-per-problem choice, applying and persisting immediately.</summary>
    public Task SetRandomizeSidePerProblemAsync(bool value)
    {
        RandomizeSidePerProblem = value;
        return PersistAsync();
    }

    /// <summary>
    /// Record the keep-folded choice and persist it — then <b>unfold now, but
    /// never fold now</b>. The asymmetry is the whole contract, and it is
    /// deliberate (finding #50).
    ///
    /// <para>
    /// <b>Turning it on defers to the next navigation.</b> "Keep the navigation
    /// panel folded" describes how pages <i>start</i>, not a fold to perform on
    /// the spot; folding the page the user is standing in strands them — the
    /// panel they just used to get here vanishes, and the checkbox they are
    /// looking at is the only thing that could tell them why. Nothing extra is
    /// needed to defer it: the choice is already in storage, and
    /// <c>navFold.js</c>'s <c>enhancedload</c> handler applies it on the next
    /// navigation, which is also where it self-demonstrates. The checkbox is the
    /// confirmation in the meantime.
    /// </para>
    ///
    /// <para>
    /// <b>Turning it off cannot wait.</b> The user is asking for the panel back,
    /// and with the panel folded there is no navigation available to them that
    /// would deliver it — the links are folded away with it. So the unfold goes
    /// through the applier immediately; without it the setting would be a
    /// one-way door.
    /// </para>
    ///
    /// <para>
    /// The applier is handed its argument <b>explicitly</b> rather than left to
    /// re-read localStorage, deliberately: the seam then carries no dependency on
    /// this method's write having landed first, so the persist and the unfold
    /// below cannot be reordered into a silent bug. It is passed the literal
    /// <c>false</c> rather than <c>value</c> — inside that branch they are the
    /// same bool, and the literal is the one that says <i>unfold</i> at the call
    /// site. (The applier's <i>own</i> storage read stays where it belongs — on
    /// the navigation path, where no C# is running.)
    /// </para>
    /// </summary>
    public async Task SetKeepNavigationPanelFoldedAsync(bool value)
    {
        KeepNavigationPanelFolded = value;
        await PersistAsync();
        if (!value)
        {
            await js.InvokeVoidAsync(NavFoldApplyFunction, false);
        }
    }

    private async Task PersistAsync() =>
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, ToJson());

    /// <summary>
    /// Serialize every setting as one JSON object, hand-written with fixed
    /// property names — the same posture <c>QuizMixJsonConverter</c> takes, and
    /// for a stronger reason here: this payload is a <b>two-language contract</b>.
    /// The <c>navFold.js</c> applier reads
    /// <see cref="KeepNavigationPanelFoldedField"/> out of this object with no
    /// compiler to check it, so the serialized shape — field names included — is
    /// pinned by a unit test asserting the literal bytes. That test, not this
    /// method, is the name's single source of truth for the JS side; renaming a
    /// field here without updating the applier fails there rather than in
    /// production. Every field is always written, so a reader never has to
    /// distinguish "absent" from "false".
    /// </summary>
    private string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean(HomeBoardOnRightField, HomeBoardOnRight);
            writer.WriteBoolean(RandomizeSidePerProblemField, RandomizeSidePerProblem);
            writer.WriteBoolean(KeepNavigationPanelFoldedField, KeepNavigationPanelFolded);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Project a stored payload onto the settings — <b>tolerantly, and never
    /// throwing</b>. A durable format with two readers that later legs will
    /// extend cannot afford a fail-loud restore: a payload written by a newer
    /// build (extra fields), an older one (missing fields), or a hand-edited
    /// devtools session must all leave the app usable. So a missing or
    /// non-boolean field falls back to that setting's default, an unknown field
    /// is ignored, and anything that is not a JSON object — including outright
    /// malformed text — leaves every default standing. Values are read into
    /// locals first so a partial read can never assign a mix of stored and
    /// default state.
    /// </summary>
    private void Restore(string? json)
    {
        if (json is null) return; // never written — the defaults already stand

        bool homeBoardOnRight;
        bool randomizeSidePerProblem;
        bool keepNavigationPanelFolded;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            homeBoardOnRight = ReadBool(root, HomeBoardOnRightField, DefaultHomeBoardOnRight);
            randomizeSidePerProblem =
                ReadBool(root, RandomizeSidePerProblemField, DefaultRandomizeSidePerProblem);
            keepNavigationPanelFolded =
                ReadBool(root, KeepNavigationPanelFoldedField, DefaultKeepNavigationPanelFolded);
        }
        catch (JsonException)
        {
            return; // malformed — every default stands, exactly as on a fresh browser
        }

        HomeBoardOnRight = homeBoardOnRight;
        RandomizeSidePerProblem = randomizeSidePerProblem;
        KeepNavigationPanelFolded = keepNavigationPanelFolded;
    }

    private static bool ReadBool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
}
