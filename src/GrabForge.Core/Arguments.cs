using System.Text;

namespace GrabForge.Core;

public sealed record ToolPaths(string YtDlp, string FFmpeg, string FFprobe, string Deno)
{
    public void Ensure(params string[] names)
    {
        foreach (var name in names)
        {
            var path = name switch { "yt-dlp" => YtDlp, "ffmpeg" => FFmpeg, "ffprobe" => FFprobe, "deno" => Deno, _ => throw new ArgumentException(name) };
            if (!File.Exists(path)) throw new FileNotFoundException($"{name}が未導入です。「ツール管理」で準備してください。", path);
        }
    }
}

public static class Arguments
{
    public static List<string> Common(ToolPaths tools, DownloadOptions o)
    {
        var args = new List<string> { "--ignore-config", "--encoding", "utf-8", "--ffmpeg-location", Path.GetDirectoryName(tools.FFmpeg)!, "--js-runtimes", $"deno:{tools.Deno}" };
        Pair(args, "--cookies-from-browser", o.BrowserCookies == "none" ? "" : o.BrowserCookies);
        Pair(args, "--proxy", o.Proxy);
        return args;
    }
    public static List<string> Download(ToolPaths tools, PortableStore store, DownloadOptions o, string url)
    {
        o.Validate(); var a = Common(tools, o);
        a.AddRange(["--no-simulate", "--newline", "--progress", "--no-color", "--progress-template", "download:GF_PROGRESS|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s",
            "--print", "before_dl:GF_TITLE|%(title)s", "--print", "after_move:GF_FILE|%(filepath)s", "--print", "after_move:GF_CHAPTERS|%(chapters)j", "--paths", store.Resolve(o.OutputDirectory), "--output", o.OutputTemplate,
            "--retries", o.Retries.ToString(), "--fragment-retries", o.FragmentRetries.ToString(), "--concurrent-fragments", o.ConcurrentFragments.ToString()]);
        var format = o.Mode switch { DownloadMode.Audio => o.AudioFormatId, DownloadMode.Video => o.VideoFormatId,
            _ => $"{o.VideoFormatId}+{o.AudioFormatId}/{o.VideoFormatId}/best" };
        a.AddRange(["--format", format]);
        if (o.Mode == DownloadMode.VideoAudio) a.AddRange(["--merge-output-format", o.VideoContainer]);
        if (o.Mode == DownloadMode.VideoAudio) a.AddRange(["--remux-video", o.RemuxVideo == "none" ? o.VideoContainer : o.RemuxVideo]);
        // yt-dlp performs format conversion before its thumbnail/chapter processors.
        // Effects are applied to the resulting main file and each chapter afterwards.
        if (o.Mode == DownloadMode.Audio)
        {
            a.AddRange(["--extract-audio", "--audio-format", o.ConvertAudio ? o.AudioFormat : "best"]);
            var post = o.ConvertAudio ? Encoding(o, o.AudioFormat, includeCodec: false) : [];
            if (post.Count > 0) a.AddRange(["--postprocessor-args", "ExtractAudio+ffmpeg_o:" + string.Join(" ", post)]);
        }
        Flag(a, o.WriteSubtitles || o.EmbedSubtitles, "--write-subs"); Flag(a, o.WriteAutoSubtitles, "--write-auto-subs");
        Flag(a, o.EmbedSubtitles && o.Mode != DownloadMode.Audio, "--embed-subs");
        if (o.WriteSubtitles || o.WriteAutoSubtitles || o.EmbedSubtitles) a.AddRange(["--sub-langs", o.SubtitleLanguages]);
        if (o.SubtitleFormat != "none") a.AddRange(["--convert-subs", o.SubtitleFormat]);
        Flag(a, o.EmbedChapters, "--embed-chapters"); Flag(a, o.SplitChapters, "--split-chapters");
        Flag(a, o.EmbedMetadata, "--embed-metadata"); Flag(a, o.EmbedThumbnail, "--embed-thumbnail");
        Flag(a, o.WriteThumbnail, "--write-thumbnail"); Flag(a, o.WriteInfoJson, "--write-info-json");
        if (o.SponsorBlock != "off") a.AddRange([o.SponsorBlock == "mark" ? "--sponsorblock-mark" : "--sponsorblock-remove", o.SponsorCategories]);
        Pair(a, "--limit-rate", o.RateLimit); a.Add(o.Playlist ? "--yes-playlist" : "--no-playlist");
        Pair(a, "--playlist-items", o.PlaylistItems);
        if (!string.IsNullOrWhiteSpace(o.DownloadArchive)) a.AddRange(["--download-archive", store.Resolve(o.DownloadArchive)]);
        Flag(a, o.RestrictFilenames, "--restrict-filenames"); Flag(a, o.NoOverwrites, "--no-overwrites"); Flag(a, o.KeepOriginal, "--keep-video");
        a.AddRange(Split(o.ExtraArguments)); a.AddRange(["--", url]); return a;
    }
    public static List<string> Encoding(DownloadOptions o, string extension, bool includeCodec = true)
    {
        var codec = extension.ToLowerInvariant() switch { "mp3" => "libmp3lame", "opus" or "ogg" or "webm" => "libopus", "flac" => "flac", "wav" => "pcm_s16le", _ => "aac" };
        var a = new List<string>(); if (includeCodec) a.AddRange(["-c:a", codec]);
        if (o.SampleRate != "source") a.AddRange(["-ar", o.SampleRate]);
        if (codec is "flac" or "pcm_s16le") return a;
        if (o.BitrateMode == "vbr" && codec is "libmp3lame" or "aac")
        {
            // LAME quality and native AAC quality use different scales.
            var q = codec == "aac" ? Math.Clamp(o.BitrateKbps / 96d, .3, 3) : o.BitrateKbps switch { <= 64 => 8, <= 96 => 6, <= 128 => 4, <= 160 => 3, <= 192 => 2, <= 256 => 1, _ => 0 };
            a.AddRange(["-q:a", Json.Number(q)]);
        }
        else a.AddRange(["-b:a", $"{o.BitrateKbps}k"]);
        if (codec == "libopus") a.AddRange(["-vbr", o.BitrateMode == "cbr" ? "off" : "on"]);
        return a;
    }
    private static void Pair(List<string> args, string flag, string value) { if (!string.IsNullOrWhiteSpace(value)) args.AddRange([flag, value]); }
    private static void Flag(List<string> args, bool value, string flag) { if (value) args.Add(flag); }
    // Small shell-free tokenizer: quoted values are supported; nothing is executed by a shell.
    public static List<string> Split(string text)
    {
        var result = new List<string>(); var token = new StringBuilder(); char quote = '\0'; bool active = false;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == '"') { token.Append('"'); i++; active = true; }
            else if (c is '\'' or '"' && (quote == '\0' || quote == c)) { quote = quote == c ? '\0' : c; active = true; }
            else if (char.IsWhiteSpace(c) && quote == '\0') { if (active) { result.Add(token.ToString()); token.Clear(); active = false; } }
            else { token.Append(c); active = true; }
        }
        if (quote != '\0') throw new ArgumentException("追加引数の引用符が閉じていません。");
        if (active) result.Add(token.ToString());
        if (result.Contains("--")) throw new ArgumentException("追加引数にはURL区切り -- を指定できません。");
        return result;
    }
}
