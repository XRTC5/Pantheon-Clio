using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace Clio.Core;

/// <summary>Where every Clio window sits relative to other windows.</summary>
public enum Layer
{
    /// <summary>
    /// Retired: pinned to the bottom of the Z-order, like Argus. RTC found a
    /// note he could click but never bring forward was the wrong default.
    /// Kept only so an older notes.json still parses; Load maps it to Normal.
    /// </summary>
    Desktop,

    /// <summary>Topmost, for reading notes over work.</summary>
    OnTop,

    /// <summary>An ordinary window: a click brings a note forward, a click on Chrome sends it back.</summary>
    Normal,
}

public sealed class NoteData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>Shown as REC 007. Never reused after a note is erased.</summary>
    public int Number { get; set; }
    /// <summary>Plain text: what History previews and what an old notes.json held.</summary>
    public string Text { get; set; } = "";

    /// <summary>The formatted document as XAML, or null for a note never formatted or typed in since v2.</summary>
    public string? Rich { get; set; }

    public int Color { get; set; }

    /// <summary>A colour from the Colours+ palette as 0xRRGGBB. Overrides <see cref="Color"/>; null means use it.</summary>
    public int? Rgb { get; set; }

    /// <summary>
    /// See-through body: the middle of the note shows the desktop behind it,
    /// while the header, border and brackets stay solid. Per note.
    /// </summary>
    public bool Clear { get; set; }

    /// <summary>Locked on top: this note stays above other windows whatever the shared layer is.</summary>
    public bool Pinned { get; set; }

    /// <summary>Pasted pictures, free-floating over the body. Files live in Store.ImagesDir.</summary>
    public List<NoteImage> Images { get; set; } = new();

    /// <summary>
    /// Drawings, one per doodle session (RTC's call: everything between the pen
    /// and Done is one drawing). Replaces the single <see cref="Ink"/> blob, which
    /// an older note still has and which is migrated into one drawing on load.
    /// </summary>
    public List<NoteDrawing> Drawings { get; set; } = new();

    /// <summary>Free-floating text boxes (Advanced: Free Drag Writing), each its own rich text.</summary>
    public List<NoteTextBox> TextBoxes { get; set; } = new();

    /// <summary>Groups (Advanced: Grouping): each a list of stack ids that select and move together.</summary>
    public List<List<string>> Groups { get; set; } = new();

    /// <summary>
    /// Draw order of everything on the note, bottom first: "text", "ink", and
    /// each picture by file name. Empty or partial (an older note) resolves to
    /// the default: pictures at the back, then drawings, then writing on top.
    /// </summary>
    public List<string> Stack { get; set; } = new();

    /// <summary>Physical pixels. Restored verbatim, the Argus placement rule.</summary>
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Doodle strokes as base64 ISF, or null for a note never drawn on.</summary>
    public string? Ink { get; set; }

    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Edited { get; set; } = DateTime.Now;

    /// <summary>When it was erased. Set only on notes in the History.</summary>
    public DateTime? Erased { get; set; }

    [JsonIgnore]
    internal Native.RECT Rect
    {
        get => new() { Left = Left, Top = Top, Right = Left + Width, Bottom = Top + Height };
        set { Left = value.Left; Top = value.Top; Width = value.Width; Height = value.Height; }
    }
}

public sealed class ClioState
{
    public bool Collapsed { get; set; }
    public Layer Layer { get; set; } = Layer.Normal;

    public bool HasHub { get; set; }
    public int HubLeft { get; set; }
    public int HubTop { get; set; }

    /// <summary>Running counter so a deleted note's number is never reused.</summary>
    public int Serial { get; set; }

    public List<NoteData> Notes { get; set; } = new();

    /// <summary>Erased notes, kept for <see cref="HistoryDays"/> and then purged.</summary>
    public List<NoteData> History { get; set; } = new();

    public int HistoryDays { get; set; } = 5;

    public bool RunOnStartup { get; set; } = true;

    /// <summary>Ask GitHub once per start whether a newer Clio exists (a dot on the hub).</summary>
    public bool CheckUpdates { get; set; } = true;

    /// <summary>
    /// Advanced Features, the master switch. Off is plain Clio: no picture
    /// pasting, nothing to drag or restack, no Alt behaviour, no Arrange button.
    /// </summary>
    public bool Advanced { get; set; }

    /// <summary>Advanced: paste pictures; pictures and drawings drag and restack.</summary>
    public bool AdvImages { get; set; }

    /// <summary>Advanced: click empty space to start a text box; boxes drag and restack.</summary>
    public bool AdvWriting { get; set; }

    [JsonIgnore]
    public bool WritingOn => Advanced && AdvWriting;

    /// <summary>Advanced: Alt+Shift+drag draws a freehand selection loop.</summary>
    public bool AdvLasso { get; set; }

    [JsonIgnore]
    public bool LassoOn => Advanced && AdvLasso;

    /// <summary>Advanced: group selected items so they select and move as one.</summary>
    public bool AdvGroups { get; set; }

    [JsonIgnore]
    public bool GroupsOn => Advanced && AdvGroups;

    /// <summary>Advanced: the note and pen colour pickers offer the full palette.</summary>
    public bool AdvColours { get; set; }

    [JsonIgnore]
    public bool ColoursOn => Advanced && AdvColours;

    /// <summary>Pen thickness in device-independent units, shared by every note.</summary>
    public double PenSize { get; set; } = 2.2;

