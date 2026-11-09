using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using GrabForge.Core;

namespace GrabForge.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private TextBox urls = null!, presetName = null!;
    private TextBlock summary = null!, preview = null!, toolStatus = null!, updateStatus = null!;
    private ListView videos = null!, audios = null!, queueList = null!;
    private ListBox presets = null!;
    private TextBox logBox = null!;
    private Image thumbnail = null!;
    private TabControl modes = null!;
    private CancellationTokenSource? previewToken;
    private readonly DispatcherTimer refresh;
    private bool closing;
    private bool restarting;
    private AppRelease? availableRelease;
    public MainWindow()
    {
        InitializeComponent(); Title = "GrabForge — yt-dlp / FFmpeg GUI v" + AppUpdates.CurrentVersion; vm = new(); DataContext = vm;
        vm.StatusChanged += message => Status.Text = message;
        vm.OptionsReplaced += () => { var text = urls.Text; BuildDownload(); urls.Text = text; _ = RefreshPreviewAsync(); };
        BuildDownload(); BuildQueue(); BuildPresets(); BuildTools();
        refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        refresh.Tick += (_, _) => RefreshQueue(); refresh.Start();
        Closing += OnClosing;
        Loaded += async (_, _) =>
        {
            UpdateModeVisibility();
            if (vm.Settings.CheckUpdatesAtStartup)
            { try { availableRelease = await vm.Updates.CheckAsync(vm.Settings.ReleaseRepository); if (availableRelease is not null) updateStatus.Text = $"新バージョン {availableRelease.Tag} があります。「ツール・更新」から更新できます。"; } catch (Exception ex) { updateStatus.Text = "更新確認: " + ex.Message; } }
        };
    }
    private static Button Button(string label, Action action, string id)
    {
        var button = new Button { Content = label }; AutomationProperties.SetAutomationId(button, id); button.Click += (_, _) => action(); return button;
    }
    private Button AsyncButton(string label, Func<CancellationToken, Task> work, string id) => Button(label, () => _ = vm.RunAsync(async ct => { ValidateEditors(); await work(ct); }), id);
    private static TextBlock Text(string value, double size = 13) => new() { Text = value, FontSize = size, Margin = new Thickness(0, 4, 0, 8) };
    private static WrapPanel Row(params UIElement[] children) { var panel = new WrapPanel(); foreach (var child in children) panel.Children.Add(child); return panel; }
    private static GroupBox Group(string title, UIElement child) => new() { Header = title, Content = child };
    private void BuildDownload()
    {
        DownloadPage.Children.Clear();
        urls = new TextBox { AcceptsReturn = true, MinHeight = 65, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ToolTip = "複数URLは改行で入力" };
        AutomationProperties.SetAutomationId(urls, "Urls"); AutomationProperties.SetName(urls, "動画・プレイリストURL");
        DownloadPage.Children.Add(Text("動画・プレイリストURL（複数は改行）", 16)); DownloadPage.Children.Add(urls);
        DownloadPage.Children.Add(Row(AsyncButton("URLを解析", async ct => { var url = Urls().FirstOrDefault() ?? throw new ArgumentException("URLを入力してください。"); await vm.AnalyzeAsync(url, ct); RefreshFormats(); await RefreshPreviewAsync(); }, "Analyze"),
            AsyncButton("キューに追加", async _ => { await vm.Queue.AddAsync(Urls(), vm.Options, vm.Media?.Title); await vm.SaveSettingsAsync(); RefreshQueue(); Status.Text = "キューに追加しました。"; }, "AddQueue"),
            Button("操作を中止", () => vm.Operation?.Cancel(), "CancelOperation")));
        summary = Text(vm.Media?.Title ?? "URLを解析し、映像と音声を個別に選択できます。", 18);
        thumbnail = new Image { Width = 150, Height = 84, Margin = new Thickness(0, 0, 16, 8), Visibility = Visibility.Collapsed };
        var mediaSummary = new DockPanel(); DockPanel.SetDock(thumbnail, Dock.Left); mediaSummary.Children.Add(thumbnail); mediaSummary.Children.Add(summary); DownloadPage.Children.Add(mediaSummary);
        modes = new TabControl { SelectedIndex = (int)vm.Options.Mode }; AutomationProperties.SetAutomationId(modes, "DownloadMode");
        foreach (var label in new[] { "動画 ＋ 音声", "音声のみ", "動画のみ" }) modes.Items.Add(new TabItem { Header = label });
        modes.SelectionChanged += (_, args) => { if (args.Source != modes) return; vm.Options.Mode = (DownloadMode)modes.SelectedIndex; UpdateModeVisibility(); _ = RefreshPreviewAsync(); };
        DownloadPage.Children.Add(modes);
        videos = FormatList(true); audios = FormatList(false); DownloadPage.Children.Add(videos); DownloadPage.Children.Add(audios);
        var output = new StackPanel();
        // Keep the template preview immediately below the template input.
        output.Children.Add(OptionEditor.Build(vm.Options, OptionEditor.Output.Take(2), () => _ = RefreshPreviewAsync()));
        preview = Text("URLを解析すると保存名を表示します。"); preview.Foreground = Brushes.LightSteelBlue; AutomationProperties.SetAutomationId(preview, "FilenamePreview"); output.Children.Add(preview);
        output.Children.Add(Row(Button("保存先を選ぶ", () => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) { vm.Options.OutputDirectory = picker.FolderName; RebuildDownload(); } }, "ChooseOutput")));
        var videoOutput = OptionEditor.Build(vm.Options, OptionEditor.Output.Skip(2).Take(1), () => _ = RefreshPreviewAsync()); videoOutput.Tag = "VideoOutput"; output.Children.Add(videoOutput);
        var audioOutput = new StackPanel { Tag = "AudioOutput" };
        var conversion = new CheckBox { Content = "取得した音声を変換する", IsChecked = vm.Options.ConvertAudio }; AutomationProperties.SetAutomationId(conversion, "ConvertAudio");
        var conversionSettings = OptionEditor.Build(vm.Options, OptionEditor.Output.Skip(4), () => _ = RefreshPreviewAsync());
        conversionSettings.IsEnabled = vm.Options.ConvertAudio;
        conversion.Checked += (_, _) => { vm.Options.ConvertAudio = true; conversionSettings.IsEnabled = true; _ = RefreshPreviewAsync(); };
        conversion.Unchecked += (_, _) => { vm.Options.ConvertAudio = false; conversionSettings.IsEnabled = false; _ = RefreshPreviewAsync(); };
        audioOutput.Children.Add(conversion); audioOutput.Children.Add(conversionSettings); output.Children.Add(audioOutput);
        DownloadPage.Children.Add(Group("出力設定", output));
        var effects = new StackPanel { Tag = "Effects" }; effects.Children.Add(BuildEffect(true)); effects.Children.Add(BuildEffect(false));
        effects.Children.Add(Text("併用時はコンプ→2パス正規化。音声は再エンコードされます。ピーク上限等により目標LUFSへ届かない場合があります。"));
        DownloadPage.Children.Add(effects);
        foreach (var (title, fields) in new[] { ("字幕・チャプター", OptionEditor.Subtitles), ("メタデータ・サムネイル", OptionEditor.Metadata), ("ネットワーク・認証", OptionEditor.Network), ("プレイリスト・後処理・追加引数", OptionEditor.Advanced) })
            DownloadPage.Children.Add(new Expander { Header = title, Content = OptionEditor.Build(vm.Options, fields) });
        RefreshFormats(); UpdateModeVisibility();
    }
    private void RebuildDownload() { var text = urls.Text; BuildDownload(); urls.Text = text; _ = RefreshPreviewAsync(); }
    private string[] Urls() => urls.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private ListView FormatList(bool video)
    {
        var list = new ListView { Height = vm.Media is null ? 55 : 190, SelectionMode = SelectionMode.Single }; AutomationProperties.SetAutomationId(list, video ? "VideoFormats" : "AudioFormats");
        var view = new GridView();
        foreach (var (label, property, width) in new[] { ("ID", "Id", 100d), ("品質", "Quality", 100d), ("形式", "Extension", 60d), ("FPS", "Fps", 55d), ("映像", "VideoCodec", 140d), ("音声", "AudioCodec", 120d), ("言語", "Language", 60d), ("サイズ", "SizeDisplay", 100d) })
            view.Columns.Add(new() { Header = label, DisplayMemberBinding = new Binding(property), Width = width });
        list.View = view;
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is MediaFormat format) { if (video) vm.Options.VideoFormatId = format.Id; else vm.Options.AudioFormatId = format.Id; _ = RefreshPreviewAsync(); } };
        return list;
    }
    private void RefreshFormats()
    {
        if (vm.Media is null) return;
        videos.Height = audios.Height = 190;
        summary.Text = $"{vm.Media.Title}\n{vm.Media.Uploader} • {TimeSpan.FromSeconds(vm.Media.Duration ?? 0):g} • 字幕: {string.Join(", ", vm.Media.Subtitles.Keys)} • チャプター: {vm.Media.Chapters.Count}";
        if (Uri.TryCreate(vm.Media.Thumbnail, UriKind.Absolute, out var imageUri) && imageUri.Scheme is "https" or "http")
            try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = imageUri; bitmap.DecodePixelWidth = 150; bitmap.EndInit(); thumbnail.Source = bitmap; thumbnail.Visibility = Visibility.Visible; } catch { thumbnail.Visibility = Visibility.Collapsed; }
        videos.ItemsSource = vm.Media.Formats.Where(f => f.HasVideo).Reverse().ToArray(); audios.ItemsSource = vm.Media.Formats.Where(f => f.HasAudio).Reverse().ToArray();
        videos.SelectedItem = vm.Media.Formats.FirstOrDefault(f => f.Id == vm.Options.VideoFormatId); audios.SelectedItem = vm.Media.Formats.FirstOrDefault(f => f.Id == vm.Options.AudioFormatId);
    }
    private void UpdateModeVisibility()
    {
        if (videos is null || audios is null) return;
        videos.Visibility = vm.Options.Mode == DownloadMode.Audio ? Visibility.Collapsed : Visibility.Visible;
        audios.Visibility = vm.Options.Mode == DownloadMode.Video ? Visibility.Collapsed : Visibility.Visible;
        foreach (var child in Descendants(DownloadPage).OfType<FrameworkElement>())
            if (child.Tag is string tag && tag is "Effects" or "AudioOutput" or "VideoOutput") child.Visibility = tag switch
            { "Effects" => vm.Options.Mode == DownloadMode.Video ? Visibility.Collapsed : Visibility.Visible,
              "AudioOutput" => vm.Options.Mode == DownloadMode.Audio ? Visibility.Visible : Visibility.Collapsed,
              _ => vm.Options.Mode == DownloadMode.Audio ? Visibility.Collapsed : Visibility.Visible };
    }
    private async Task RefreshPreviewAsync()
    {
        previewToken?.Cancel(); previewToken = new(); var token = previewToken.Token;
        try { await Task.Delay(250, token); var name = await vm.PreviewAsync(token); if (!token.IsCancellationRequested) preview.Text = "保存名: " + name; }
        catch (OperationCanceledException) { } catch (Exception ex) { if (!token.IsCancellationRequested) preview.Text = ex.Message; }
    }
    private GroupBox BuildEffect(bool compressor)
    {
        var title = compressor ? "コンプレッサー" : "ラウドネス正規化"; var panel = new StackPanel();
        var check = new CheckBox { Content = title + "を適用", IsChecked = compressor ? vm.Options.Compressor : vm.Options.Normalize };
        AutomationProperties.SetAutomationId(check, compressor ? "Compressor" : "Normalize"); panel.Children.Add(check);
        var tabs = new TabControl { IsEnabled = check.IsChecked == true }; AutomationProperties.SetAutomationId(tabs, compressor ? "CompressorTabs" : "NormalizeTabs");
        check.Checked += (_, _) => { if (compressor) vm.Options.Compressor = true; else vm.Options.Normalize = true; tabs.IsEnabled = true; };
        check.Unchecked += (_, _) => { if (compressor) vm.Options.Compressor = false; else vm.Options.Normalize = false; tabs.IsEnabled = false; };
        var simple = new StackPanel();
        simple.Children.Add(Text(compressor ? "弱め: 抑揚を残す ／ 標準: 穏やかな圧縮 ／ 強め: 大きな音をしっかり抑える" : "正規化は指定LUFSにそろえる処理です。元の音量が同じなら変化しません。"));
        var choices = new WrapPanel(); simple.Children.Add(choices);
        var custom = Text("詳細タブで微調整できます。プリセット選択後も全数値を編集できます。"); simple.Children.Add(custom);
        FrameworkElement? advancedEditor = null;
        void ResetAdvanced()
        {
            if (advancedEditor is not null) advancedEditor.DataContext = vm.Options;
        }
        foreach (var (label, index) in (compressor ? new[] { "弱め", "標準", "強め" } : new[] { "控えめ（−20 LUFS）", "標準（−16 LUFS）", "大きめ（−14 LUFS）" }).Select((label, index) => (label, index)))
            choices.Children.Add(Button(label, () => { ApplyEffectPreset(compressor, index); custom.Text = $"適用中: {label}"; var item = (TabItem)tabs.Items[1]; item.Content = advancedEditor = Advanced(); ResetAdvanced(); }, (compressor ? "CompressorPreset" : "NormalizePreset") + index));
        FrameworkElement Advanced()
        {
            var p = new StackPanel(); p.Children.Add(OptionEditor.Build(vm.Options, compressor ? OptionEditor.Compressor : OptionEditor.Normalize, () => custom.Text = "詳細のカスタム設定を使用中"));
            p.Children.Add(Button("初期値に戻す", () => { ApplyEffectPreset(compressor, 1); ((TabItem)tabs.Items[1]).Content = Advanced(); custom.Text = "初期値に戻しました。"; }, title + "Reset")); return p;
        }
        tabs.Items.Add(new TabItem { Header = "かんたん", Content = simple }); tabs.Items.Add(new TabItem { Header = "詳細", Content = advancedEditor = Advanced() });
        panel.Children.Add(tabs); return Group(title, panel);
    }
    private void ApplyEffectPreset(bool compressor, int index)
    {
        var o = vm.Options;
        if (compressor) { o.ThresholdDb = new[] { -12d, -18.06, -24 }[index]; o.Ratio = new[] { 1.5, 2, 4 }[index]; o.AttackMs = new[] { 30d, 20, 10 }[index]; o.ReleaseMs = new[] { 300d, 250, 200 }[index]; o.MakeupDb = 0; o.KneeDb = index == 2 ? 6 : 9.03; }
        else { o.TargetLufs = new[] { -20d, -16, -14 }[index]; o.TruePeakDb = index == 0 ? -2 : -1.5; o.LoudnessRange = 50; }
    }
    private void BuildQueue()
    {
        QueuePage.RowDefinitions.Add(new() { Height = GridLength.Auto }); QueuePage.RowDefinitions.Add(new()); QueuePage.RowDefinitions.Add(new() { Height = new GridLength(180) });
        var controls = Row(AsyncButton("一時停止 / 再開", async _ => await vm.Queue.PauseAsync(SelectedJob()), "PauseResume"), AsyncButton("キャンセル", async _ => await vm.Queue.CancelAsync(SelectedJob()), "CancelJob"), AsyncButton("再試行", async _ => await vm.Queue.RetryAsync(SelectedJob()), "RetryJob"), Button("ファイルの場所を開く", () => OpenPath(SelectedJob().OutputPath), "OpenJob"), AsyncButton("完了履歴を消去", async _ => { if (MessageBox.Show("完了・失敗・中断の履歴を消去しますか？ファイルは削除しません。", "履歴消去", MessageBoxButton.YesNo) == MessageBoxResult.Yes) await vm.Queue.ClearFinishedAsync(); }, "ClearHistory")); QueuePage.Children.Add(controls);
        queueList = new ListView(); AutomationProperties.SetAutomationId(queueList, "Jobs"); var view = new GridView();
        foreach (var (label, property, width) in new[] { ("タイトル", "Title", 340d), ("状態", "State", 110d), ("進捗 %", "Progress", 80d), ("速度", "Speed", 100d), ("残り時間", "Eta", 100d), ("エラー", "Error", 250d) }) view.Columns.Add(new() { Header = label, DisplayMemberBinding = new Binding(property), Width = width });
        queueList.View = view; Grid.SetRow(queueList, 1); QueuePage.Children.Add(queueList);
        logBox = new TextBox { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap }; AutomationProperties.SetAutomationId(logBox, "JobLog"); Grid.SetRow(logBox, 2); QueuePage.Children.Add(logBox);
        queueList.SelectionChanged += (_, _) => RefreshLog(); RefreshQueue();
    }
    private DownloadJob SelectedJob() => queueList.SelectedItem as DownloadJob ?? throw new InvalidOperationException("ジョブを選択してください。");
    private void RefreshQueue()
    {
        var snapshot = vm.Queue.Jobs; var selected = queueList.SelectedItem as DownloadJob;
        if (queueList.Items.Count != snapshot.Count || !queueList.Items.Cast<DownloadJob>().Select(j => j.Id).SequenceEqual(snapshot.Select(j => j.Id))) { queueList.ItemsSource = snapshot; if (selected is not null) queueList.SelectedItem = snapshot.FirstOrDefault(j => j.Id == selected.Id); }
        RefreshLog();
    }
    private void RefreshLog() { if (queueList.SelectedItem is DownloadJob job) { var text = string.Join(Environment.NewLine, vm.Queue.GetLog(job)); if (logBox.Text != text) { logBox.Text = text; logBox.ScrollToEnd(); } } }
    private void BuildPresets()
    {
        PresetsPage.Children.Clear(); PresetsPage.Children.Add(Text("出力・音声処理・字幕・ネットワーク等、すべての設定を保存します。", 16));
        PresetsPage.Children.Add(Text("プロキシ等の設定も保存されます。他の人へ渡す前に内容を確認してください。"));
        presetName = new TextBox(); AutomationProperties.SetAutomationId(presetName, "PresetName"); PresetsPage.Children.Add(Text("プリセット名")); PresetsPage.Children.Add(presetName);
        presets = new ListBox { Height = 240, DisplayMemberPath = "Name", ItemsSource = vm.Presets.ToArray() }; AutomationProperties.SetAutomationId(presets, "Presets"); PresetsPage.Children.Add(presets);
        presets.SelectionChanged += (_, _) => { if (presets.SelectedItem is Preset p) presetName.Text = p.Name; };
        PresetsPage.Children.Add(Row(AsyncButton("新規保存 / 複製", async _ => { await vm.SavePresetAsync(presetName.Text, false); RefreshPresets(); }, "SavePreset"),
            AsyncButton("上書き保存", async _ => { if (MessageBox.Show("同名プリセットを現在の設定で置き換えますか？", "プリセット", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { await vm.SavePresetAsync(presetName.Text, true); RefreshPresets(); } }, "ReplacePreset"),
            AsyncButton("適用して編集", _ => { vm.ApplyPreset(SelectedPreset()); Pages.SelectedIndex = 0; return Task.CompletedTask; }, "ApplyPreset"),
            AsyncButton("削除", async _ => { var p = SelectedPreset(); if (MessageBox.Show($"「{p.Name}」を削除しますか？", "削除", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { await vm.DeletePresetAsync(p); RefreshPresets(); } }, "DeletePreset")));
        PresetsPage.Children.Add(Row(AsyncButton("JSONをインポート", async _ => { var dialog = new OpenFileDialog { Filter = "プリセット JSON|*.json" }; if (dialog.ShowDialog(this) == true) { await vm.ImportAsync(dialog.FileName); RefreshPresets(); } }, "ImportPreset"),
            AsyncButton("JSONへエクスポート", async _ => { var p = SelectedPreset(); var dialog = new SaveFileDialog { Filter = "プリセット JSON|*.json", FileName = "preset.json" }; if (dialog.ShowDialog(this) == true) await PortableStore.ExportAsync(dialog.FileName, p); }, "ExportPreset")));
    }
    private Preset SelectedPreset() => presets.SelectedItem as Preset ?? throw new InvalidOperationException("プリセットを選択してください。");
    private void RefreshPresets() => presets.ItemsSource = vm.Presets.ToArray();
    private void BuildTools()
    {
        ToolsPage.Children.Add(Text("ツール管理", 22)); ToolsPage.Children.Add(Text("ツールはこのアプリの tools フォルダだけに保存します。PCのPATHや既存インストールを変更しません。配布元SHA-256照合と起動確認後に切り替えます。"));
        toolStatus = Text(InstalledTools()); ToolsPage.Children.Add(toolStatus);
        ToolsPage.Children.Add(AsyncButton("必要なツールをまとめて準備 / 更新", async ct => { foreach (var tool in ToolManager.Definitions) { await vm.Tools.InstallAsync(tool, vm.Report, ct); toolStatus.Text = InstalledTools(); } }, "InstallAllTools"));
        var row = new WrapPanel(); foreach (var tool in ToolManager.Definitions) row.Children.Add(AsyncButton(tool.Id + " を更新 / 修復", async ct => { await vm.Tools.InstallAsync(tool, vm.Report, ct); toolStatus.Text = InstalledTools(); }, "Install-" + tool.Id)); ToolsPage.Children.Add(row);
        ToolsPage.Children.Add(AsyncButton("ツールの更新を確認", async ct => { var lines = new List<string>(); foreach (var tool in ToolManager.Definitions) { var release = await vm.Tools.LatestAsync(tool, ct); var installed = vm.Tools.Installed(tool.Id); var asset = release.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == tool.AssetName); var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null; var state = installed is null ? "未導入" : digest == "sha256:" + installed.Sha256 ? "最新" : "更新あり"; lines.Add($"{tool.Id}: {state} / {installed?.ExecutableVersion ?? installed?.Version ?? "未導入"}"); } toolStatus.Text = string.Join("\n", lines); }, "CheckTools"));
        ToolsPage.Children.Add(Text("アプリ更新", 22)); updateStatus = Text("現在のバージョン: v" + AppUpdates.CurrentVersion); ToolsPage.Children.Add(updateStatus);
        var repository = new TextBox { Text = vm.Settings.ReleaseRepository }; AutomationProperties.SetAutomationId(repository, "ReleaseRepository"); ToolsPage.Children.Add(Text("GitHub Releases（owner/repository）")); ToolsPage.Children.Add(repository);
        var startup = new CheckBox { Content = "起動時にアプリ更新を確認", IsChecked = vm.Settings.CheckUpdatesAtStartup }; startup.Checked += (_, _) => vm.Settings.CheckUpdatesAtStartup = true; startup.Unchecked += (_, _) => vm.Settings.CheckUpdatesAtStartup = false; ToolsPage.Children.Add(startup);
        ToolsPage.Children.Add(Row(AsyncButton("アプリ更新を確認", async ct => { vm.Settings.ReleaseRepository = repository.Text.Trim(); availableRelease = await vm.Updates.CheckAsync(vm.Settings.ReleaseRepository, ct); updateStatus.Text = availableRelease is null ? "新しいリリースはありません。" : $"{availableRelease.Tag}\n{availableRelease.Notes}"; await vm.SaveSettingsAsync(); }, "CheckAppUpdate"),
            AsyncButton("ダウンロードして再起動・更新", async ct =>
            {
                if (availableRelease is null) throw new InvalidOperationException("先に更新を確認してください。"); if (vm.Queue.IsBusy) throw new InvalidOperationException("ジョブが終了してから更新してください。");
                if (MessageBox.Show($"{availableRelease.Tag}へ更新して再起動しますか？", "アプリ更新", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                var request = await vm.Updates.PrepareAsync(availableRelease, vm.Report, ct); await vm.SaveSettingsAsync(); vm.Updates.LaunchUpdater(request); restarting = true; Close();
            }, "ApplyAppUpdate")));
        ToolsPage.Children.Add(Text("同時ダウンロード数（1～8）", 16)); var concurrency = new TextBox { Text = vm.Settings.ConcurrentDownloads.ToString(), MaxWidth = 140, HorizontalAlignment = HorizontalAlignment.Left }; AutomationProperties.SetAutomationId(concurrency, "Concurrency"); ToolsPage.Children.Add(concurrency);
        ToolsPage.Children.Add(AsyncButton("設定を保存", async _ => { if (!int.TryParse(concurrency.Text, out var value) || value is < 1 or > 8) throw new ArgumentException("同時数は1～8です。"); vm.Settings.ConcurrentDownloads = value; vm.Queue.Concurrency = value; vm.Settings.ReleaseRepository = repository.Text.Trim(); await vm.SaveSettingsAsync(); Status.Text = "設定を保存しました。"; }, "SaveSettings"));
        ToolsPage.Children.Add(Text($"GrabForge v{AppUpdates.CurrentVersion} / MIT License\n外部ツールのライセンスと配布元は tools 内の SOURCE.txt を参照してください。\nポータブル設定・プリセット・履歴は data 内。更新時も保持されます。"));
    }
    private string InstalledTools() => string.Join("\n", ToolManager.Definitions.Select(tool => { var installed = vm.Tools.Installed(tool.Id); return $"{tool.Id}: {installed?.ExecutableVersion ?? installed?.Version ?? "未導入"}"; }));
    private void OpenOutput(object sender, RoutedEventArgs e) => OpenPath(vm.Store.Resolve(vm.Options.OutputDirectory));
    private void OpenPath(string path)
    {
        try { var target = File.Exists(path) ? Path.GetDirectoryName(path)! : path; if (!Directory.Exists(target)) throw new DirectoryNotFoundException("保存先はまだ作成されていません。"); Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { Status.Text = ex.Message; }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private void ValidateEditors() { if (Descendants(this).Any(Validation.GetHasError)) throw new ArgumentException("赤枠の入力値を修正してください。"); }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return; e.Cancel = true;
        if (vm.Queue.IsBusy && MessageBox.Show("実行中・待機中のジョブがあります。中断して終了しますか？", "GrabForge", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (vm.Busy && !restarting && MessageBox.Show("実行中の操作を中止して終了しますか？", "GrabForge", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        closing = true; refresh.Stop(); previewToken?.Cancel();
        try { await vm.DisposeAsync(); } catch (Exception ex) { MessageBox.Show(ex.Message, "終了時の保存エラー"); } Close();
    }
}
