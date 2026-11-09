using System.Text.Json;

namespace GrabForge.Core;

public sealed class PortableStore
{
    public string Root { get; }
    public string Data => Path.Combine(Root, "data");
    public string Tools => Path.Combine(Root, "tools");
    private readonly SemaphoreSlim writes = new(1);
    public PortableStore(string root) { Root = Path.GetFullPath(root); Directory.CreateDirectory(Data); }
    public string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Root, path));
    public T Load<T>(string name, Func<T> fallback)
    {
        var path = Path.Combine(Data, name);
        if (!File.Exists(path)) return fallback();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json.Options) ?? throw new InvalidDataException("空のデータです。"); }
        catch (JsonException e) { throw new InvalidDataException($"{name}を読み取れません。原本を保持しました。", e); }
    }
    public async Task SaveAsync<T>(string name, T value)
    {
        await writes.WaitAsync();
        try
        {
            var path = Path.Combine(Data, name); var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, Json.Options));
            File.Move(temp, path, true);
        }
        finally { writes.Release(); }
    }
    public List<Preset> LoadPresets()
    {
        var presets = Load("presets.json", BuiltInPresets.Create);
        foreach (var p in presets) Validate(p);
        return presets;
    }
    public static void Validate(Preset p)
    {
        if (p.SchemaVersion != 1) throw new InvalidDataException("未対応のプリセット形式です。");
        if (p.Id == Guid.Empty || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 100) throw new InvalidDataException("プリセット名またはIDが不正です。");
        p.Options.Validate();
    }
    public static async Task<Preset> ImportAsync(string path)
    {
        var p = JsonSerializer.Deserialize<Preset>(await File.ReadAllTextAsync(path), Json.Options) ?? throw new InvalidDataException("プリセットが空です。");
        Validate(p); return p with { Id = Guid.NewGuid(), Options = p.Options.Clone() };
    }
    public static Task ExportAsync(string path, Preset preset) { Validate(preset); return File.WriteAllTextAsync(path, JsonSerializer.Serialize(preset, Json.Options)); }
}
