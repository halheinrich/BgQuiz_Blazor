namespace BgQuiz_Blazor.Client.Quiz;

using System.Buffers;
using System.Text;
using System.Text.Json;
using BgUiPrimitives_Razor;

/// <summary>
/// The per-app (Scoped, one-per-tab in WASM) holder of the decision's notes'
/// two <b>remembered preferences</b> — the placement (<c>SPEC-quiz-view.md</c>
/// §4, "The notes overlay's placement is a remembered preference", ruled
/// 2026-10-05, issue <c>halheinrich/backgammon#344</c>) and the size (§4, "The
/// notes overlay's size is a remembered preference", ruled 2026-10-25, issue
/// <c>halheinrich/backgammon#414</c>) — as one <see cref="NotesPreferences"/>,
/// and <b>their one reader and writer</b>: nothing else touches
/// <see cref="StorageKey"/>. <c>DecisionNotes</c> shows <see cref="Current"/>
/// and calls <see cref="SetAsync"/> with what a move, a resize or Reset made
/// of it.
///
/// <para>
/// <b>Its own key, not a field of <c>xg_quizSettings</c>.</b> The preferences
/// are not Settings-page choices: they are set by moving and resizing the
/// notes, written on every completed gesture, and have their own failure rule.
/// Kept apart, the settings entry stays what Help says it is — the Settings
/// page's choices and nothing else — its wire format and <c>navFold.js</c>'s
/// reading of it are untouched, and a fault in either payload cannot cost the
/// other.
/// </para>
///
/// <para>
/// <b>One entry for both.</b> One gesture can change both — a resize writes
/// the placement that keeps the overlay's corner where it was — and one Reset
/// clears both, so they are written together and removed together. The key
/// is the placement's, from before the size existed: Help renders it and a
/// rename would cost either a migration or every stored placement.
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
/// missing, malformed or out of range, or a read the browser refuses, reads as
/// unset (<see cref="EnsureLoadedAsync"/>) — each preference on its own, so a
/// fault in one leaves the other standing. A write the browser refuses leaves
/// the new preferences in memory for the rest of the session
/// (<see cref="SetAsync"/>). Both refusals are logged as warnings and neither
/// is surfaced as an error: the notes open, close, move and resize regardless.
/// A refusal is reported to <see cref="BrowserStorageCondition"/>, the one
/// fact <c>Home</c>'s storage notice says (halheinrich/backgammon#360). Every
/// call goes through <see cref="ReportingBrowserStorage"/>, which reports and
/// logs a refusal in this store's words, and whose <see cref="BrowserStorage"/>
/// tells the browser's refusal from every other failure — the one line this
/// store does not draw — and keeps no latch, so a refusal disables no later
/// call.
/// </para>
/// </summary>
internal sealed class NotesPreferencesStore(ReportingBrowserStorage storage)
{
    /// <summary>
    /// The localStorage key holding both preferences as one JSON object, in
    /// the <c>xg_</c> family <c>MixDraft.StorageKey</c> established. Absent
    /// while both are unset: Reset removes it.
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
    private const string WidthField = "width";
    private const string HeightField = "height";

    /// <summary>The load, once per app; see <see cref="EnsureLoadedAsync"/>.</summary>
    private Task? _load;

    /// <summary>
    /// Set by the first <see cref="SetAsync"/>, so a load still reading when
    /// the user's first gesture lands cannot put the stored value back over it.
    /// </summary>
    private bool _setSinceLoadBegan;

    /// <summary>
    /// The preferences as this session holds them: what was stored, until a
    /// move, a resize or Reset changes them. <see cref="NotesPreferences.Unset"/>
    /// until <see cref="EnsureLoadedAsync"/> has read storage.
    /// </summary>
    public NotesPreferences Current { get; private set; }

