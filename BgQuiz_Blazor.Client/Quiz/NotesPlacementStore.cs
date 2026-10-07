namespace BgQuiz_Blazor.Client.Quiz;

using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

/// <summary>
/// The per-app (Scoped, one-per-tab in WASM) holder of the decision's notes'
/// <b>placement preference</b> (<c>SPEC-quiz-view.md</c> §4, "The notes
/// overlay's placement is a remembered preference", ruled 2026-10-05, issue
/// <c>halheinrich/backgammon#344</c>), and <b>its one reader and writer</b>:
/// nothing else touches <see cref="StorageKey"/>. <c>DecisionNotes</c> shows
/// <see cref="Current"/> and calls <see cref="SetAsync"/> with what a move or
/// Reset made of it.
///
/// <para>
/// <b>Its own key, not a field of <c>xg_quizSettings</c>.</b> The preference
/// is not a Settings-page choice: it is set by moving the notes, written on
/// every completed move, and has its own failure rule. Kept apart, the
/// settings entry stays what Help says it is — the Settings page's choices and
/// nothing else — its wire format and <c>navFold.js</c>'s reading of it are
/// untouched, and a fault in either payload cannot cost the other.
/// </para>
///
/// <para>
/// <b>What it survives.</b> Everything in §6's table: in memory for the life
/// of the app, so closing the notes, the next problem, navigating away and
/// back, End quiz and the next Start all find it here; and in localStorage
/// under <see cref="StorageKey"/>, so a reload or a new visit finds it there.
/// Open/closed is not here and never will be — that bit is
/// <c>DecisionNotes</c>' own and is not persisted.
/// </para>
///
/// <para>
/// <b>Storage that fails never stops the notes.</b> A stored value that is
/// missing, malformed or out of range, or storage that throws, reads as
/// <see cref="NotesPlacement.Unset"/> (<see cref="EnsureLoadedAsync"/>). A
/// write that throws leaves the new placement in memory for the rest of the
/// session (<see cref="SetAsync"/>). Both are logged as warnings and neither
/// is surfaced as an error: the notes open, close and move regardless. A
/// refusal is reported to <see cref="BrowserStorageCondition"/>, the one fact
/// <c>Home</c>'s storage notice says (halheinrich/backgammon#360).
/// </para>
/// </summary>
internal sealed class NotesPlacementStore(
    IJSRuntime js, ILogger<NotesPlacementStore> logger, BrowserStorageCondition storage)
{
    /// <summary>
    /// The localStorage key holding the placement as one JSON object, in the
    /// <c>xg_</c> family <see cref="MixDraft.StorageKey"/> established.
    /// Absent while the preference is unset: Reset removes it.
    ///
    /// <para>
    /// <b><c>internal</c>, and named for its siblings</b> — <c>Help</c>'s data
    /// section names this entry to the user and renders it from here, so the
    /// name a reader checks in devtools cannot drift from the name this type
    /// writes under; the same discipline as <see cref="QuizSettings.StorageKey"/>.
    /// </para>
    /// </summary>
    internal const string StorageKey = "xg_notesPlacement";

    // The wire property names, fixed strings rather than a naming policy: the
    // exact bytes are pinned by a test (see ToJson).
    private const string HorizontalField = "horizontal";
    private const string VerticalField = "vertical";

    /// <summary>The load, once per app; see <see cref="EnsureLoadedAsync"/>.</summary>
    private Task? _load;

    /// <summary>
    /// Set by the first <see cref="SetAsync"/>, so a load still reading when
    /// the user's first move lands cannot put the stored value back over it.
    /// </summary>
    private bool _setSinceLoadBegan;

    /// <summary>
    /// The preference as this session holds it: what was stored, until a move
    /// or Reset changes it. <see cref="NotesPlacement.Unset"/> until
    /// <see cref="EnsureLoadedAsync"/> has read storage.
    /// </summary>
    public NotesPlacement Current { get; private set; }

    /// <summary>
    /// Read the stored preference into <see cref="Current"/> — once per app;
    /// later callers get the same task. Anything but a well-formed payload
    /// reads as unset: no entry, text that is not a JSON object, a position
    /// that is not a number from 0 to 1 (or <c>null</c>), and storage that
    /// throws. Never throws itself.
    /// </summary>
    public Task EnsureLoadedAsync() => _load ??= LoadAsync();

    /// <summary>
    /// Make <paramref name="placement"/> the preference: in memory at once,
    /// then in storage — written, or removed when it is unset. A write that
    /// fails is logged and the placement stays in memory for the session.
    /// Never throws for storage.
    /// </summary>
    public async Task SetAsync(NotesPlacement placement)
    {
        _setSinceLoadBegan = true;
        Current = placement;
        try
        {
            if (placement.IsUnset)
                await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
            else
                await js.InvokeVoidAsync("localStorage.setItem", StorageKey, ToJson(placement));
        }
        catch (JSException e)
        {
            logger.LogWarning(e,
                "The notes' placement could not be saved to browser storage ({Key}); it is kept for this session only.",
                StorageKey);
            storage.ReportRefused();
        }
    }

    private async Task LoadAsync()
    {
        string? stored;
        try
        {
            stored = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        }
        catch (JSException e)
        {
            logger.LogWarning(e,
                "The notes' placement could not be read from browser storage ({Key}); the notes open centred.",
                StorageKey);
            storage.ReportRefused();
            return;
        }
        if (!_setSinceLoadBegan) Current = Parse(stored);
    }

    /// <summary>
    /// The placement as one JSON object with both fields always written, an
    /// unset axis as JSON <c>null</c> — hand-written with fixed property
    /// names, the posture <see cref="QuizSettings"/>' payload takes, so the
    /// format is the pinned bytes and not a serializer's choice. Field order
    /// is append-only, as there.
    /// </summary>
    private static string ToJson(NotesPlacement placement)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WritePosition(writer, HorizontalField, placement.Horizontal);
            WritePosition(writer, VerticalField, placement.Vertical);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// A stored payload as a placement — <see cref="NotesPlacement.Unset"/>
    /// unless it is a JSON object whose <c>horizontal</c> and <c>vertical</c>
    /// fields are each a number from 0 to 1, <c>null</c> or absent (an absent
    /// or <c>null</c> field is an unset axis). Fields it does not know are
    /// ignored, so a newer build's payload still reads. All or nothing: one
    /// unusable field makes the whole payload unset, never half of it.
    /// </summary>
    private static NotesPlacement Parse(string? json)
    {
        if (json is null) return NotesPlacement.Unset;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return NotesPlacement.Unset;
            if (!TryReadPosition(root, HorizontalField, out var horizontal)) return NotesPlacement.Unset;
            if (!TryReadPosition(root, VerticalField, out var vertical)) return NotesPlacement.Unset;
            return NotesPlacement.TryCreate(horizontal, vertical, out var placement) ? placement : NotesPlacement.Unset;
        }
        catch (JsonException)
        {
            return NotesPlacement.Unset;
        }
    }

    private static void WritePosition(Utf8JsonWriter writer, string name, double? position)
    {
        if (position is double value)
            writer.WriteNumber(name, value);
        else
            writer.WriteNull(name);
    }

    private static bool TryReadPosition(JsonElement root, string name, out double? position)
    {
        position = null;
        if (!root.TryGetProperty(name, out var value)) return true;
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return true;
            case JsonValueKind.Number when value.TryGetDouble(out var number):
                position = number;
                return true;
            default:
                return false;
        }
    }
}
