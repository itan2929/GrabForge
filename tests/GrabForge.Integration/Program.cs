using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using GrabForge.Core;

if (args.Length < 2 || args[0] is not ("install" or "verify" or "analyze" or "update")) { Console.Error.WriteLine("Usage: install|verify|analyze|update <isolated-qa-root> [URL or package]"); return 2; }
var root = Path.GetFullPath(args[1]); var store = new PortableStore(root);
using var http = ToolManager.CreateHttpClient(); var manager = new ToolManager(store, http); var process = new ToolProcess();
if (args[0] == "install")
{
    foreach (var tool in ToolManager.Definitions) await manager.InstallAsync(tool, Console.WriteLine);
    Console.WriteLine("PASS: managed tools downloaded, SHA-256 verified and version probes succeeded."); return 0;
}
manager.Paths.Ensure("yt-dlp", "ffmpeg", "ffprobe", "deno");
if (args[0] == "analyze")
{
    var media = await new MediaService(manager.Paths, process, store).AnalyzeAsync(args[2], new());
    if (media.Formats.Count == 0) throw new Exception("No formats");
    Console.WriteLine($"PASS: supported-site analysis: {media.Title}, {media.Duration}s, {media.Formats.Count} formats"); return 0;
}
if (args[0] == "update")
{
    var archive = Path.GetFullPath(args[2]);
    await store.SaveAsync("settings.json", new AppSettings { CheckUpdatesAtStartup = false });
    await File.WriteAllTextAsync(Path.Combine(store.Data, "update-sentinel.txt"), "preserve data");
    await File.WriteAllTextAsync(Path.Combine(store.Tools, "update-sentinel.txt"), "preserve tools");
    var request = new UpdateRequest(args.Length > 3 ? int.Parse(args[3]) : -1, store.Root, archive, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive))), AppUpdates.CurrentVersion);
    var requestPath = Path.Combine(store.Data, "qa-update.json"); await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, Json.Options));
    var helper = new AppUpdates(store, http).LaunchUpdater(requestPath); Console.WriteLine("Updater started from " + helper); return 0;
}
var output = Path.Combine(root, "fixtures"); Directory.CreateDirectory(output);
async Task<ProcessResult> FF(params string[] arguments) { var r = await process.RunAsync(manager.Paths.FFmpeg, arguments); ToolProcess.RequireSuccess(r); return r; }
void Require(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
var wav = Path.Combine(output, "quiet.wav");
await FF("-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=5", "-af", "volume=0.1", "-ar", "44100", wav);
var pipeline = new AudioPipeline(manager.Paths, process);
var before = await pipeline.MeasureAsync(wav, new(), false);
Require(before is not null && before.IntegratedLufs < -30, "quiet fixture is below -30 LUFS");
foreach (var ext in new[] { "mp3", "m4a", "opus", "flac", "wav" })
{
    var input = Path.Combine(output, "convert-" + ext + ".wav"); File.Copy(wav, input, true);
    var options = new DownloadOptions { Mode = DownloadMode.Audio, ConvertAudio = true, AudioFormat = ext, Compressor = true, Normalize = true, TargetLufs = -14, SampleRate = ext == "opus" ? "48000" : "44100", BitrateMode = "cbr", BitrateKbps = 160 };
    var result = await pipeline.ApplyAsync(input, options, Console.WriteLine);
    var measured = await pipeline.MeasureAsync(result, options, false);
    Require(measured is not null && Math.Abs(measured.IntegratedLufs + 14) < .8, ext + " reaches -14 LUFS");
    var probe = await process.RunAsync(manager.Paths.FFprobe, ["-v", "error", "-show_streams", "-of", "json", result]); ToolProcess.RequireSuccess(probe);
    using var info = JsonDocument.Parse(probe.Output); var audio = info.RootElement.GetProperty("streams").EnumerateArray().First(s => s.GetProperty("codec_type").GetString() == "audio");
    Require(audio.GetProperty("sample_rate").GetString() == options.SampleRate, ext + " sampling frequency preserved");
}
var video = Path.Combine(output, "video.mp4");
await FF("-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=160x90:d=5", "-i", wav, "-c:v", "libx264", "-c:a", "aac", "-shortest", video);
async Task<string> VideoHash(string path) => (await FF("-v", "error", "-i", path, "-map", "0:v:0", "-c", "copy", "-f", "hash", "-hash", "sha256", "-")).Output.Trim();
var originalHash = await VideoHash(video);
await pipeline.ApplyAsync(video, new() { Compressor = true, Normalize = true, TargetLufs = -14 }, Console.WriteLine);
Require(await VideoHash(video) == originalHash, "video stream hash unchanged after audio effects");

// Actual yt-dlp acquisition from a local HTTP fixture. No third-party content required.
var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
using var stop = new CancellationTokenSource();
var serving = Task.Run(async () =>
{
    while (!stop.IsCancellationRequested)
    {
        HttpListenerContext context;
        try { context = await listener.GetContextAsync().WaitAsync(stop.Token); } catch (OperationCanceledException) { break; } catch (HttpListenerException) { break; }
        _ = Task.Run(async () =>
        {
            try
            {
                var requested = Path.Combine(output, Path.GetFileName(context.Request.Url!.AbsolutePath));
                if (!File.Exists(requested)) { context.Response.StatusCode = 404; return; }
                context.Response.ContentType = Path.GetExtension(requested) switch { ".html" => "text/html", ".jpg" => "image/jpeg", ".mp4" => "video/mp4", ".vtt" => "text/vtt", _ => "audio/wav" };
                var data = await File.ReadAllBytesAsync(requested); context.Response.ContentLength64 = data.LongLength;
                if (context.Request.HttpMethod != "HEAD") await context.Response.OutputStream.WriteAsync(data);
            }
            finally { context.Response.Close(); }
        });
    }
});
try
{
    var url = $"http://127.0.0.1:{port}/quiet.wav"; var options = new DownloadOptions { Mode = DownloadMode.Audio, OutputDirectory = Path.Combine(root, "downloads"), ConvertAudio = true, AudioFormat = "mp3", Normalize = true, TargetLufs = -14, EmbedThumbnail = false };
    using var usage = manager.AcquireUsage();
    var media = await new MediaService(manager.Paths, process, store).AnalyzeAsync(url, options);
    Require(media.Formats.Count > 0, "actual yt-dlp JSON analysis");
    var preview = await new MediaService(manager.Paths, process, store).PreviewAsync(media, options);
    Require(preview.EndsWith(".mp3"), "yt-dlp filename preview uses final extension");
    await using var queue = new DownloadQueue(store, manager);
    await queue.AddAsync([url], options, media.Title);
    var timeout = Stopwatch.StartNew();
    while (queue.IsBusy && timeout.Elapsed < TimeSpan.FromSeconds(90)) await Task.Delay(100);
    var job = queue.Jobs.First();
    Require(job.State == JobState.Complete, "queue executes download and postprocessing: " + job.Error);
    Require(File.Exists(job.OutputPath) && Path.GetExtension(job.OutputPath) == ".mp3", "queue final path is converted MP3");
    Require(job.Log.Any(l => l.Contains("出力実測")), "queue logs measured output loudness");
    await CombinationChecks.RunAsync(store, manager, output, $"http://127.0.0.1:{port}");
    await QueueChecks.RunAsync(store, manager, output, $"http://127.0.0.1:{port}");
}
finally { stop.Cancel(); listener.Stop(); await serving; }
Console.WriteLine("All integration checks passed."); return 0;
