using System.Text.Json;

namespace AndroidOnWindows.Services;

public sealed class KeyMapping
{
    public string Key { get; set; } = "";        // e.g. "A", "Space", "F1"
    public int X { get; set; }                    // device pixel coords (current screen)
    public int Y { get; set; }
    public string? Package { get; set; }          // optional: only active for this app
}

/// <summary>
/// A pragmatic keyboard mapper: while "mapping mode" is on, each mapped key is turned
/// into a tap on the Android device (adb input tap). This covers the typical case of
/// driving a game/touch UI from the keyboard without BlueStacks-style overlays.
/// </summary>
public sealed class KeyMapService
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private readonly string _file = Path.Combine(AppPaths.Root, "keymap.json");

    public List<KeyMapping> Mappings { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(_file))
                Mappings = JsonSerializer.Deserialize<List<KeyMapping>>(File.ReadAllText(_file)) ?? new();
        }
        catch (Exception ex) { Log.Warn($"keymap load failed: {ex.Message}"); }
    }

    public void Save()
    {
        try { File.WriteAllText(_file, JsonSerializer.Serialize(Mappings, Opts)); }
        catch (Exception ex) { Log.Warn($"keymap save failed: {ex.Message}"); }
    }

    public bool TryGet(string key, out KeyMapping mapping)
    {
        mapping = Mappings.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase))!;
        return mapping is not null;
    }

    public void AddOrUpdate(KeyMapping m)
    {
        Mappings.RemoveAll(x => string.Equals(x.Key, m.Key, StringComparison.OrdinalIgnoreCase));
        Mappings.Add(m);
        Save();
    }

    public void Remove(string key)
    {
        Mappings.RemoveAll(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        Save();
    }
}
