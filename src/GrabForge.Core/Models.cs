using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrabForge.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Changed(property); return true; }
}

public enum DownloadMode { VideoAudio, Audio, Video }
public enum JobState { Queued, Downloading, Processing, Paused, Complete, Error, Cancelled, Interrupted }

public sealed class DownloadOptions
{
    public DownloadMode Mode { get; set; }
    public string VideoFormatId { get; set; } = "bestvideo*";
    public string AudioFormatId { get; set; } = "bestaudio";
    public string OutputDirectory { get; set; } = "downloads";
    public string OutputTemplate { get; set; } = "%(title)s [%(id)s].%(ext)s";
    public string VideoContainer { get; set; } = "mp4";
    public bool ConvertAudio { get; set; }
    public string AudioFormat { get; set; } = "mp3";
    public string SampleRate { get; set; } = "source";
    public string BitrateMode { get; set; } = "vbr";
    public int BitrateKbps { get; set; } = 192;
    public bool Compressor { get; set; }
    public bool Normalize { get; set; }
    public double ThresholdDb { get; set; } = -18.06;
    public double Ratio { get; set; } = 2;
    public double AttackMs { get; set; } = 20;
    public double ReleaseMs { get; set; } = 250;
    public double MakeupDb { get; set; }
    public double KneeDb { get; set; } = 9.03;
    public double TargetLufs { get; set; } = -16;
    public double TruePeakDb { get; set; } = -1.5;
    public double LoudnessRange { get; set; } = 50;
    public bool WriteSubtitles { get; set; }
    public bool WriteAutoSubtitles { get; set; }
    public bool EmbedSubtitles { get; set; }
    public string SubtitleLanguages { get; set; } = "ja,en";
    public string SubtitleFormat { get; set; } = "none";
    public bool EmbedChapters { get; set; } = true;
    public bool SplitChapters { get; set; }
    public bool EmbedMetadata { get; set; } = true;
    public bool EmbedThumbnail { get; set; } = true;
    public bool WriteThumbnail { get; set; }
    public bool WriteInfoJson { get; set; }
    public string SponsorBlock { get; set; } = "off";
    public string SponsorCategories { get; set; } = "sponsor,selfpromo,interaction";
    public string BrowserCookies { get; set; } = "none";
    public string Proxy { get; set; } = "";
    public string RateLimit { get; set; } = "";
    public int Retries { get; set; } = 10;
    public int FragmentRetries { get; set; } = 10;
    public int ConcurrentFragments { get; set; } = 4;
    public bool Playlist { get; set; } = true;
    public string PlaylistItems { get; set; } = "";
    public string DownloadArchive { get; set; } = "";
    public bool RestrictFilenames { get; set; }
    public bool NoOverwrites { get; set; }
    public bool KeepOriginal { get; set; }
    public string RemuxVideo { get; set; } = "none";
    public string ExtraArguments { get; set; } = "";
    [JsonIgnore] public bool Effects => Mode != DownloadMode.Video && (Compressor || Normalize);
    public DownloadOptions Clone() => JsonSerializer.Deserialize<DownloadOptions>(JsonSerializer.Serialize(this, Json.Options), Json.Options)!;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(OutputTemplate) || OutputTemplate.Length > 2048) throw new ArgumentException("ファイル名テンプレートを入力してください（2048文字以内）。");
        if (string.IsNullOrWhiteSpace(OutputDirectory)) throw new ArgumentException("保存先を入力してください。");
        Choice(VideoContainer, "mp4", "mkv", "webm"); Choice(AudioFormat, "mp3", "m4a", "opus", "flac", "wav");
        Choice(SampleRate, "source", "8000", "12000", "16000", "24000", "32000", "44100", "48000", "88200", "96000", "192000");
        Choice(BitrateMode, "cbr", "vbr"); Range(BitrateKbps, 32, 320, "ビットレート");
        Range(ThresholdDb, -60, 0, "しきい値"); Range(Ratio, 1, 20, "比率"); Range(AttackMs, .01, 2000, "アタック");
        Range(ReleaseMs, .01, 9000, "リリース"); Range(MakeupDb, 0, 36, "ゲイン"); Range(KneeDb, 0, 18, "ニー");
        Range(TargetLufs, -70, -5, "LUFS"); Range(TruePeakDb, -9, 0, "トゥルーピーク"); Range(LoudnessRange, 1, 50, "LRA");
        Range(Retries, 0, 100, "再試行"); Range(FragmentRetries, 0, 100, "フラグメント再試行"); Range(ConcurrentFragments, 1, 32, "並列フラグメント");
        Choice(SponsorBlock, "off", "mark", "remove"); Choice(BrowserCookies, "none", "chrome", "edge", "firefox", "brave");
        Choice(RemuxVideo, "none", "mp4", "mkv", "webm", "mov"); Choice(SubtitleFormat, "none", "srt", "vtt", "ass");
        if (Mode == DownloadMode.Audio && ConvertAudio && SampleRate != "source")
        {
            var rates = AudioFormat switch { "mp3" => new[] { "8000", "12000", "16000", "24000", "32000", "44100", "48000" }, "opus" => ["8000", "12000", "16000", "24000", "48000"], _ => [] };
            if (rates.Length > 0 && !rates.Contains(SampleRate)) throw new ArgumentException($"{AudioFormat}では{SampleRate} Hzを使用できません。");
        }
    }
    private static void Choice(string value, params string[] allowed) { if (!allowed.Contains(value)) throw new ArgumentException($"未対応の設定値: {value}"); }
    private static void Range(double value, double min, double max, string label) { if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentException($"{label}: {min}～{max}で入力してください。"); }
}

