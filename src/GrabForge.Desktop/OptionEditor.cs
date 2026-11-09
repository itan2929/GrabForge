using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using GrabForge.Core;

namespace GrabForge.Desktop;

internal sealed record OptionField(string Property, string Label, string? Choices = null, string? Hint = null);

internal static class OptionEditor
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DownloadOptions, Dictionary<string, FrameworkElement>> editors = new();
    public static void Reset(DownloadOptions options) => editors.Remove(options);
    public static bool HasErrors(DownloadOptions options) => editors.TryGetValue(options, out var fields) && fields.Values.Any(Validation.GetHasError);
    public static FrameworkElement Build(DownloadOptions options, IEnumerable<OptionField> fields, Action? changed = null, int columns = 1)
    {
        var grid = new Grid();
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new());
        int index = 0;
        foreach (var field in fields)
        {
            var property = typeof(DownloadOptions).GetProperty(field.Property)!;
            if (index % columns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var cell = new StackPanel { Margin = new Thickness(0, 0, index % columns < columns - 1 ? 10 : 0, 6) };
            FrameworkElement editor;
            var binding = new Binding(property.Name) { Source = options, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnExceptions = true, NotifyOnValidationError = true };
            if (property.PropertyType == typeof(bool))
            {
                var check = new CheckBox { Content = field.Label, VerticalAlignment = VerticalAlignment.Center, MinHeight = 28 }; check.SetBinding(CheckBox.IsCheckedProperty, binding);
                check.Checked += (_, _) => changed?.Invoke(); check.Unchecked += (_, _) => changed?.Invoke(); editor = check;
            }
            else if (field.Choices is not null)
            {
                var combo = new ComboBox { ItemsSource = field.Choices.Split('|'), HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
                combo.SetBinding(ComboBox.SelectedItemProperty, binding); combo.SelectionChanged += (_, _) => changed?.Invoke(); editor = combo;
            }
            else
            {
                var text = new TextBox(); text.SetBinding(TextBox.TextProperty, binding);
                text.TextChanged += (_, _) => changed?.Invoke(); editor = text;
            }
            editor.Margin = new Thickness(0, 2, 0, 0);
            if (property.PropertyType != typeof(bool)) cell.Children.Add(new TextBlock { Text = field.Label, FontSize = 11, Foreground = System.Windows.Media.Brushes.LightSteelBlue, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
            cell.Children.Add(editor);
            AutomationProperties.SetAutomationId(editor, property.Name); AutomationProperties.SetName(editor, field.Label);
            editors.GetOrCreateValue(options)[property.Name] = editor;
            if (property.Name is nameof(DownloadOptions.BitrateMode) or nameof(DownloadOptions.BitrateKbps))
            {
                void UpdateAvailability() => editor.IsEnabled = options.AudioFormat is not ("flac" or "wav");
                grid.Loaded += (_, _) => UpdateAvailability();
                grid.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => UpdateAvailability()));
            }
            if (field.Hint is not null) editor.ToolTip = field.Hint;
            Grid.SetColumn(cell, index % columns); Grid.SetRow(cell, index++ / columns); grid.Children.Add(cell);
        }
        return grid;
    }

    public static readonly OptionField[] Output = [
        new(nameof(DownloadOptions.OutputDirectory), "保存先"), new(nameof(DownloadOptions.OutputTemplate), "ファイル名テンプレート"),
        new(nameof(DownloadOptions.VideoContainer), "動画コンテナ", "mp4|mkv|webm"),
        new(nameof(DownloadOptions.ConvertAudio), "取得した音声を変換する"),
        new(nameof(DownloadOptions.AudioFormat), "音声保存形式", "mp3|m4a|opus|flac|wav"),
        new(nameof(DownloadOptions.SampleRate), "サンプリング周波数 · Hz", "source|8000|12000|16000|24000|32000|44100|48000|88200|96000|192000"),
        new(nameof(DownloadOptions.BitrateMode), "ビットレートモード", "vbr|cbr"), new(nameof(DownloadOptions.BitrateKbps), "ビットレート（kbps）")
    ];
    public static readonly OptionField[] Compressor = [
        new(nameof(DownloadOptions.ThresholdDb), "しきい値（dB）", Hint: "−60～0。これを超えた音を圧縮"),
        new(nameof(DownloadOptions.Ratio), "比率（:1）", Hint: "1～20"), new(nameof(DownloadOptions.AttackMs), "アタック（ms）", Hint: "0.01～2000"),
        new(nameof(DownloadOptions.ReleaseMs), "リリース（ms）", Hint: "0.01～9000"), new(nameof(DownloadOptions.MakeupDb), "ゲイン（dB）", Hint: "メイクアップゲイン。0～36"), new(nameof(DownloadOptions.KneeDb), "ニー（dB）", Hint: "0～18")
    ];
    public static readonly OptionField[] Normalize = [
        new(nameof(DownloadOptions.TargetLufs), "目標ラウドネス（LUFS）", Hint: "−70～−5。0に近いほど大きい"),
        new(nameof(DownloadOptions.TruePeakDb), "最大トゥルーピーク（dBTP）", Hint: "−9～0"), new(nameof(DownloadOptions.LoudnessRange), "ラウドネスレンジ（LU）", Hint: "1～50。50は元の強弱を維持")
    ];
    public static readonly OptionField[] Subtitles = [
        new(nameof(DownloadOptions.WriteSubtitles), "字幕を保存"), new(nameof(DownloadOptions.WriteAutoSubtitles), "自動字幕を保存"), new(nameof(DownloadOptions.EmbedSubtitles), "字幕を埋め込む"),
        new(nameof(DownloadOptions.SubtitleLanguages), "字幕言語"), new(nameof(DownloadOptions.SubtitleFormat), "字幕変換", "none|srt|vtt|ass"), new(nameof(DownloadOptions.EmbedChapters), "チャプターを埋め込む"), new(nameof(DownloadOptions.SplitChapters), "チャプターで分割")
    ];
    public static readonly OptionField[] Metadata = [new(nameof(DownloadOptions.EmbedMetadata), "メタデータを埋め込む"), new(nameof(DownloadOptions.EmbedThumbnail), "サムネイルを埋め込む"), new(nameof(DownloadOptions.WriteThumbnail), "サムネイルを保存"), new(nameof(DownloadOptions.WriteInfoJson), "info.jsonを保存")];
    public static readonly OptionField[] Network = [new(nameof(DownloadOptions.BrowserCookies), "ブラウザCookie", "none|chrome|edge|firefox|brave"), new(nameof(DownloadOptions.Proxy), "プロキシ"), new(nameof(DownloadOptions.RateLimit), "速度制限（例: 5M）"), new(nameof(DownloadOptions.Retries), "再試行回数"), new(nameof(DownloadOptions.FragmentRetries), "フラグメント再試行"), new(nameof(DownloadOptions.ConcurrentFragments), "並列フラグメント")];
    public static readonly OptionField[] Advanced = [new(nameof(DownloadOptions.Playlist), "プレイリストを取得"), new(nameof(DownloadOptions.PlaylistItems), "プレイリスト範囲（例: 1-5）"), new(nameof(DownloadOptions.DownloadArchive), "アーカイブファイル"), new(nameof(DownloadOptions.RemuxVideo), "リマックス", "none|mp4|mkv|webm|mov"), new(nameof(DownloadOptions.SponsorBlock), "SponsorBlock", "off|mark|remove"), new(nameof(DownloadOptions.SponsorCategories), "SponsorBlockカテゴリー"), new(nameof(DownloadOptions.RestrictFilenames), "ファイル名をASCIIに制限"), new(nameof(DownloadOptions.NoOverwrites), "上書き禁止"), new(nameof(DownloadOptions.KeepOriginal), "元ファイルも保持"), new(nameof(DownloadOptions.ExtraArguments), "追加のyt-dlp引数")];
}
