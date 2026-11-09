using System.Text.Json;

namespace GrabForge.Core;

public sealed class MediaService(ToolPaths tools, ToolProcess process, PortableStore store)
{
    public async Task<MediaInfo> AnalyzeAsync(string url, DownloadOptions options, CancellationToken ct = default)
    {
        tools.Ensure("yt-dlp", "deno");
        var args = Arguments.Common(tools, options);
        args.AddRange(["--dump-single-json", "--skip-download", "--playlist-end", "1", "--", url]);
        var result = await process.RunAsync(tools.YtDlp, args, cancellationToken: ct); ToolProcess.RequireSuccess(result);
        using var document = JsonDocument.Parse(result.Output); var info = document.RootElement;
        int? count = null;
        if (info.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            if (info.TryGetProperty("playlist_count", out var total) && total.TryGetInt32(out var n)) count = n;
            info = entries.EnumerateArray().FirstOrDefault(e => e.ValueKind == JsonValueKind.Object);
            if (info.ValueKind != JsonValueKind.Object) throw new IOException("プレイリストに取得可能なメディアがありません。");
        }
        var media = JsonSerializer.Deserialize<MediaInfo>(info.GetRawText()) ?? throw new IOException("メディア情報が空です。");
        media.RawJson = info.GetRawText(); media.PlaylistCount ??= count; return media;
    }
    public async Task<string> PreviewAsync(MediaInfo media, DownloadOptions options, CancellationToken ct = default)
    {
        tools.Ensure("yt-dlp");
        var video = media.Formats.FirstOrDefault(f => f.Id == options.VideoFormatId);
        var audio = media.Formats.FirstOrDefault(f => f.Id == options.AudioFormatId);
        var sourceAudio = audio?.AudioCodec?.Split('.')[0] switch { "aac" => "m4a", "opus" => "opus", "vorbis" => "ogg", "mp3" => "mp3", "flac" => "flac", _ => audio?.Extension ?? "m4a" };
        var ext = options.Mode switch { DownloadMode.Audio => options.ConvertAudio ? options.AudioFormat : sourceAudio,
            _ => options.VideoContainer };
        if (options.RemuxVideo != "none" && options.Mode != DownloadMode.Audio) ext = options.RemuxVideo;
        var doc = System.Text.Json.Nodes.JsonNode.Parse(media.RawJson)?.AsObject() ?? throw new IOException("解析を先に行ってください。");
        doc["ext"] = ext;
        // A minimal info JSON avoids a second network request and evaluates all yt-dlp template syntax.
        doc.Remove("formats"); doc.Remove("requested_formats"); doc.Remove("requested_downloads");
        doc["url"] = "https://example.invalid/preview"; doc["format_id"] = "preview";
        var temp = Path.Combine(store.Data, $"preview-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(temp, doc.ToJsonString(), ct);
            var args = new List<string> { "--ignore-config", "--encoding", "utf-8", "--simulate", "--load-info-json", temp, "--output", options.OutputTemplate, "--print", "filename" };
            if (options.RestrictFilenames) args.Add("--restrict-filenames");
            var result = await process.RunAsync(tools.YtDlp, args, cancellationToken: ct); ToolProcess.RequireSuccess(result);
            return result.Output.Trim();
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
