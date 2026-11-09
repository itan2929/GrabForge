using System.Windows;

namespace GrabForge.Desktop;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\GrabForge-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppContext.BaseDirectory)))[..20], out var first);
        if (!first) { MessageBox.Show("このフォルダのGrabForgeは既に起動しています。", "GrabForge"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "GrabForge — エラー", MessageBoxButton.OK, MessageBoxImage.Error); args.Handled = true; };
        try { new MainWindow().Show(); }
        catch (Exception ex) { MessageBox.Show(ex.Message + "\n書き込み可能な場所にZIP全体を展開してください。", "GrabForge — 起動エラー"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
