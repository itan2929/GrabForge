using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using GrabForge.Core;

namespace GrabForge.Desktop;

public sealed class MainViewModel : Observable, IAsyncDisposable
{
    public PortableStore Store { get; } = new(PortableLayout.FindRoot(AppContext.BaseDirectory));
    public ToolManager Tools { get; }
    public DownloadQueue Queue { get; }
    public AppUpdates Updates { get; }
    public AppSettings Settings { get; }
    private readonly HttpClient http = ToolManager.CreateHttpClient();
    public List<Preset> Presets { get; private set; }
    public DownloadOptions Options { get; private set; }
    public MediaInfo? Media { get; private set; }
    public bool Busy { get; private set; }
    public CancellationTokenSource? Operation { get; private set; }
    private TaskCompletionSource? operationFinished;
    private bool shuttingDown;
    public event Action<string>? StatusChanged;
    public event Action? OptionsReplaced;
    public MainViewModel()
    {
        Settings = Store.Load("settings.json", () => new AppSettings());
        Options = Settings.LastOptions.Clone(); Options.Validate(); Presets = Store.LoadPresets();
        Tools = new(Store, http); Queue = new(Store, Tools) { Concurrency = Settings.ConcurrentDownloads }; Updates = new(Store, http);
    }
    public async Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (Busy || shuttingDown) { StatusChanged?.Invoke("別の操作を実行中です。"); return; }
        Busy = true; Operation = new(); operationFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try { await work(Operation.Token); }
        catch (OperationCanceledException) { StatusChanged?.Invoke("操作を中止しました。"); }
        catch (Exception ex) { StatusChanged?.Invoke(ex.Message); MessageBox.Show(ex.Message, "GrabForge", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { Operation.Dispose(); Operation = null; Busy = false; operationFinished.TrySetResult(); }
    }
    public void Report(string message) => Application.Current.Dispatcher.BeginInvoke(() => StatusChanged?.Invoke(message));
    public async Task AnalyzeAsync(string url, CancellationToken ct)
    {
        using var lease = Tools.AcquireUsage(); Report("メディアを解析中…");
        Media = await new MediaService(Tools.Paths, new ToolProcess(), Store).AnalyzeAsync(url, Options, ct);
        var video = Media.Formats.LastOrDefault(f => f.HasVideo && !f.HasAudio) ?? Media.Formats.LastOrDefault(f => f.HasVideo);
        var audio = Media.Formats.LastOrDefault(f => f.HasAudio && !f.HasVideo) ?? Media.Formats.LastOrDefault(f => f.HasAudio);
        // Preserve selector expressions from compatibility presets; concrete IDs belong to the analyzed item.
        if (!Options.VideoFormatId.Contains('[')) Options.VideoFormatId = video?.Id ?? "bestvideo*";
        if (!Options.AudioFormatId.Contains('[')) Options.AudioFormatId = audio?.Id ?? "bestaudio";
        Report($"解析完了 · {Media.Formats.Count}形式");
    }
    public async Task<string> PreviewAsync(CancellationToken ct)
    {
        if (Media is null) return "URLを解析すると、実際の保存名を表示します。";
        using var lease = Tools.AcquireUsage(); return await new MediaService(Tools.Paths, new ToolProcess(), Store).PreviewAsync(Media, Options, ct);
    }
    public async Task SavePresetAsync(string name, bool overwrite)
    {
        Options.Validate(); if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("プリセット名を入力してください。");
        name = name.Trim(); var previous = Presets.FirstOrDefault(p => p.Name == name);
        if (previous is not null && !overwrite) throw new InvalidOperationException("同名のプリセットがあります。別名にするか上書き保存してください。");
        var preset = new Preset(1, previous?.Id ?? Guid.NewGuid(), name.Trim(), Options.Clone()); PortableStore.Validate(preset);
        if (previous is not null) Presets.Remove(previous); Presets.Add(preset); await Store.SaveAsync("presets.json", Presets);
    }
    public void ApplyPreset(Preset p) { PortableStore.Validate(p); Options = p.Options.Clone(); OptionsReplaced?.Invoke(); Report($"「{p.Name}」を適用しました。"); }
    public async Task ImportAsync(string path) { var preset = await PortableStore.ImportAsync(path); Presets.Add(preset); await Store.SaveAsync("presets.json", Presets); }
    public async Task DeletePresetAsync(Preset p) { Presets.Remove(p); await Store.SaveAsync("presets.json", Presets); }
    public async Task SaveSettingsAsync() { Options.Validate(); Settings.LastOptions = Options.Clone(); await Store.SaveAsync("settings.json", Settings); }
    public async ValueTask DisposeAsync() { shuttingDown = true; Operation?.Cancel(); if (Busy && operationFinished is not null) await operationFinished.Task; await Queue.DisposeAsync(); await SaveSettingsAsync(); http.Dispose(); }
}
