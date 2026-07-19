// folderAccess.js — BgQuiz's folder-picking + stats-file module (the app's
// first authored JS). Loaded as an ES module by JsFolderAccess; nothing else
// imports it, and no other script in the app is app-authored.
//
// Two-slot state model:
//   picked slot — populated by a pick gesture (either mechanism); holds the
//     FileSystemDirectoryHandle (FS-Access) or null (fallback), plus a
//     name → FileSystemFileHandle | File map for byte reads.
//   active slot — the directory handle a *running quiz* writes its stats
//     through. Bound only by promoteToActive() at quiz start, so a mid-quiz
//     Clear or re-pick (which only touches the picked slot) never affects the
//     running quiz's recording.
//
// Handles never cross the interop boundary — C# sees names, sizes, bytes, and
// booleans. Expected outcomes are result values (cancelled pick, missing stats
// file, denied permission); only unexpected browser failures throw, surfacing
// as JSException on the C# side.

let pickedHandle = null;      // FileSystemDirectoryHandle | null (fallback picks have none)
let pickedFiles = new Map();  // name -> FileSystemFileHandle | File
let activeHandle = null;      // FileSystemDirectoryHandle | null — the running quiz's stats folder

const PROBLEM_EXTENSIONS = ['.xg', '.xgp'];

function hasProblemExtension(name) {
    const lower = name.toLowerCase();
    return PROBLEM_EXTENSIONS.some(ext => lower.endsWith(ext));
}

export function supportsDirectoryPicker() {
    return typeof window.showDirectoryPicker === 'function';
}

// FS-Access pick: native picker (read mode), then a readwrite permission
// request on the picked handle. AbortError is the user dismissing the picker —
// an expected outcome, returned as { status: 'cancelled' }. If the readwrite
// request auto-denies (some Chromium versions treat the transient user
// activation as consumed by the picker), the single-prompt alternative is
// showDirectoryPicker({ mode: 'readwrite' }) — same C#-visible contract.
export async function pickDirectory() {
    let handle;
    try {
        handle = await window.showDirectoryPicker();
    } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') {
            return { status: 'cancelled', directoryName: '', writable: false, files: [] };
        }
        throw e;
    }

    const writable = await handle.requestPermission({ mode: 'readwrite' }) === 'granted';

    // Top-level enumeration only (user-settled): values() yields direct
    // children; subdirectories are not descended into. Names keep their
    // extension — the DecisionId-stamping contract.
    const files = [];
    const map = new Map();
    for await (const entry of handle.values()) {
        if (entry.kind !== 'file' || !hasProblemExtension(entry.name)) continue;
        const file = await entry.getFile();
        map.set(entry.name, entry);
        files.push({ name: entry.name, size: file.size });
    }

    pickedHandle = handle;
    pickedFiles = map;
    return { status: 'ok', directoryName: handle.name, writable, files };
}

// Fallback gesture: open the hidden webkitdirectory input's native picker.
// The eventual pick arrives via the input's own change event (collected with
// collectFallbackFiles); a dismissal fires nothing.
export function clickElement(element) {
    element.click();
}

// Fallback collection: the browser hands over the folder's whole tree; keep
// only top-level problem files — webkitRelativePath is "folder/file.ext" for
// direct children (exactly one separator). Blazor's InputFile can't see
// webkitRelativePath, which is why this module reads the FileList itself.
export function collectFallbackFiles(inputElement) {
    const all = Array.from(inputElement.files ?? []);
    const topLevel = all.filter(f =>
        hasProblemExtension(f.name) &&
        (f.webkitRelativePath.match(/\//g) ?? []).length === 1);

    const directoryName = all.length > 0 ? all[0].webkitRelativePath.split('/')[0] : '';

    pickedHandle = null;  // no writable handle on this mechanism
    pickedFiles = new Map(topLevel.map(f => [f.name, f]));
    // Allow the same folder to be re-picked later: a change event only fires
    // when the selection differs, so reset the input now that it's collected.
    inputElement.value = '';
    return { directoryName, files: topLevel.map(f => ({ name: f.name, size: f.size })) };
}

// Byte read from the picked slot. Returns the raw ArrayBuffer — the .NET side
// receives it as IJSStreamReference and enforces its own size cap when opening
// the stream.
export async function readFileData(name) {
    const entry = pickedFiles.get(name);
    if (entry === undefined) {
        throw new Error(`No picked file named '${name}'.`);
    }
    const file = entry instanceof File ? entry : await entry.getFile();
    return await file.arrayBuffer();
}

// Quiz-start bind: promote the picked slot's handle to the active slot.
// False = no FS-Access handle picked (fallback pick, cleared, or never
// picked) — the caller's no-stats signal.
export function promoteToActive() {
    activeHandle = pickedHandle;
    return activeHandle !== null;
}

// Stats read via the ACTIVE slot. null = the file doesn't exist yet (a fresh
// corpus — not an error). Anything else unexpected throws.
export async function readStatsFile(fileName) {
    if (activeHandle === null) {
        throw new Error('No active folder to read stats from.');
    }
    let fileHandle;
    try {
        fileHandle = await activeHandle.getFileHandle(fileName);
    } catch (e) {
        if (e instanceof DOMException && e.name === 'NotFoundError') {
            return null;
        }
        throw e;
    }
    const file = await fileHandle.getFile();
    return await file.text();
}

// Stats write via the ACTIVE slot, replacing any existing content.
export async function writeStatsFile(fileName, json) {
    if (activeHandle === null) {
        throw new Error('No active folder to write stats to.');
    }
    const fileHandle = await activeHandle.getFileHandle(fileName, { create: true });
    const stream = await fileHandle.createWritable();
    await stream.write(json);
    await stream.close();
}

// Clear affordance: reset the PICKED slot only. The active slot persists so a
// running quiz keeps recording until the next Start re-binds it.
export function clearPicked() {
    pickedHandle = null;
    pickedFiles = new Map();
}
