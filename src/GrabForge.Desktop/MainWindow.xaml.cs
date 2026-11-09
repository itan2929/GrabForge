using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GrabForge.Core;

namespace GrabForge.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private readonly DispatcherTimer refresh;
    private CancellationTokenSource? previewToken;
    private bool closing, restarting;
    private AppRelease? availableRelease;

    public MainWindow()
    {
        InitializeComponent();
        Title = "GrabForge · " + AppUpdates.CurrentVersion;
        VersionLabel.Text = "v" + AppUpdates.CurrentVersion;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 20);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 20);
        vm = new(); DataContext = vm;
        vm.StatusChanged += message => { Status.Text = message; Status.ToolTip = message; };
        vm.OptionsReplaced += RebuildDownload;
        BuildDownload(); BuildQueue(); BuildPresets(); BuildTools();
        Navigation.SelectedIndex = 0;
        refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        refresh.Tick += (_, _) => RefreshQueue(); refresh.Start();
        Closing += OnClosing;
        Loaded += async (_, _) =>
        {
            if (!vm.Settings.CheckUpdatesAtStartup) return;
            try
            {
                availableRelease = await vm.Updates.CheckAsync(vm.Settings.ReleaseRepository);
                if (availableRelease is not null) updateStatus.Text = availableRelease.Tag + " を利用できます";
            }
            catch (Exception ex) { updateStatus.Text = "更新確認に失敗"; updateStatus.ToolTip = ex.Message; }
        };
    }

    private void Navigate(object sender, SelectionChangedEventArgs e)
    {
        if (DownloadPage is null || ToolsPage is null || Navigation.SelectedIndex < 0) return;
        FrameworkElement[] pages = [DownloadPage, QueuePage, PresetsPage, ToolsPage];
        for (var i = 0; i < pages.Length; i++) pages[i].Visibility = Navigation.SelectedIndex == i ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = ((ListBoxItem)Navigation.SelectedItem).Content.ToString();
    }

    private static Button Button(string label, Action action, string id)
    {
        var button = new Button { Content = label };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => action(); return button;
    }

    private Button AsyncButton(string label, Func<CancellationToken, Task> work, string id) =>
        Button(label, () => _ = vm.RunAsync(async ct => { ValidateEditors(); await work(ct); }), id);

    private static TextBlock Text(string value, double size = 13) => new()
    { Text = value, FontSize = size, Margin = new Thickness(0, 0, 0, 8) };

    private static WrapPanel Row(params UIElement[] children)
    { var panel = new WrapPanel(); foreach (var child in children) panel.Children.Add(child); return panel; }

    private static Border Card(string title, UIElement content)
    {
        var panel = new DockPanel();
        var heading = Text(title, 14); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading); panel.Children.Add(content);
        return new Border { Child = panel, CornerRadius = new CornerRadius(9), Padding = new Thickness(14),
            Background = new SolidColorBrush(Color.FromRgb(23, 33, 50)), BorderBrush = new SolidColorBrush(Color.FromRgb(42, 56, 78)), BorderThickness = new Thickness(1) };
    }

    private static Grid Columns(UIElement left, UIElement right, double leftWeight = 1, double rightWeight = 1)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(leftWeight, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(rightWeight, GridUnitType.Star) });
        grid.Children.Add(left); Grid.SetColumn(right, 2); grid.Children.Add(right); return grid;
    }

    private static TabItem Tab(string title, UIElement content, string? id = null)
    {
        var item = new TabItem { Header = title, Content = content };
        if (id is not null) AutomationProperties.SetAutomationId(item, id);
        return item;
    }

    private void OpenOutput(object sender, RoutedEventArgs e) => OpenPath(vm.Store.Resolve(vm.Options.OutputDirectory));
    private void OpenPath(string path)
    {
        try
        {
            var target = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
            if (!Directory.Exists(target)) throw new DirectoryNotFoundException("保存先がありません。");
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }

    private void ValidateEditors()
    {
        if (Descendants(this).Any(Validation.GetHasError) || OptionEditor.HasErrors(vm.Options)) throw new ArgumentException("入力値を修正してください。");
        // Also validates values on settings tabs that are currently not mounted in the visual tree.
        vm.Options.Validate();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return; e.Cancel = true;
        if (vm.Queue.IsBusy && MessageBox.Show("ジョブを中断して終了しますか？", "GrabForge", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (vm.Busy && !restarting && MessageBox.Show("操作を中止して終了しますか？", "GrabForge", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        closing = true; refresh.Stop(); previewToken?.Cancel();
        try { await vm.DisposeAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "保存エラー"); }
        Close();
    }
}
