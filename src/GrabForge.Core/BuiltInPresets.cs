namespace GrabForge.Core;

public static class BuiltInPresets
{
    public static List<Preset> Create() => [
        new(1, Guid.NewGuid(), "最高品質の動画", new()),
        new(1, Guid.NewGuid(), "互換性重視 MP4", new() { VideoFormatId = "bestvideo[ext=mp4][vcodec^=avc1]/best[ext=mp4]", AudioFormatId = "bestaudio[ext=m4a]/bestaudio", VideoContainer = "mp4" }),
        new(1, Guid.NewGuid(), "音楽 MP3・音量をそろえる", new() { Mode = DownloadMode.Audio, ConvertAudio = true, AudioFormat = "mp3", BitrateKbps = 256, Normalize = true, TargetLufs = -16 }),
        new(1, Guid.NewGuid(), "アーカイブ MKV", new() { VideoContainer = "mkv", EmbedSubtitles = true, WriteSubtitles = true, SubtitleLanguages = "all", WriteThumbnail = true, WriteInfoJson = true })
    ];
}
