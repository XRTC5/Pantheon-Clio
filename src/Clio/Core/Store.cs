using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace Clio.Core;

/// <summary>Where every Clio window sits relative to other windows.</summary>
public enum Layer
{
    /// <summary>Bottom of the Z-order, like Argus: other windows open over it.</summary>
    Desktop,

    /// <summary>Topmost, for reading notes over work.</summary>
    OnTop,
}

public sealed class NoteData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>Shown as REC 007. Never reused after a note is erased.</summary>
    public int Number { get; set; }
    public string Text { get; set; } = "";
    public int Color { get; set; }

    /// <summary>Physical pixels. Restored verbatim, the Argus placement rule.</summary>
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Doodle strokes as base64 ISF, or null for a note never drawn on.</summary>
    public string? Ink { get; set; }

    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Edited { get; set; } = DateTime.Now;

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
    public Layer Layer { get; set; } = Layer.Desktop;

    public bool HasHub { get; set; }
    public int HubLeft { get; set; }
    public int HubTop { get; set; }

    /// <summary>Running counter so a deleted note's number is never reused.</summary>
    public int Serial { get; set; }

    public List<NoteData> Notes { get; set; } = new();
}

/// <summary>
/// One JSON file in %APPDATA%\Clio. Text edits are saved on a short debounce,
/// so typing does not write to disk on every key; moves, resizes and deletes
/// save at once. Writes go to a temp file first, so a crash mid-write can
/// never leave a half-written notes file where the real one was.
/// </summary>
internal static class Store
{
    public static readonly string Directory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clio");

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

    public static void SaveSoon()
    {
        _debounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background,
            (_, _) => { _debounce!.Stop(); Save(); }, Dispatcher.CurrentDispatcher);
        _debounce.Stop();
        _debounce.Start();
    }

    public static void Save()
    {
        _debounce?.Stop();
        try
        {
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
