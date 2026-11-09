using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;

namespace GrabForge.Core;

public sealed class DownloadQueue : IAsyncDisposable
{
    private readonly PortableStore store;
    private readonly ToolManager tools;
    private readonly ToolProcess process = new();
    private readonly Channel<DownloadJob> pending = Channel.CreateUnbounded<DownloadJob>();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<Guid, (CancellationTokenSource Token, Process? Process)> running = [];
    private readonly Dictionary<Guid, JobState> pausedStates = [];
    private readonly List<DownloadJob> jobs;
    private readonly Task[] workers;
    private readonly object gate = new();
    private int active;
    public int Concurrency { get; set; } = 2;
    public event Action? Updated;
    public bool IsBusy { get { lock (gate) return running.Count > 0 || jobs.Any(j => j.State is JobState.Queued or JobState.Downloading or JobState.Processing or JobState.Paused); } }
    public IReadOnlyList<DownloadJob> Jobs { get { lock (gate) return jobs.OrderByDescending(j => j.CreatedAt).ToArray(); } }
    public string[] GetLog(DownloadJob job) { lock (gate) return job.Log.ToArray(); }
    public DownloadQueue(PortableStore store, ToolManager tools)
    {
        this.store = store; this.tools = tools;
        jobs = store.Load<List<DownloadJob>>("history.json", () => []);
        foreach (var job in jobs.Where(j => j.State is JobState.Queued or JobState.Downloading or JobState.Processing or JobState.Paused)) { job.State = JobState.Interrupted; job.Error = "前回の実行が中断されました。再試行できます。"; }
        workers = Enumerable.Range(0, 8).Select(_ => Task.Run(WorkerAsync)).ToArray();
    }
    public async Task AddAsync(IEnumerable<string> urls, DownloadOptions options, string? title = null)
    {
        options.Validate(); tools.Paths.Ensure("yt-dlp", "ffmpeg", "ffprobe", "deno");
        var list = urls.Select(u => u.Trim()).Where(u => u.Length > 0).ToArray();
        if (list.Length is 0 or > 100) throw new ArgumentException("URLは1～100件を指定してください。");
        foreach (var url in list)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("http/httpsのURLを指定してください。");
        }
        foreach (var url in list)
        {
            var job = new DownloadJob { Url = url, Title = list.Length == 1 ? title ?? url : url, Options = options.Clone(), State = JobState.Queued };
            lock (gate) jobs.Add(job); await pending.Writer.WriteAsync(job, lifetime.Token);
        }
        await PersistAsync(); Updated?.Invoke();
    }
    public async Task RetryAsync(DownloadJob job)
    {
        lock (gate)
        {
            if (running.ContainsKey(job.Id) || job.State is not (JobState.Error or JobState.Cancelled or JobState.Interrupted)) throw new InvalidOperationException("処理が終了した失敗・中断ジョブだけ再試行できます。");
            job.State = JobState.Queued; job.Error = ""; job.Progress = 0; job.Log.Clear();
            job.OutputPath = ""; job.OutputFiles.Clear();
        }
        await pending.Writer.WriteAsync(job, lifetime.Token); await PersistAsync(); Updated?.Invoke();
    }
    public async Task PauseAsync(DownloadJob job)
    {
        lock (gate)
        {
            if (!running.TryGetValue(job.Id, out var run) || run.Process is null || run.Process.HasExited) throw new InvalidOperationException("現在停止できる処理がありません。");
            if (job.State == JobState.Paused) { ProcessTree.Resume(run.Process.Id); job.State = pausedStates.GetValueOrDefault(job.Id, JobState.Downloading); pausedStates.Remove(job.Id); }
            else if (job.State is JobState.Downloading or JobState.Processing) { ProcessTree.Suspend(run.Process.Id); pausedStates[job.Id] = job.State; job.State = JobState.Paused; }
            else throw new InvalidOperationException("実行中のジョブだけ一時停止できます。");
        }
        await PersistAsync(); Updated?.Invoke();
    }
    public async Task CancelAsync(DownloadJob job)
    {
        lock (gate)
        {
            if (job.State is JobState.Complete or JobState.Error or JobState.Cancelled) return;
            var paused = job.State == JobState.Paused; job.State = JobState.Cancelled;
            if (running.TryGetValue(job.Id, out var run)) { try { if (paused && run.Process is not null && !run.Process.HasExited) ProcessTree.Resume(run.Process.Id); } catch { } run.Token.Cancel(); }
        }
        await PersistAsync(); Updated?.Invoke();
    }
    public async Task ClearFinishedAsync()
    {
        lock (gate) jobs.RemoveAll(j => !running.ContainsKey(j.Id) && j.State is JobState.Complete or JobState.Error or JobState.Cancelled or JobState.Interrupted);
        await PersistAsync(); Updated?.Invoke();
    }
    private async Task WorkerAsync()
    {
        try
        {
            await foreach (var job in pending.Reader.ReadAllAsync(lifetime.Token))
            {
                bool acquired = false;
                while (!acquired)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    lock (gate) { if (active < Math.Clamp(Concurrency, 1, 8)) { active++; acquired = true; } }
                    if (!acquired) await Task.Delay(100, lifetime.Token);
                }
                try { if (job.State == JobState.Queued) await RunAsync(job); }
                finally { lock (gate) active--; }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    private async Task RunAsync(DownloadJob job)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        lock (gate) { if (job.State != JobState.Queued) return; running[job.Id] = (cancellation, null); job.State = JobState.Downloading; }
        var files = new List<string>();
        void Started(Process p) { lock (gate) { running[job.Id] = (cancellation, p); if (job.State == JobState.Paused) ProcessTree.Suspend(p.Id); } }
        void Log(string text)
        {
            lock (gate)
            {
                if (text.StartsWith("CF_PROGRESS|"))
                {
                    var pieces = text.Split('|');
                    if (pieces.Length >= 4) { if (double.TryParse(pieces[1].Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) job.Progress = Math.Clamp(value, 0, 100); job.Speed = pieces[2]; job.Eta = pieces[3]; }
                }
                else if (text.StartsWith("CF_TITLE|")) job.Title = text[9..];
                else if (text.StartsWith("CF_FILE|")) { var path = text[8..].Trim(); if (!files.Contains(path)) files.Add(path); job.OutputPath = path; }
                else if (text.StartsWith("CF_CHAPTERS|"))
                {
                    var payload = text[12..].Trim();
                    if (payload.StartsWith('['))
                    {
                        using var chapters = JsonDocument.Parse(payload);
                        foreach (var chapter in chapters.RootElement.EnumerateArray())
                            if (chapter.TryGetProperty("filepath", out var path) && path.GetString() is { Length: > 0 } file && !files.Contains(file)) files.Add(file);
                    }
                }
                else { if (job.Log.Count >= 250) job.Log.RemoveAt(0); job.Log.Add(text); }
            }
            Updated?.Invoke();
        }
        try
        {
            using var usage = tools.AcquireUsage();
            cancellation.Token.ThrowIfCancellationRequested(); Directory.CreateDirectory(store.Resolve(job.Options.OutputDirectory)); Updated?.Invoke(); await PersistAsync();
            var args = Arguments.Download(tools.Paths, store, job.Options, job.Url);
            var result = await process.RunAsync(tools.Paths.YtDlp, args, Log, Log, Started, cancellation.Token); ToolProcess.RequireSuccess(result);
            if (job.Options.Effects || job.Options.Mode == DownloadMode.Video)
            {
                if (files.Count == 0) throw new IOException("音声処理する出力ファイルを取得できませんでした。既存ファイルまたは追加引数を確認してください。");
                cancellation.Token.ThrowIfCancellationRequested(); job.State = JobState.Processing; Updated?.Invoke();
                var audio = new AudioPipeline(tools.Paths, process);
                for (int i = 0; i < files.Count; i++) { cancellation.Token.ThrowIfCancellationRequested(); files[i] = job.Options.Mode == DownloadMode.Video
                    ? await audio.VideoOnlyAsync(files[i], job.Options, Log, Started, cancellation.Token)
                    : await audio.ApplyAsync(files[i], job.Options, Log, Started, cancellation.Token); }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            lock (gate) { job.OutputFiles = files.ToList(); if (files.Count > 0) job.OutputPath = files[0]; }
            job.State = JobState.Complete; job.Progress = 100;
        }
        catch (OperationCanceledException) { job.State = lifetime.IsCancellationRequested ? JobState.Interrupted : JobState.Cancelled; }
        catch (Exception e) { job.State = cancellation.IsCancellationRequested ? JobState.Cancelled : JobState.Error; job.Error = e.Message; Log(e.Message); }
        finally { lock (gate) { running.Remove(job.Id); pausedStates.Remove(job.Id); } await PersistAsync(); Updated?.Invoke(); }
    }
    private Task PersistAsync()
    {
        // Snapshot under lock before serialization; worker output mutates logs concurrently.
        List<DownloadJob> snapshot;
        lock (gate)
        {
            snapshot = System.Text.Json.JsonSerializer.Deserialize<List<DownloadJob>>(System.Text.Json.JsonSerializer.Serialize(jobs, Json.Options), Json.Options)!;
            return store.SaveAsync("history.json", snapshot);
        }
    }
    public async ValueTask DisposeAsync()
    {
        lock (gate) foreach (var (id, run) in running) { try { if (pausedStates.ContainsKey(id) && run.Process is not null && !run.Process.HasExited) ProcessTree.Resume(run.Process.Id); } catch { } run.Token.Cancel(); }
        lifetime.Cancel(); pending.Writer.TryComplete(); await Task.WhenAll(workers); await PersistAsync(); lifetime.Dispose();
    }
}
