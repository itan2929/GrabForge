using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using GrabForge.Core;

namespace GrabForge.Desktop;

public partial class MainWindow
{
    private TextBox urls = null!;
    private TextBlock summary = null!, preview = null!;
    private Image thumbnail = null!;
    private DockPanel mediaSummary = null!;
    private ListView videos = null!, audios = null!;
    private Grid formatPanels = null!;
    private Border videoPanel = null!, audioPanel = null!;
    private TabItem effectsTab = null!;
    private FrameworkElement videoOutput = null!, audioOutput = null!;

    private void BuildDownload()
    {
        OptionEditor.Reset(vm.Options);
        DownloadPage.Children.Clear(); DownloadPage.RowDefinitions.Clear();
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) })
            DownloadPage.RowDefinitions.Add(new() { Height = height });

        urls = new TextBox { AcceptsReturn = true, Height = 48, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "URL · 複数は改行", Margin = new Thickness(0, 0, 10, 8) };
        AutomationProperties.SetAutomationId(urls, "Urls"); AutomationProperties.SetName(urls, "URL");
        var urlBar = new DockPanel();
        var analyze = AsyncButton("解析", async ct =>
        {
            await vm.AnalyzeAsync(Urls().FirstOrDefault() ?? throw new ArgumentException("URLを入力してください。"), ct);
            RefreshFormats(); await RefreshPreviewAsync();
        }, "Analyze");
        var add = AsyncButton("キューに追加", async _ =>
        {
            await vm.Queue.AddAsync(Urls(), vm.Options, vm.Media?.Title);
            await vm.SaveSettingsAsync(); RefreshQueue(); Status.Text = "追加しました";
        }, "AddQueue");
        add.Style = (Style)FindResource("PrimaryButton");
        var actions = Row(analyze, add, Button("中止", () => vm.Operation?.Cancel(), "CancelOperation"));
        DockPanel.SetDock(actions, Dock.Right); urlBar.Children.Add(actions); urlBar.Children.Add(urls);
        DownloadPage.Children.Add(urlBar);

        thumbnail = new Image { Width = 64, Height = 42, Margin = new Thickness(0, 0, 10, 0), Visibility = Visibility.Collapsed };
        summary = Text("", 13); summary.TextWrapping = TextWrapping.NoWrap; summary.TextTrimming = TextTrimming.CharacterEllipsis;
        mediaSummary = new DockPanel { Margin = new Thickness(0, 2, 0, 8), Visibility = Visibility.Collapsed };
        DockPanel.SetDock(thumbnail, Dock.Left); mediaSummary.Children.Add(thumbnail); mediaSummary.Children.Add(summary);
        Grid.SetRow(mediaSummary, 1); DownloadPage.Children.Add(mediaSummary);

        var modes = new TabControl { Style = (Style)FindResource("HeaderTabs"), SelectedIndex = (int)vm.Options.Mode, Margin = new Thickness(0, 0, 0, 10) };
        AutomationProperties.SetAutomationId(modes, "DownloadMode");
        foreach (var (label, id) in new[] { ("動画 ＋ 音声", "ModeVideoAudio"), ("音声のみ", "ModeAudio"), ("動画のみ", "ModeVideo") })
            modes.Items.Add(Tab(label, new Grid(), id));
        modes.SelectionChanged += (_, e) =>
        {
            if (e.Source != modes) return;
            vm.Options.Mode = (DownloadMode)modes.SelectedIndex; UpdateModeVisibility(); _ = RefreshPreviewAsync();
        };
        Grid.SetRow(modes, 2); DownloadPage.Children.Add(modes);

        videos = FormatList(true); audios = FormatList(false);
        videoPanel = Card("映像", videos); audioPanel = Card("音声", audios);
        videoPanel.Margin = new Thickness(0, 0, 0, 10);
        formatPanels = new Grid(); formatPanels.RowDefinitions.Add(new()); formatPanels.RowDefinitions.Add(new());
        formatPanels.Children.Add(videoPanel); Grid.SetRow(audioPanel, 1); formatPanels.Children.Add(audioPanel);

        var settings = new TabControl(); AutomationProperties.SetAutomationId(settings, "DownloadSettings");
        settings.Items.Add(Tab("出力", OutputSettings(), "OutputTab"));
        effectsTab = Tab("音声処理", Columns(BuildEffect(true), BuildEffect(false)), "EffectsTab"); settings.Items.Add(effectsTab);
        var info = new StackPanel();
        info.Children.Add(Text("字幕・チャプター", 14));
        info.Children.Add(OptionEditor.Build(vm.Options, OptionEditor.Subtitles, columns: 2));
        info.Children.Add(Text("メタデータ・画像", 14));
        info.Children.Add(OptionEditor.Build(vm.Options, OptionEditor.Metadata, columns: 2));
        settings.Items.Add(Tab("付加情報", info, "MetadataTab"));
        var advanced = new TabControl();
        advanced.Items.Add(Tab("接続", OptionEditor.Build(vm.Options, OptionEditor.Network, columns: 2), "NetworkTab"));
        advanced.Items.Add(Tab("取得", OptionEditor.Build(vm.Options, OptionEditor.Advanced.Take(6), columns: 2), "AcquisitionTab"));
        advanced.Items.Add(Tab("その他", OptionEditor.Build(vm.Options, OptionEditor.Advanced.Skip(6), columns: 2), "OtherTab"));
        settings.Items.Add(Tab("詳細", advanced, "AdvancedTab"));
        var body = Columns(formatPanels, settings, 1, 1.06); Grid.SetRow(body, 3); DownloadPage.Children.Add(body);
        RefreshFormats(); UpdateModeVisibility();
    }

    private UIElement OutputSettings()
    {
        var panel = new StackPanel();
        var folder = new DockPanel();
        var browse = Button("参照", () =>
        {
            var picker = new OpenFolderDialog();
            if (picker.ShowDialog(this) == true) { vm.Options.OutputDirectory = picker.FolderName; RebuildDownload(); }
        }, "ChooseOutput");
        browse.VerticalAlignment = VerticalAlignment.Bottom; browse.Margin = new Thickness(8, 0, 0, 6);
        DockPanel.SetDock(browse, Dock.Right); folder.Children.Add(browse);
        folder.Children.Add(OptionEditor.Build(vm.Options, OptionEditor.Output.Take(1)));
        panel.Children.Add(folder);
        panel.Children.Add(OptionEditor.Build(vm.Options, OptionEditor.Output.Skip(1).Take(1), () => _ = RefreshPreviewAsync()));
        preview = Text("—", 11); preview.Foreground = Brushes.LightSteelBlue;
        preview.TextWrapping = TextWrapping.NoWrap; preview.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetAutomationId(preview, "FilenamePreview"); panel.Children.Add(preview);
        videoOutput = OptionEditor.Build(vm.Options, OptionEditor.Output.Skip(2).Take(1), () => _ = RefreshPreviewAsync(), columns: 2);
        panel.Children.Add(videoOutput);
        var audio = new StackPanel();
        var convert = new CheckBox { Content = "取得した音声を変換する", IsChecked = vm.Options.ConvertAudio };
        AutomationProperties.SetAutomationId(convert, "ConvertAudio");
        var options = OptionEditor.Build(vm.Options, OptionEditor.Output.Skip(4), () => _ = RefreshPreviewAsync(), columns: 2);
        options.IsEnabled = vm.Options.ConvertAudio;
        convert.Checked += (_, _) => { vm.Options.ConvertAudio = true; options.IsEnabled = true; _ = RefreshPreviewAsync(); };
        convert.Unchecked += (_, _) => { vm.Options.ConvertAudio = false; options.IsEnabled = false; _ = RefreshPreviewAsync(); };
        audio.Children.Add(convert); audio.Children.Add(options); audioOutput = audio; panel.Children.Add(audio);
        return panel;
    }

    private void RebuildDownload()
    {
        var text = urls.Text; BuildDownload(); urls.Text = text; _ = RefreshPreviewAsync();
    }

    private string[] Urls() => urls.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private ListView FormatList(bool video)
    {
        var list = new ListView { SelectionMode = SelectionMode.Single, Margin = new Thickness(0), BorderThickness = new Thickness(0) };
        AutomationProperties.SetAutomationId(list, video ? "VideoFormats" : "AudioFormats");
        var view = new GridView();
        var columns = new List<(string, string, double)> { ("ID", "Id", 54), ("品質", "Quality", 70), ("形式", "Extension", 50) };
        if (video) columns.Add(("FPS", "Fps", 30));
        columns.Add(("コーデック", video ? "VideoCodec" : "AudioCodec", 110));
        if (!video) columns.Add(("言語", "Language", 36));
        columns.Add(("サイズ", "SizeDisplay", 60));
        foreach (var (label, property, width) in columns)
            view.Columns.Add(new() { Header = label, DisplayMemberBinding = new Binding(property), Width = width });
        list.View = view;
        var codecColumn = view.Columns.First(c => (string)c.Header == "コーデック");
        list.SizeChanged += (_, _) => codecColumn.Width = Math.Max(60, list.ActualWidth - view.Columns.Where(c => c != codecColumn).Sum(c => c.Width) - 24);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not MediaFormat format) return;
            if (video) vm.Options.VideoFormatId = format.Id; else vm.Options.AudioFormatId = format.Id;
            _ = RefreshPreviewAsync();
        };
        return list;
    }

    private void RefreshFormats()
    {
        if (vm.Media is not { } media) return;
        mediaSummary.Visibility = Visibility.Visible;
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(media.Uploader)) details.Add(media.Uploader);
        if (media.Duration is double duration) details.Add(TimeSpan.FromSeconds(Math.Round(duration)).ToString("g"));
        if (media.Subtitles.Count > 0) details.Add($"字幕 {media.Subtitles.Count}");
        if (media.Chapters.Count > 0) details.Add($"チャプター {media.Chapters.Count}");
        summary.Text = media.Title + (details.Count == 0 ? "" : "\n" + string.Join(" · ", details));
        summary.ToolTip = summary.Text;
        if (Uri.TryCreate(media.Thumbnail, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            try
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = uri; bitmap.DecodePixelWidth = 128; bitmap.EndInit();
                thumbnail.Source = bitmap; thumbnail.Visibility = Visibility.Visible;
            }
            catch { thumbnail.Visibility = Visibility.Collapsed; }
        videos.ItemsSource = media.Formats.Where(f => f.HasVideo).Reverse().ToArray();
        audios.ItemsSource = media.Formats.Where(f => f.HasAudio).Reverse().ToArray();
        videos.SelectedItem = media.Formats.FirstOrDefault(f => f.Id == vm.Options.VideoFormatId);
        audios.SelectedItem = media.Formats.FirstOrDefault(f => f.Id == vm.Options.AudioFormatId);
    }

    private void UpdateModeVisibility()
    {
        var mode = vm.Options.Mode;
        videoPanel.Visibility = mode == DownloadMode.Audio ? Visibility.Collapsed : Visibility.Visible;
        audioPanel.Visibility = mode == DownloadMode.Video ? Visibility.Collapsed : Visibility.Visible;
        formatPanels.RowDefinitions[0].Height = mode == DownloadMode.Audio ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        formatPanels.RowDefinitions[1].Height = mode == DownloadMode.Video ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        videoPanel.Margin = mode == DownloadMode.VideoAudio ? new Thickness(0, 0, 0, 10) : new Thickness(0);
        videoOutput.Visibility = mode == DownloadMode.Audio ? Visibility.Collapsed : Visibility.Visible;
        audioOutput.Visibility = mode == DownloadMode.Audio ? Visibility.Visible : Visibility.Collapsed;
        if (mode == DownloadMode.Video && effectsTab.IsSelected) ((TabControl)effectsTab.Parent).SelectedIndex = 0;
        effectsTab.Visibility = mode == DownloadMode.Video ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task RefreshPreviewAsync()
    {
        previewToken?.Cancel(); previewToken?.Dispose(); previewToken = new(); var ct = previewToken.Token;
        try
        {
            await Task.Delay(250, ct);
            var name = vm.Media is null ? "—" : await vm.PreviewAsync(ct);
            if (!ct.IsCancellationRequested) { preview.Text = name; preview.ToolTip = name; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) { preview.Text = ex.Message; preview.ToolTip = ex.Message; } }
    }

    private UIElement BuildEffect(bool compressor)
    {
        var title = compressor ? "コンプレッサー" : "正規化";
        var check = new CheckBox { Content = title, IsChecked = compressor ? vm.Options.Compressor : vm.Options.Normalize };
        AutomationProperties.SetAutomationId(check, compressor ? "Compressor" : "Normalize");
        var tabs = new TabControl { IsEnabled = check.IsChecked == true, Padding = new Thickness(8) };
        AutomationProperties.SetAutomationId(tabs, compressor ? "CompressorTabs" : "NormalizeTabs");
        check.Checked += (_, _) => { if (compressor) vm.Options.Compressor = true; else vm.Options.Normalize = true; tabs.IsEnabled = true; };
        check.Unchecked += (_, _) => { if (compressor) vm.Options.Compressor = false; else vm.Options.Normalize = false; tabs.IsEnabled = false; };
        var simple = new StackPanel();
        var labels = compressor ? new[] { "弱め", "標準", "強め" } : new[] { "−20 LUFS", "−16 LUFS", "−14 LUFS" };
        var selected = Text("", 11); selected.Foreground = Brushes.LightSteelBlue;
        FrameworkElement Advanced()
        {
            var panel = new StackPanel();
            panel.Children.Add(OptionEditor.Build(vm.Options, compressor ? OptionEditor.Compressor : OptionEditor.Normalize, () => selected.Text = "カスタム", compressor ? 2 : 1));
            panel.Children.Add(Button("リセット", () => { ApplyEffectPreset(compressor, 1); ((TabItem)tabs.Items[1]).Content = Advanced(); selected.Text = ""; }, compressor ? "ResetCompressor" : "ResetNormalize"));
            return panel;
        }
        foreach (var (label, index) in labels.Select((label, index) => (label, index)))
            simple.Children.Add(Button(label, () =>
            {
                ApplyEffectPreset(compressor, index); ((TabItem)tabs.Items[1]).Content = Advanced(); selected.Text = label;
            }, (compressor ? "CompressorPreset" : "NormalizePreset") + index));
        simple.Children.Add(selected);
        tabs.Items.Add(Tab("かんたん", simple, compressor ? "CompressorSimple" : "NormalizeSimple"));
        tabs.Items.Add(Tab("詳細", Advanced(), compressor ? "CompressorAdvanced" : "NormalizeAdvanced"));
        var body = new DockPanel(); DockPanel.SetDock(check, Dock.Top); body.Children.Add(check); body.Children.Add(tabs);
        return body;
    }

    private void ApplyEffectPreset(bool compressor, int index)
    {
        var o = vm.Options;
        if (compressor)
        {
            o.ThresholdDb = new[] { -12d, -18.06, -24 }[index]; o.Ratio = new[] { 1.5, 2, 4 }[index];
            o.AttackMs = new[] { 30d, 20, 10 }[index]; o.ReleaseMs = new[] { 300d, 250, 200 }[index];
            o.MakeupDb = 0; o.KneeDb = index == 2 ? 6 : 9.03;
        }
        else { o.TargetLufs = new[] { -20d, -16, -14 }[index]; o.TruePeakDb = index == 0 ? -2 : -1.5; o.LoudnessRange = 50; }
    }
}
