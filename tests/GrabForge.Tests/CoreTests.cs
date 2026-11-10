using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrabForge.Core;
using Xunit;

namespace GrabForge.Tests;

public sealed class CoreTests : IDisposable
{
    [Theory]
    [InlineData("\"chapters\":null,\"subtitles\":null,\"automatic_captions\":null")]
    [InlineData("\"chapters\":[],\"subtitles\":{},\"automatic_captions\":{}")]
    [InlineData("\"duration\":609")]
    public void MissingOptionalMetadataLeavesStreamsSelectable(string metadata)
    {
        var media = JsonSerializer.Deserialize<MediaInfo>("{" + metadata + """
            ,"formats":[{"format_id":"137","ext":"mp4","vcodec":"avc1","acodec":"none"},
                        {"format_id":"140","ext":"m4a","vcodec":"none","acodec":"mp4a"}]}
            """)!;
        Assert.Empty(media.Chapters); Assert.Empty(media.Subtitles); Assert.Empty(media.AutoSubtitles);
        Assert.Single(media.Formats.Where(f => f.HasVideo)); Assert.Single(media.Formats.Where(f => f.HasAudio));
    }
    [Fact] public void ExplicitNullFormatsBecomesAnEmptyList()
    {
        Assert.Empty(JsonSerializer.Deserialize<MediaInfo>("{\"formats\":null}")!.Formats);
    }
    private readonly string root = Path.Combine(Path.GetTempPath(), "GrabForge-tests-" + Guid.NewGuid().ToString("N"));
    private ToolPaths Paths => new(Path.Combine(root, "yt-dlp.exe"), Path.Combine(root, "ffmpeg.exe"), Path.Combine(root, "ffprobe.exe"), Path.Combine(root, "deno.exe"));
    [Fact] public void DefaultsAndCloneAreIndependent()
    {
        var original = new DownloadOptions(); original.Validate(); var copy = original.Clone(); copy.TargetLufs = -20;
        Assert.Equal(-16, original.TargetLufs); Assert.Equal(-20, copy.TargetLufs);
    }
    [Fact] public void UnknownCodecFormatsRemainSelectableWithoutInventingQuality()
    {
        var audio = new MediaFormat { Extension = "wav" }; Assert.True(audio.HasAudio); Assert.False(audio.HasVideo); Assert.Equal("—", audio.Quality);
        var container = new MediaFormat { Extension = "mp4" }; Assert.True(container.HasVideo); Assert.True(container.HasAudio);
        var videoOnly = new MediaFormat { Extension = "mp4", VideoCodec = "h264", AudioCodec = "none" }; Assert.True(videoOnly.HasVideo); Assert.False(videoOnly.HasAudio);
    }
    [Theory]
    [InlineData(double.NaN, 2)] [InlineData(-61, 2)] [InlineData(-12, 21)]
    public void InvalidCompressorSettingsFail(double threshold, double ratio)
    { Assert.Throws<ArgumentException>(() => new DownloadOptions { ThresholdDb = threshold, Ratio = ratio }.Validate()); }
    [Fact] public void InvalidAudioRateFails()
    { Assert.Throws<ArgumentException>(() => new DownloadOptions { Mode = DownloadMode.Audio, ConvertAudio = true, AudioFormat = "opus", SampleRate = "44100" }.Validate()); }
    [Theory]
    [InlineData(DownloadMode.Audio, "251")] [InlineData(DownloadMode.Video, "401")] [InlineData(DownloadMode.VideoAudio, "401+251/401/best")]
    public void ModesSelectIndependentStreams(DownloadMode mode, string expected)
    {
        var a = Arguments.Download(Paths, new(root), new() { Mode = mode, VideoFormatId = "401", AudioFormatId = "251" }, "https://example.com/watch");
        Assert.Equal(expected, a[a.IndexOf("--format") + 1]); Assert.Contains("--no-simulate", a); Assert.Equal("https://example.com/watch", a[^1]);
    }
    [Fact] public void EveryAdvancedFeatureMapsToArguments()
    {
        var o = new DownloadOptions { WriteSubtitles = true, WriteAutoSubtitles = true, EmbedSubtitles = true, SubtitleFormat = "srt", SplitChapters = true,
            WriteThumbnail = true, WriteInfoJson = true, BrowserCookies = "firefox", Proxy = "http://localhost:8080", RateLimit = "5M", PlaylistItems = "1-3", DownloadArchive = "archive.txt", RestrictFilenames = true,
            NoOverwrites = true, KeepOriginal = true, SponsorBlock = "remove", RemuxVideo = "mkv", ExtraArguments = "--sleep-requests 1 --no-mtime" };
        var a = Arguments.Download(Paths, new(root), o, "https://example.com");
        foreach (var flag in new[] { "--write-subs", "--write-auto-subs", "--embed-subs", "--convert-subs", "--embed-chapters", "--split-chapters", "--embed-metadata", "--embed-thumbnail", "--write-thumbnail", "--write-info-json", "--cookies-from-browser", "--proxy", "--limit-rate", "--playlist-items", "--download-archive", "--restrict-filenames", "--no-overwrites", "--keep-video", "--sponsorblock-remove", "--remux-video", "--sleep-requests", "--no-mtime" }) Assert.Contains(flag, a);
    }
    [Fact] public void AudioConversionIsOptionalAndPrecedesMetadataProcessing()
    {
        var o = new DownloadOptions { Mode = DownloadMode.Audio };
        var source = Arguments.Download(Paths, new(root), o, "https://example.com");
        Assert.Contains("--extract-audio", source); Assert.Equal("best", source[source.IndexOf("--audio-format") + 1]);
        o.ConvertAudio = true; Assert.Contains("--extract-audio", Arguments.Download(Paths, new(root), o, "https://example.com"));
        o.Normalize = true; Assert.Contains("--extract-audio", Arguments.Download(Paths, new(root), o, "https://example.com"));
    }
    [Fact] public void EffectsHaveValidatedValuesAndCultureIndependentFilters()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            var o = new DownloadOptions { Ratio = 3.5, TargetLufs = -20, TruePeakDb = -2 };
            Assert.Contains(":ratio=3.5", AudioPipeline.CompressorFilter(o)); Assert.Equal("loudnorm=I=-20:TP=-2:LRA=50", AudioPipeline.NormalizationFilter(o));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
    [Fact] public void MeasurementAllowsTrailingFfmpegLogsAndRejectsSilence()
    {
        const string measurement = "{\"input_i\":\"-22.4\",\"input_tp\":\"-3\",\"input_lra\":\"2\",\"input_thresh\":\"-33\",\"target_offset\":\"0.2\"}";
        Assert.Equal(-22.4, AudioPipeline.ParseMeasurement("logging\n" + measurement + "\ntrailer")!.IntegratedLufs);
        Assert.Null(AudioPipeline.ParseMeasurement(measurement.Replace("-22.4", "-inf")));
    }
    [Fact] public void QuotedArgumentsDoNotInvokeShell()
    {
        Assert.Equal(new[] { "--output", "My Folder/%(title)s.%(ext)s", "--no-mtime" }, Arguments.Split("--output \"My Folder/%(title)s.%(ext)s\" --no-mtime"));
        Assert.Throws<ArgumentException>(() => Arguments.Split("--output \"unclosed"));
        Assert.Throws<ArgumentException>(() => Arguments.Split("-- https://another.example"));
        Assert.Contains("$(not-executed)", Arguments.Split("--output '$(not-executed)'"));
    }
    [Fact] public async Task PresetsRoundTripAllOptionsAndRejectFutureSchema()
    {
        var store = new PortableStore(root); var o = new DownloadOptions { Compressor = true, Normalize = true, Ratio = 4, TargetLufs = -14, SampleRate = "48000", PlaylistItems = "2-4", ExtraArguments = "--no-mtime" };
        var p = new Preset(1, Guid.NewGuid(), "音楽", o); await store.SaveAsync("presets.json", new[] { p });
        Assert.Equal(JsonSerializer.Serialize(o, Json.Options), JsonSerializer.Serialize(store.LoadPresets()[0].Options, Json.Options));
        var path = Path.Combine(root, "preset.json"); await PortableStore.ExportAsync(path, p); var imported = await PortableStore.ImportAsync(path); Assert.NotEqual(p.Id, imported.Id);
        Assert.Throws<InvalidDataException>(() => PortableStore.Validate(p with { SchemaVersion = 2 }));
    }
    [Fact] public void CorruptSettingsArePreserved()
    {
        var store = new PortableStore(root); var path = Path.Combine(store.Data, "settings.json"); File.WriteAllText(path, "{corrupt");
        Assert.Throws<InvalidDataException>(() => store.Load("settings.json", () => new AppSettings())); Assert.Equal("{corrupt", File.ReadAllText(path));
    }
    [Fact] public async Task DownloadsRequireCorrectHash()
    {
        Directory.CreateDirectory(root); var data = Encoding.UTF8.GetBytes("verified binary fixture");
        using var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }));
        var output = Path.Combine(root, "download"); var hash = Convert.ToHexString(SHA256.HashData(data));
        await ToolManager.DownloadVerifiedAsync(http, new("https://example.com/asset"), output, hash, null, default); Assert.Equal(data, File.ReadAllBytes(output));
        await Assert.ThrowsAsync<IOException>(() => ToolManager.DownloadVerifiedAsync(http, new("https://example.com/asset"), output, new string('0', 64), null, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => ToolManager.DownloadVerifiedAsync(http, new("http://example.com/asset"), output, hash, null, default));
    }
    [Fact] public async Task ReleaseCheckRejectsUnverifiableArtifacts()
    {
        var json = "{\"tag_name\":\"v9.9.9\",\"body\":\"notes\",\"html_url\":\"https://github.com/itan2929/GrabForge\",\"assets\":[{\"name\":\"GrabForge-v9.9.9-win-x64.zip\",\"browser_download_url\":\"https://example.com/asset\",\"digest\":null}]}";
        using var http = new HttpClient(new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(json) }));
        await Assert.ThrowsAsync<IOException>(() => new AppUpdates(new(root), http).CheckAsync("itan2929/GrabForge"));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request)); }
