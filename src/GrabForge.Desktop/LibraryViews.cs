using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using GrabForge.Core;

namespace GrabForge.Desktop;

public partial class MainWindow
{
    private ListView queueList = null!;
    private TextBox logBox = null!, presetName = null!;
    private ListBox presets = null!;
    private TextBlock toolStatus = null!, updateStatus = null!;

    private void BuildQueue()
    {
        QueuePage.RowDefinitions.Add(new() { Height = GridLength.Auto });
        QueuePage.RowDefinitions.Add(new()); QueuePage.RowDefinitions.Add(new() { Height = new GridLength(140) });
        var controls = Row(
            AsyncButton("一時停止 / 再開", async _ => await vm.Queue.PauseAsync(SelectedJob()), "PauseResume"),
            AsyncButton("キャンセル", async _ => await vm.Queue.CancelAsync(SelectedJob()), "CancelJob"),
            AsyncButton("再試行", async _ => await vm.Queue.RetryAsync(SelectedJob()), "RetryJob"),
            Button("保存先", () => OpenPath(SelectedJob().OutputPath), "OpenJob"),
            AsyncButton("履歴を消去", async _ =>
            {
                if (MessageBox.Show("履歴を消去しますか？ファイルは保持します。", "GrabForge", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    await vm.Queue.ClearFinishedAsync();
            }, "ClearHistory"));
        QueuePage.Children.Add(controls);
        queueList = new ListView(); AutomationProperties.SetAutomationId(queueList, "Jobs");
        var view = new GridView();
        foreach (var (label, property, width) in new[] { ("タイトル", "Title", 290d), ("状態", "State", 100d), ("進捗 %", "Progress", 65d), ("速度", "Speed", 85d), ("残り", "Eta", 70d), ("エラー", "Error", 200d) })
            view.Columns.Add(new() { Header = label, DisplayMemberBinding = new Binding(property), Width = width });
        queueList.View = view; Grid.SetRow(queueList, 1); QueuePage.Children.Add(queueList);
        logBox = new TextBox { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap };
        AutomationProperties.SetAutomationId(logBox, "JobLog"); Grid.SetRow(logBox, 2); QueuePage.Children.Add(logBox);
        queueList.SelectionChanged += (_, _) => RefreshLog(); RefreshQueue();
    }

    private DownloadJob SelectedJob() => queueList.SelectedItem as DownloadJob ?? throw new InvalidOperationException("ジョブを選択してください。");
    private void RefreshQueue()
    {
        var snapshot = vm.Queue.Jobs; var selected = queueList.SelectedItem as DownloadJob;
        if (queueList.Items.Count != snapshot.Count || !queueList.Items.Cast<DownloadJob>().Select(j => j.Id).SequenceEqual(snapshot.Select(j => j.Id)))
        {
            queueList.ItemsSource = snapshot;
            if (selected is not null) queueList.SelectedItem = snapshot.FirstOrDefault(j => j.Id == selected.Id);
        }
        QueueSummary.Text = snapshot.Count == 0 ? "" : $"{snapshot.Count(j => j.State == JobState.Complete)} / {snapshot.Count} 完了";
        RefreshLog();
    }

    private void RefreshLog()
    {
        if (queueList.SelectedItem is not DownloadJob job) return;
        var text = string.Join(Environment.NewLine, vm.Queue.GetLog(job));
        if (logBox.Text != text) { logBox.Text = text; logBox.ScrollToEnd(); }
    }

    private void BuildPresets()
    {
        presets = new ListBox { DisplayMemberPath = "Name", ItemsSource = vm.Presets.ToArray(), BorderThickness = new Thickness(0) };
        AutomationProperties.SetAutomationId(presets, "Presets");
        var detail = new StackPanel(); detail.Children.Add(Text("名前", 12));
        presetName = new TextBox(); AutomationProperties.SetAutomationId(presetName, "PresetName"); detail.Children.Add(presetName);
        var presetSummary = Text("", 12); presetSummary.Margin = new Thickness(0, 8, 0, 16); detail.Children.Add(presetSummary);
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedItem is not Preset p) return;
            presetName.Text = p.Name;
            var mode = p.Options.Mode switch { DownloadMode.Audio => "音声のみ", DownloadMode.Video => "動画のみ", _ => "動画 ＋ 音声" };
            presetSummary.Text = $"{mode} · {(p.Options.Mode == DownloadMode.Audio ? p.Options.AudioFormat : p.Options.VideoContainer).ToUpperInvariant()}\nコンプ {(p.Options.Compressor ? "ON" : "OFF")} · 正規化 {(p.Options.Normalize ? p.Options.TargetLufs + " LUFS" : "OFF")}";
        };
        detail.Children.Add(Row(
            AsyncButton("新規保存", async _ => { await vm.SavePresetAsync(presetName.Text, false); RefreshPresets(); }, "SavePreset"),
            AsyncButton("上書き", async _ =>
            {
                if (MessageBox.Show("同名プリセットを上書きしますか？", "GrabForge", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                await vm.SavePresetAsync(presetName.Text, true); RefreshPresets();
            }, "ReplacePreset"),
            AsyncButton("適用", _ => { vm.ApplyPreset(SelectedPreset()); Navigation.SelectedIndex = 0; return Task.CompletedTask; }, "ApplyPreset"),
            AsyncButton("削除", async _ =>
            {
                var preset = SelectedPreset();
                if (MessageBox.Show($"「{preset.Name}」を削除しますか？", "GrabForge", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                await vm.DeletePresetAsync(preset); RefreshPresets();
            }, "DeletePreset")));
        detail.Children.Add(Row(
            AsyncButton("インポート", async _ =>
            {
                var dialog = new OpenFileDialog { Filter = "JSON|*.json" };
                if (dialog.ShowDialog(this) == true) { await vm.ImportAsync(dialog.FileName); RefreshPresets(); }
            }, "ImportPreset"),
            AsyncButton("エクスポート", async _ =>
            {
                var preset = SelectedPreset(); var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "preset.json" };
                if (dialog.ShowDialog(this) == true) await PortableStore.ExportAsync(dialog.FileName, preset);
            }, "ExportPreset")));
        PresetsPage.Children.Add(Columns(Card("保存済み", presets), Card("編集", detail), 1, 1.35));
    }

    private Preset SelectedPreset() => presets.SelectedItem as Preset ?? throw new InvalidOperationException("プリセットを選択してください。");
    private void RefreshPresets() => presets.ItemsSource = vm.Presets.ToArray();

    private void BuildTools()
    {
        var tools = new StackPanel();
        toolStatus = Text(InstalledTools(), 12); toolStatus.TextWrapping = TextWrapping.NoWrap;
        toolStatus.TextTrimming = TextTrimming.CharacterEllipsis; toolStatus.ToolTip = toolStatus.Text; tools.Children.Add(toolStatus);
        void RefreshTools() { toolStatus.Text = InstalledTools(); toolStatus.ToolTip = toolStatus.Text; }
        tools.Children.Add(Row(AsyncButton("まとめて準備 / 更新", async ct =>
        {
            foreach (var tool in ToolManager.Definitions) { await vm.Tools.InstallAsync(tool, vm.Report, ct); RefreshTools(); }
        }, "InstallAllTools"), Button("中止", () => vm.Operation?.Cancel(), "CancelTools")));
        foreach (var tool in ToolManager.Definitions)
            tools.Children.Add(AsyncButton(tool.Id + " を更新 / 修復", async ct => { await vm.Tools.InstallAsync(tool, vm.Report, ct); RefreshTools(); }, "Install-" + tool.Id));
        tools.Children.Add(AsyncButton("更新を確認", async ct =>
        {
            var lines = new List<string>();
            foreach (var tool in ToolManager.Definitions)
            {
                var release = await vm.Tools.LatestAsync(tool, ct); var installed = vm.Tools.Installed(tool.Id);
                var asset = release.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == tool.AssetName);
                var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
                var state = installed is null ? "未導入" : digest == "sha256:" + installed.Sha256 ? "最新" : "更新あり";
                lines.Add($"{tool.Id} · {state}");
            }
            toolStatus.Text = string.Join('\n', lines);
        }, "CheckTools"));

        var app = new StackPanel();
        updateStatus = Text("v" + AppUpdates.CurrentVersion); AutomationProperties.SetAutomationId(updateStatus, "UpdateStatus"); app.Children.Add(updateStatus);
        app.Children.Add(Text("更新元", 11));
        var repository = new TextBox { Text = vm.Settings.ReleaseRepository };
        AutomationProperties.SetAutomationId(repository, "ReleaseRepository"); app.Children.Add(repository);
        var startup = new CheckBox { Content = "起動時に更新を確認", IsChecked = vm.Settings.CheckUpdatesAtStartup };
        startup.Checked += (_, _) => vm.Settings.CheckUpdatesAtStartup = true; startup.Unchecked += (_, _) => vm.Settings.CheckUpdatesAtStartup = false; app.Children.Add(startup);
        app.Children.Add(Row(
            AsyncButton("更新を確認", async ct =>
            {
                vm.Settings.ReleaseRepository = repository.Text.Trim();
                availableRelease = await vm.Updates.CheckAsync(vm.Settings.ReleaseRepository, ct);
                updateStatus.Text = availableRelease is null ? "最新です · v" + AppUpdates.CurrentVersion : availableRelease.Tag + " を利用できます";
                await vm.SaveSettingsAsync();
            }, "CheckAppUpdate"),
            AsyncButton("更新して再起動", async ct =>
            {
                if (availableRelease is null) throw new InvalidOperationException("先に更新を確認してください。");
                if (vm.Queue.IsBusy) throw new InvalidOperationException("ジョブの終了後に更新してください。");
                if (MessageBox.Show($"{availableRelease.Tag}へ更新しますか？", "GrabForge", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                var request = await vm.Updates.PrepareAsync(availableRelease, vm.Report, ct);
                await vm.SaveSettingsAsync(); vm.Updates.LaunchUpdater(request); restarting = true; Close();
            }, "ApplyAppUpdate")));
        app.Children.Add(Button("リリースノート", () => Process.Start(new ProcessStartInfo(availableRelease?.PageUrl ?? $"https://github.com/{vm.Settings.ReleaseRepository}/releases") { UseShellExecute = true }), "ReleaseNotes"));
        app.Children.Add(Text("同時ダウンロード数", 12));
        var concurrency = new TextBox { Text = vm.Settings.ConcurrentDownloads.ToString(), MaxWidth = 100, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(concurrency, "Concurrency"); app.Children.Add(concurrency);
        app.Children.Add(AsyncButton("保存", async _ =>
        {
            if (!int.TryParse(concurrency.Text, out var value) || value is < 1 or > 8) throw new ArgumentException("同時数は1～8です。");
            vm.Settings.ConcurrentDownloads = value; vm.Queue.Concurrency = value;
            vm.Settings.ReleaseRepository = repository.Text.Trim(); await vm.SaveSettingsAsync(); Status.Text = "保存しました";
        }, "SaveSettings"));
        ToolsPage.Children.Add(Columns(Card("ツール", tools), Card("アプリ", app)));
    }

    private string InstalledTools() => string.Join("\n\n", ToolManager.Definitions.Select(tool =>
    {
        var installed = vm.Tools.Installed(tool.Id);
        var version = installed?.ExecutableVersion ?? installed?.Version ?? "未導入";
        if (version.StartsWith("ffmpeg version ")) version = version[15..].Split(' ')[0];
        return $"{tool.Id} · {version}";
    }));
}
