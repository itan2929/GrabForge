using System.Diagnostics;
using System.Text.Json;

namespace GrabForge.Core;

public sealed record AudioMeasurement(double IntegratedLufs, double TruePeakDb, double RangeLu, double Threshold, double Offset);
public sealed class AudioPipeline(ToolPaths tools, ToolProcess process)
{
    public async Task<string> VideoOnlyAsync(string path, DownloadOptions options, Action<string> log, Action<Process>? started = null, CancellationToken ct = default)
    {
        var ext = options.RemuxVideo == "none" ? options.VideoContainer : options.RemuxVideo;
        var destination = Path.ChangeExtension(path, ext);
        var temp = Path.Combine(Path.GetDirectoryName(path)!, ".video-" + Guid.NewGuid().ToString("N") + "." + ext);
        try
        {
            log("動画のみ: 音声を除去し、映像をコピーします。");
            ToolProcess.RequireSuccess(await process.RunAsync(tools.FFmpeg, ["-hide_banner", "-nostdin", "-y", "-i", path, "-map", "0:v", "-map", "0:s?", "-map_metadata", "0", "-map_chapters", "0", "-an", "-c", "copy", temp], started: started, cancellationToken: ct));
            ct.ThrowIfCancellationRequested();
            if (options.NoOverwrites && !path.Equals(destination, StringComparison.OrdinalIgnoreCase) && File.Exists(destination)) throw new IOException("変換先が既に存在します。");
            if (options.KeepOriginal) File.Copy(path, Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".original" + Path.GetExtension(path)));
            File.Move(temp, destination, true); if (!path.Equals(destination, StringComparison.OrdinalIgnoreCase)) File.Delete(path); return destination;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string CompressorFilter(DownloadOptions o) => $"acompressor=threshold={Json.Number(Math.Pow(10, o.ThresholdDb / 20))}:ratio={Json.Number(o.Ratio)}:attack={Json.Number(o.AttackMs)}:release={Json.Number(o.ReleaseMs)}:makeup={Json.Number(Math.Pow(10, o.MakeupDb / 20))}:knee={Json.Number(Math.Pow(10, o.KneeDb / 20))}";
    public static string NormalizationFilter(DownloadOptions o) => $"loudnorm=I={Json.Number(o.TargetLufs)}:TP={Json.Number(o.TruePeakDb)}:LRA={Json.Number(o.LoudnessRange)}";
    public static AudioMeasurement? ParseMeasurement(string stderr)
    {
        var start = stderr.LastIndexOf('{'); if (start < 0) throw new IOException("FFmpeg測定結果がありません。");
        var end = stderr.IndexOf('}', start); if (end < 0) throw new IOException("FFmpeg測定結果が不完全です。");
        using var doc = JsonDocument.Parse(stderr[start..(end + 1)]);
        double Read(string key)
        {
            var text = doc.RootElement.GetProperty(key).GetString()!;
            return text.ToLowerInvariant() switch { "-inf" => double.NegativeInfinity, "inf" or "+inf" => double.PositiveInfinity, "nan" => double.NaN,
                _ => double.Parse(text, System.Globalization.CultureInfo.InvariantCulture) };
        }
        var values = new[] { Read("input_i"), Read("input_tp"), Read("input_lra"), Read("input_thresh"), Read("target_offset") };
        if (values.Any(n => !double.IsFinite(n))) return null;
        return new(values[0], values[1], values[2], values[3], values[4]);
    }
    public async Task<AudioMeasurement?> MeasureAsync(string path, DownloadOptions o, bool compressor, Action<Process>? started = null, CancellationToken ct = default)
    {
        tools.Ensure("ffmpeg");
        var filters = compressor ? CompressorFilter(o) + "," : "";
        filters += NormalizationFilter(o) + ":print_format=json";
        var r = await process.RunAsync(tools.FFmpeg, ["-hide_banner", "-nostdin", "-nostats", "-i", path, "-map", "0:a:0", "-af", filters, "-f", "null", "-"], started: started, cancellationToken: ct);
        ToolProcess.RequireSuccess(r); return ParseMeasurement(r.Error);
    }
    public async Task<string> ApplyAsync(string path, DownloadOptions o, Action<string> log, Action<Process>? started = null, CancellationToken ct = default)
    {
        if (!o.Effects && !(o.Mode == DownloadMode.Audio && o.ConvertAudio)) return path;
        o.Validate(); tools.Ensure("ffmpeg", "ffprobe");
        if (!File.Exists(path)) throw new FileNotFoundException("後処理するファイルがありません。", path);
        var filters = new List<string>(); if (o.Compressor) filters.Add(CompressorFilter(o));
        if (o.Normalize)
        {
            log($"音量測定中（目標 {o.TargetLufs} LUFS）");
            var measure = await MeasureAsync(path, o, o.Compressor, started, ct);
            if (measure is not null)
            {
                log($"入力 {measure.IntegratedLufs:0.00} LUFS / {measure.TruePeakDb:0.00} dBTP、目標差 {o.TargetLufs - measure.IntegratedLufs:+0.00;-0.00;0} dB");
                filters.Add(NormalizationFilter(o) + $":linear=true:measured_I={Json.Number(measure.IntegratedLufs)}:measured_TP={Json.Number(measure.TruePeakDb)}:measured_LRA={Json.Number(measure.RangeLu)}:measured_thresh={Json.Number(measure.Threshold)}:offset={Json.Number(measure.Offset)}");
            }
            else log("無音のため正規化をスキップします。");
        }
        var ext = o.Mode == DownloadMode.Audio && o.ConvertAudio ? o.AudioFormat : Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var destination = Path.ChangeExtension(path, ext);
        if (!string.Equals(destination, path, StringComparison.OrdinalIgnoreCase) && File.Exists(destination) && o.NoOverwrites) throw new IOException("変換先が既に存在します。上書き禁止を解除するか保存名を変更してください。");
        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileNameWithoutExtension(path)}.processing-{Guid.NewGuid():N}.{ext}");
        var probe = await process.RunAsync(tools.FFprobe, ["-v", "error", "-show_streams", "-of", "json", path], cancellationToken: ct); ToolProcess.RequireSuccess(probe);
        using var metadata = JsonDocument.Parse(probe.Output);
        var audio = metadata.RootElement.GetProperty("streams").EnumerateArray().FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "audio");
        if (audio.ValueKind != JsonValueKind.Object) throw new IOException("音声ストリームがありません。");
        var encoding = o.Clone();
        if (encoding.SampleRate == "source") encoding.SampleRate = audio.TryGetProperty("sample_rate", out var rate) ? rate.GetString() ?? "48000" : "48000";
        if (ext is "opus" or "webm" or "ogg" && !new[] { "8000", "12000", "16000", "24000", "48000" }.Contains(encoding.SampleRate)) encoding.SampleRate = "48000";
        string? pictureMetadata = null;
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", path };
        var streams = metadata.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var artwork = streams.FirstOrDefault(s => s.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var p) && p.GetInt32() == 1);
        if (ext is "opus" or "ogg" && artwork.ValueKind == JsonValueKind.Object)
        {
            pictureMetadata = await PictureMetadataAsync(path, artwork, started, ct);
            args.AddRange(["-f", "ffmetadata", "-i", pictureMetadata]);
        }
        args.AddRange(["-map_metadata", "0"]);
        if (pictureMetadata is not null) args.AddRange(["-map_metadata", "1"]);
        args.AddRange(["-map_chapters", "0", "-c", "copy"]);
        if (o.Mode == DownloadMode.Audio)
        {
            args.AddRange(["-map", "0:a:0"]);
            // Keep attached artwork, but never include an actual video stream in audio-only output.
            foreach (var stream in metadata.RootElement.GetProperty("streams").EnumerateArray())
                if (stream.TryGetProperty("disposition", out var disposition) && disposition.TryGetProperty("attached_pic", out var pic) && pic.GetInt32() == 1 && ext is "mp3" or "m4a" or "flac")
                    args.AddRange(["-map", "0:" + stream.GetProperty("index").GetInt32()]);
        }
        else args.AddRange(["-map", "0"]);
        args.AddRange(Arguments.Encoding(encoding, ext));
        if (filters.Count > 0) args.AddRange(["-af", string.Join(',', filters)]);
        log("音声処理中（映像はストリームコピー）");
        try
        {
            var r = await process.RunAsync(tools.FFmpeg, args.Concat([temp]), error: line => { if (line.Contains("Error", StringComparison.OrdinalIgnoreCase)) log(line); }, started: started, cancellationToken: ct);
            ToolProcess.RequireSuccess(r);
            if (o.Normalize)
            {
                var measure = await MeasureAsync(temp, o, false, started, ct);
                if (measure is not null) log($"出力実測 {measure.IntegratedLufs:0.00} LUFS / {measure.TruePeakDb:0.00} dBTP");
            }
            ct.ThrowIfCancellationRequested();
            if (o.KeepOriginal)
            {
                var original = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".original" + Path.GetExtension(path));
                if (File.Exists(original)) throw new IOException("元ファイルの退避先が既にあります。");
                File.Copy(path, original);
            }
            File.Move(temp, destination, true);
            if (!string.Equals(destination, path, StringComparison.OrdinalIgnoreCase)) File.Delete(path);
            return destination;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); if (pictureMetadata is not null) File.Delete(pictureMetadata); }
    }
    private async Task<string> PictureMetadataAsync(string source, JsonElement stream, Action<Process>? started, CancellationToken ct)
    {
        var png = stream.GetProperty("codec_name").GetString() == "png";
        var image = Path.Combine(Path.GetDirectoryName(source)!, ".cover-" + Guid.NewGuid().ToString("N") + (png ? ".png" : ".jpg"));
        try
        {
            ToolProcess.RequireSuccess(await process.RunAsync(tools.FFmpeg, ["-v", "error", "-nostdin", "-y", "-i", source, "-map", "0:" + stream.GetProperty("index").GetInt32(), "-frames:v", "1", "-c:v", png ? "png" : "mjpeg", "-update", "1", image], started: started, cancellationToken: ct));
            var bytes = await File.ReadAllBytesAsync(image, ct); var mime = System.Text.Encoding.ASCII.GetBytes(png ? "image/png" : "image/jpeg");
            using var data = new MemoryStream();
            void Number(int value) { Span<byte> buffer = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer, value); data.Write(buffer); }
            Number(3); Number(mime.Length); data.Write(mime); Number(0);
            Number(stream.GetProperty("width").GetInt32()); Number(stream.GetProperty("height").GetInt32()); Number(24); Number(0); Number(bytes.Length); data.Write(bytes);
            var file = image + ".ffmetadata";
            await File.WriteAllTextAsync(file, ";FFMETADATA1\nMETADATA_BLOCK_PICTURE=" + Convert.ToBase64String(data.ToArray()) + "\n", ct); return file;
        }
        finally { if (File.Exists(image)) File.Delete(image); }
    }
}