public sealed class MediaFormat
{
    [JsonPropertyName("format_id")] public string Id { get; set; } = "";
    [JsonPropertyName("ext")] public string Extension { get; set; } = "";
    [JsonPropertyName("vcodec")] public string? VideoCodec { get; set; }
    [JsonPropertyName("acodec")] public string? AudioCodec { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("fps")] public double? Fps { get; set; }
    [JsonPropertyName("abr")] public double? AudioBitrate { get; set; }
    [JsonPropertyName("tbr")] public double? TotalBitrate { get; set; }
    [JsonPropertyName("filesize")] public long? Size { get; set; }
    [JsonPropertyName("filesize_approx")] public long? ApproximateSize { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    private bool AudioExtension => Extension is "mp3" or "m4a" or "aac" or "opus" or "ogg" or "flac" or "wav";
    private bool UnknownContainer => VideoCodec is null && AudioCodec is null && Extension is "mp4" or "webm" or "mkv" or "mov";
    [JsonIgnore] public bool HasVideo => VideoCodec is not null and not "none" || UnknownContainer;
    [JsonIgnore] public bool HasAudio => AudioCodec is not null and not "none" || AudioCodec is null && (AudioExtension || UnknownContainer);
    [JsonIgnore] public string Quality => Height is > 0 ? $"{Height}p" : (AudioBitrate ?? TotalBitrate) is double rate ? $"{rate:0} kbps" : "—";
    [JsonIgnore] public string SizeDisplay => (Size ?? ApproximateSize) is long n ? $"{n / 1048576d:0.0} MB" : "不明";
    public override string ToString() => string.Join(" · ", new[] { Id, Quality, Extension, VideoCodec, AudioCodec }.Where(value => value is not null and not "none" && value.Length > 0));
}

public sealed class MediaInfo
{
    private List<MediaFormat> formats = [];
    private Dictionary<string, JsonElement> subtitles = [], autoSubtitles = [];
    private List<JsonElement> chapters = [];
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("uploader")] public string? Uploader { get; set; }
    [JsonPropertyName("duration")] public double? Duration { get; set; }
    [JsonPropertyName("webpage_url")] public string? Url { get; set; }
    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }
    // Extractors may emit explicit nulls as well as omit unavailable metadata.
    [JsonPropertyName("formats")] public List<MediaFormat> Formats { get => formats; set => formats = value ?? []; }
    [JsonPropertyName("subtitles")] public Dictionary<string, JsonElement> Subtitles { get => subtitles; set => subtitles = value ?? []; }
    [JsonPropertyName("automatic_captions")] public Dictionary<string, JsonElement> AutoSubtitles { get => autoSubtitles; set => autoSubtitles = value ?? []; }
    [JsonPropertyName("chapters")] public List<JsonElement> Chapters { get => chapters; set => chapters = value ?? []; }
    [JsonPropertyName("playlist_count")] public int? PlaylistCount { get; set; }
    [JsonIgnore] public string RawJson { get; set; } = "";
}

public sealed class DownloadJob : Observable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = "";
    public DownloadOptions Options { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    private string title = ""; public string Title { get => title; set => Set(ref title, value); }
    private JobState state; public JobState State { get => state; set => Set(ref state, value); }
    private double progress; public double Progress { get => progress; set => Set(ref progress, value); }
    private string speed = ""; public string Speed { get => speed; set => Set(ref speed, value); }
    private string eta = ""; public string Eta { get => eta; set => Set(ref eta, value); }
    private string outputPath = ""; public string OutputPath { get => outputPath; set => Set(ref outputPath, value); }
    private string error = ""; public string Error { get => error; set => Set(ref error, value); }
    public List<string> Log { get; set; } = [];
    public List<string> OutputFiles { get; set; } = [];
}

public sealed record Preset(int SchemaVersion, Guid Id, string Name, DownloadOptions Options)
{
    public override string ToString() => Name;
}
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public int ConcurrentDownloads { get; set; } = 2;
    public bool CheckUpdatesAtStartup { get; set; } = true;
    public string ReleaseRepository { get; set; } = "itan2929/GrabForge";
    public DownloadOptions LastOptions { get; set; } = new();
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    public static string Number(double value) => value.ToString("G", CultureInfo.InvariantCulture);
}
