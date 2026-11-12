using System.Windows;
using GrabForge.Core;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GrabForge.Desktop;

public partial class App : Application
{
    private Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var root = PortableLayout.FindRoot(AppContext.BaseDirectory);
        instance = new Mutex(true, "Local\\GrabForge-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..20], out var first);
        if (!first) { MessageBox.Show("このフォルダのGrabForgeは既に起動しています。", "GrabForge"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "GrabForge — エラー", MessageBoxButton.OK, MessageBoxImage.Error); args.Handled = true; };
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await PortableLayout.PrepareAsync(root, timeout.Token);
            if (e.Args.Length == 2 && e.Args[0] == "--self-check")
            {
                var store = new PortableStore(root);
                await File.WriteAllTextAsync(e.Args[1], JsonSerializer.Serialize(new
                {
                    Version = AppUpdates.CurrentVersion, Root = root, BaseDirectory = AppContext.BaseDirectory,
                    RuntimeDirectory = RuntimeEnvironment.GetRuntimeDirectory(), Executable = Environment.ProcessPath,
                    Tools = store.Tools, Updater = PortableLayout.Updater(root)
                }, Json.Options));
                Shutdown(0); return;
            }
            new MainWindow().Show();
        }
        catch (Exception ex)
        {
            if (e.Args.FirstOrDefault() == "--self-check") Console.Error.WriteLine(ex);
            else MessageBox.Show(ex.Message + "\n書き込み可能な場所にZIP全体を展開してください。", "GrabForge — 起動エラー");
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