    /// <summary>Pictures and drawings are movable only with both switches on.</summary>
    [JsonIgnore]
    public bool ImagesOn => Advanced && AdvImages;

    /// <summary>Drops History entries older than the retention. True if any went.</summary>
    /// <summary>Drops History entries older than the retention, deleting their picture files.</summary>
    public bool Purge()
    {
        var cutoff = DateTime.Now.AddDays(-Math.Max(1, HistoryDays));
        var gone = History.Where(n => n.Erased is { } at && at < cutoff).ToList();
        foreach (var n in gone)
        {
            History.Remove(n);
            Store.DeleteImages(n);
        }
        return gone.Count > 0;
    }
}

/// <summary>
/// One pasted picture. Position and size are in device-independent units
/// relative to the note's body, so they survive a move between displays.
/// </summary>
/// <summary>
/// One drawing: strokes as base64 ISF in the note body's coordinates, plus the
/// offset it has been dragged by since (folded back into the strokes the next
/// time the note is drawn on).
/// </summary>
public sealed class NoteDrawing
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Ink { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>A text box placed anywhere on the note: its own formatted text, position and width.</summary>
public sealed class NoteTextBox
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string? Rich { get; set; }
    public string Text { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 180;
}

public sealed class NoteImage
{
    public string File { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}

/// <summary>
/// One JSON file in %APPDATA%\Clio. Text edits are saved on a short debounce,
/// so typing does not write to disk on every key; moves, resizes and deletes
/// save at once. Writes go to a temp file first, so a crash mid-write can
/// never leave a half-written notes file where the real one was.
/// </summary>
internal static class Store
{
    /// <summary>
    /// %APPDATA%\Clio, or CLIO_DATA when set (a portable copy, or a test run
    /// that must not touch real notes). Setting APPDATA does NOT redirect it:
    /// GetFolderPath asks the shell's known-folder API, not the environment.
    /// </summary>
    public static readonly string Directory =
        Environment.GetEnvironmentVariable("CLIO_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clio");

    private static readonly string FilePath = Path.Combine(Directory, "notes.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static DispatcherTimer? _debounce;

    public static ClioState State { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                State = JsonSerializer.Deserialize<ClioState>(File.ReadAllText(FilePath), Json) ?? new();
            if (State.Layer == Layer.Desktop) State.Layer = Layer.Normal;
        }
        catch (Exception ex)
        {
            // Never overwrite a file we failed to read: keep it aside so no
            // note is lost to a parse error, and start empty.
            Log.Error("notes.json unreadable; kept as notes.bad.json", ex);
            try { File.Copy(FilePath, Path.Combine(Directory, "notes.bad.json"), overwrite: true); } catch { }
            State = new();
        }
    }

    /// <summary>
    /// Writes %APPDATA%\Clio\app.json with the path Clio is running from. This
    /// is how Argus, if it is installed, finds Clio and offers Start and Stop
    /// for it; nothing in Clio depends on Argus being there. Written only when
    /// the path changes, so Argus re-reads it only when there is news.
    /// </summary>
    /// <summary>Pasted pictures, as PNG files, so notes.json stays small.</summary>
    public static string ImagesDir => Path.Combine(Directory, "images");

    /// <summary>
    /// Deletes picture files no note or History entry lists. A removed picture's
    /// file is kept while Clio runs so undo can bring it back; this sweeps the
    /// leftovers at the next start.
    /// </summary>
    public static void SweepImages()
    {
        try
        {
            if (!System.IO.Directory.Exists(ImagesDir)) return;
            var used = State.Notes.Concat(State.History).SelectMany(n => n.Images).Select(i => i.File)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var f in System.IO.Directory.GetFiles(ImagesDir))
                if (!used.Contains(Path.GetFileName(f))) File.Delete(f);
        }
        catch (Exception ex)
        {
            Log.Warn($"picture sweep failed: {ex.Message}");
        }
    }

    /// <summary>Deletes a note's picture files. Only for a note that is gone for good.</summary>
    public static void DeleteImages(NoteData note)
    {
        foreach (var img in note.Images)
        {
            try { File.Delete(Path.Combine(ImagesDir, img.File)); }
            catch (Exception ex) { Log.Warn($"picture {img.File} not deleted: {ex.Message}"); }
        }
    }

    public static void Announce()
    {
        try
        {
            var file = Path.Combine(Directory, "app.json");
            var json = JsonSerializer.Serialize(new { ExePath = Environment.ProcessPath, Version = 1 }, Json);
            if (File.Exists(file) && File.ReadAllText(file) == json) return;
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(file, json);
        }
        catch (Exception ex)
        {
            Log.Warn($"app.json not written: {ex.Message}");
        }
    }

    public static void SaveSoon()
    {
        _debounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background,
            (_, _) => { _debounce!.Stop(); Save(); }, Dispatcher.CurrentDispatcher);
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>
    /// Raised just before a write. Open notes serialise their formatted text
    /// here rather than on every keystroke: turning a FlowDocument into XAML is
    /// the expensive part of a save, so it happens once per save, not per key.
    /// </summary>
    public static event Action? Flushing;

    public static void Save()
    {
        _debounce?.Stop();
        try
        {
            Flushing?.Invoke();
            System.IO.Directory.CreateDirectory(Directory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(State, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("save failed", ex);
        }
    }
}