    /// <summary>
    /// Read the stored preferences into <see cref="Current"/> — once per app;
    /// later callers get the same task. Each preference reads as unset unless
    /// its own fields are well formed — a position from 0 to 1 or a positive
    /// length, or <c>null</c> — and both read as unset for no entry, text that
    /// is not a JSON object, and a read the browser refuses. Never throws for
    /// storage.
    /// </summary>
    public Task EnsureLoadedAsync() => _load ??= LoadAsync();

    /// <summary>
    /// Make <paramref name="preferences"/> the preferences: in memory at once,
    /// then in storage — written, or removed when both are unset. A write the
    /// browser refuses is logged and reported, and the preferences stay in
    /// memory for the session.
    /// Never throws for storage.
    /// </summary>
    public async Task SetAsync(NotesPreferences preferences)
    {
        _setSinceLoadBegan = true;
        Current = preferences;
        const string consequence = "the notes' placement and size are kept for this session only";
        if (preferences.IsUnset)
            await storage.RemoveAsync(BrowserStorageArea.Local, StorageKey, consequence);
        else
            await storage.WriteAsync(BrowserStorageArea.Local, StorageKey, ToJson(preferences), consequence);
    }

    private async Task LoadAsync()
    {
        var read = await storage.ReadAsync(
            BrowserStorageArea.Local, StorageKey, "the notes open centred at their first size");
        if (read.IsRefused) return;
        if (!_setSinceLoadBegan) Current = Parse(read.Value);
    }

    /// <summary>
    /// Both preferences as one JSON object with all four fields always
    /// written, an unset axis as JSON <c>null</c> — hand-written with fixed
    /// property names, the posture <see cref="QuizSettings"/>' payload takes,
    /// so the format is the pinned bytes and not a serializer's choice. Field
    /// order is append-only, as there: the placement's two, then the size's.
    /// </summary>
    private static string ToJson(NotesPreferences preferences)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WriteNumber(writer, HorizontalField, preferences.Placement.Horizontal);
            WriteNumber(writer, VerticalField, preferences.Placement.Vertical);
            WriteNumber(writer, WidthField, preferences.Size.Width);
            WriteNumber(writer, HeightField, preferences.Size.Height);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// A stored payload as the preferences — both unset unless it is a JSON
    /// object. Each preference is then read from its own two fields, each a
    /// number, <c>null</c> or absent (an absent or <c>null</c> field is an
    /// unset axis), and all or nothing <i>within</i> that preference: one
    /// unusable field makes that preference unset, never half of it, and
    /// leaves the other preference as stored. Fields it does not know are
    /// ignored, so a newer build's payload still reads, and a payload written
    /// before the size existed reads as its placement and an unset size.
    /// </summary>
    private static NotesPreferences Parse(string? json)
    {
        if (json is null) return NotesPreferences.Unset;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return NotesPreferences.Unset;
            return new(ParsePlacement(root), ParseSize(root));
        }
        catch (JsonException)
        {
            return NotesPreferences.Unset;
        }
    }

    private static NotesPlacement ParsePlacement(JsonElement root) =>
        TryReadNumber(root, HorizontalField, out var horizontal)
        && TryReadNumber(root, VerticalField, out var vertical)
        && NotesPlacement.TryCreate(horizontal, vertical, out var placement)
            ? placement
            : NotesPlacement.Unset;

    private static NotesSize ParseSize(JsonElement root) =>
        TryReadNumber(root, WidthField, out var width)
        && TryReadNumber(root, HeightField, out var height)
        && NotesSize.TryCreate(width, height, out var size)
            ? size
            : NotesSize.Unset;

    private static void WriteNumber(Utf8JsonWriter writer, string name, double? number)
    {
        if (number is double value)
            writer.WriteNumber(name, value);
        else
            writer.WriteNull(name);
    }

    private static bool TryReadNumber(JsonElement root, string name, out double? number)
    {
        number = null;
        if (!root.TryGetProperty(name, out var value)) return true;
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return true;
            case JsonValueKind.Number when value.TryGetDouble(out var parsed):
                number = parsed;
                return true;
            default:
                return false;
        }
    }
}
