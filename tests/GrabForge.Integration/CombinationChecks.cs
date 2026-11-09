using System.Text.Json;
using GrabForge.Core;

internal static class CombinationChecks
{
    public static async Task RunAsync(PortableStore store, ToolManager tools, string fixtures, string origin)
    {
        var process = new ToolProcess();
        async Task FF(params string[] arguments) => ToolProcess.RequireSuccess(await process.RunAsync(tools.Paths.FFmpeg, arguments));
        var cover = Path.Combine(fixtures, "cover.jpg");
        await FF("-v", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=160x160", "-frames:v", "1", "-update", "1", cover);
        await File.WriteAllTextAsync(Path.Combine(fixtures, "caption.vtt"), "WEBVTT\n\n00:00:00.000 --> 00:00:02.000\nGrabForge subtitle fixture\n");
        var source = Path.Combine(fixtures, "combined.mp4");
        await FF("-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=160x90:d=5", "-i", Path.Combine(fixtures, "quiet.wav"), "-c:v", "libx264", "-c:a", "aac", "-shortest", source);
        var info = new
        {
            id = "combination", title = "GrabForge combination", uploader = "Local fixture", duration = 5,
            extractor = "generic", extractor_key = "Generic", webpage_url = origin + "/combined.mp4",
            url = origin + "/combined.mp4", ext = "mp4", format_id = "local", vcodec = "h264", acodec = "aac",
            formats = new[] { new { format_id = "local", url = origin + "/combined.mp4", ext = "mp4", vcodec = "h264", acodec = "aac" } },
            chapters = new[] { new { start_time = 0d, end_time = 2.5, title = "First" }, new { start_time = 2.5, end_time = 5d, title = "Second" } },
            subtitles = new Dictionary<string, object> { ["ja"] = new[] { new { ext = "vtt", url = origin + "/caption.vtt" } } },
            thumbnails = new[] { new { id = "cover", url = origin + "/cover.jpg", ext = "jpg" } }
        };
        var infoPath = Path.Combine(fixtures, "combined.info.json"); await File.WriteAllTextAsync(infoPath, JsonSerializer.Serialize(info));
        foreach (var audioOnly in new[] { false, true })
        {
            var directory = Path.Combine(store.Root, "combo-" + Guid.NewGuid().ToString("N"));
            var options = new DownloadOptions { Mode = audioOnly ? DownloadMode.Audio : DownloadMode.VideoAudio,
                VideoFormatId = "local", AudioFormatId = audioOnly ? "local" : "missing", OutputDirectory = directory,
                ConvertAudio = audioOnly, AudioFormat = "mp3", Normalize = true, TargetLufs = -20,
                WriteSubtitles = true, EmbedSubtitles = !audioOnly, SubtitleLanguages = "ja", SubtitleFormat = "srt",
                EmbedThumbnail = true, WriteThumbnail = true, WriteInfoJson = true, EmbedChapters = true, SplitChapters = true,
                ExtraArguments = "--load-info-json \"" + infoPath + "\"" };
            await using var queue = new DownloadQueue(store, tools);
            await queue.AddAsync([origin + "/combined.mp4"], options);
            var job = queue.Jobs.First(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            while (queue.IsBusy) await Task.Delay(100, timeout.Token);
            if (job.State != JobState.Complete) throw new Exception("Combination failed: " + job.Error + "\n" + string.Join('\n', job.Log));
            var ext = audioOnly ? ".mp3" : ".mp4";
            var outputs = Directory.GetFiles(directory, "*" + ext, SearchOption.AllDirectories);
            if (outputs.Length != 3) throw new Exception($"Expected main file + 2 chapters, got {outputs.Length} in {directory}");
            foreach (var file in outputs)
            {
                var measure = await new AudioPipeline(tools.Paths, process).MeasureAsync(file, options, false);
                if (measure is null || Math.Abs(measure.IntegratedLufs + 20) > 1) throw new Exception("Unprocessed chapter or media: " + file);
            }
            var main = outputs.OrderBy(f => f.Length).First();
            var probe = await process.RunAsync(tools.Paths.FFprobe, ["-v", "error", "-show_streams", "-show_chapters", "-show_format", "-of", "json", main]); ToolProcess.RequireSuccess(probe);
            using var doc = JsonDocument.Parse(probe.Output); var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if (!streams.Any(s => s.TryGetProperty("disposition", out var disposition) && disposition.GetProperty("attached_pic").GetInt32() == 1)) throw new Exception("Embedded thumbnail was lost: " + main);
            if (!audioOnly && !streams.Any(s => s.GetProperty("codec_type").GetString() == "subtitle")) throw new Exception("Embedded subtitles were lost");
            if (doc.RootElement.GetProperty("chapters").GetArrayLength() != 2) throw new Exception("Embedded chapters were lost");
            if (!Directory.GetFiles(directory, "*.srt").Any() || !Directory.GetFiles(directory, "*.info.json").Any() || !Directory.GetFiles(directory, "*.jpg").Any()) throw new Exception("Sidecar files are missing");
            Console.WriteLine($"PASS: {(audioOnly ? "audio" : "video")} normalization + metadata + cover + subtitles + 2 chapter outputs");
        }
        // Opus stores artwork in a Vorbis comment, which FFmpeg exposes as an attached stream.
        var mp3 = Directory.GetFiles(store.Root, "*.mp3", SearchOption.AllDirectories).First(f => f.Contains("combo-"));
        var opusInput = Path.Combine(fixtures, "picture-audio.mp3"); File.Copy(mp3, opusInput, true);
        var opusOptions = new DownloadOptions { Mode = DownloadMode.Audio, ConvertAudio = true, AudioFormat = "opus", Normalize = true, TargetLufs = -20 };
        var opus = await new AudioPipeline(tools.Paths, process).ApplyAsync(opusInput, opusOptions, Console.WriteLine);
        var opusProbe = await process.RunAsync(tools.Paths.FFprobe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", opus]); ToolProcess.RequireSuccess(opusProbe);
        using var opusJson = JsonDocument.Parse(opusProbe.Output);
        if (!opusJson.RootElement.GetProperty("streams").EnumerateArray().Any(s => s.TryGetProperty("disposition", out var d) && d.GetProperty("attached_pic").GetInt32() == 1)) throw new Exception("Opus picture metadata lost");
        Console.WriteLine("PASS: Opus artwork survives normalization and conversion");
        var videoInput = Path.Combine(fixtures, "video-only.mp4"); File.Copy(source, videoInput, true);
        await new AudioPipeline(tools.Paths, process).VideoOnlyAsync(videoInput, new() { Mode = DownloadMode.Video }, Console.WriteLine);
        var videoProbe = await process.RunAsync(tools.Paths.FFprobe, ["-v", "error", "-show_entries", "stream=codec_type", "-of", "json", videoInput]);
        if (videoProbe.Output.Contains("audio")) throw new Exception("Video-only output still has audio");
        Console.WriteLine("PASS: video-only output strips combined source audio");
        foreach (var i in Enumerable.Range(1, 3)) File.Copy(Path.Combine(fixtures, "quiet.wav"), Path.Combine(fixtures, $"playlist-{i}.wav"), true);
        await File.WriteAllTextAsync(Path.Combine(fixtures, "playlist.html"), "<!doctype html><title>GrabForge playlist</title>" + string.Join("", Enumerable.Range(1, 3).Select(i => $"<video controls src=\"{origin}/playlist-{i}.wav\"></video>")));
        var playlistOutput = Path.Combine(store.Root, "playlist-" + Guid.NewGuid().ToString("N"));
        await using var playlistQueue = new DownloadQueue(store, tools);
        var playlistMedia = await new MediaService(tools.Paths, process, store).AnalyzeAsync(origin + "/playlist.html", new());
        if (playlistMedia.PlaylistCount != 3) throw new Exception("Playlist count was not extracted");
        await playlistQueue.AddAsync([origin + "/playlist.html"], new() { Mode = DownloadMode.Audio, AudioFormatId = "best", OutputDirectory = playlistOutput, OutputTemplate = "track-%(playlist_index)02d.%(ext)s", PlaylistItems = "2", EmbedThumbnail = false, EmbedChapters = false });
        using var playlistTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (playlistQueue.IsBusy) await Task.Delay(100, playlistTimeout.Token);
        var playlistJob = playlistQueue.Jobs.First();
        if (playlistJob.State != JobState.Complete || !File.Exists(Path.Combine(playlistOutput, "track-02.wav")) || Directory.GetFiles(playlistOutput, "*.wav").Length != 1) throw new Exception("Playlist range failed: " + playlistJob.Error);
        Console.WriteLine("PASS: playlist range selects only item 2 and evaluates playlist filename fields");
    }
}
