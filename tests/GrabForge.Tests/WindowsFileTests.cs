using GrabForge.Core;
using Xunit;

namespace GrabForge.Tests;

public sealed class WindowsFileTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GrabForge-lock-tests-" + Guid.NewGuid().ToString("N"));
    [Fact] public async Task ReplacementSucceedsWhenTransientWindowsLockIsReleased()
    {
        Directory.CreateDirectory(root); var source = Path.Combine(root, "new.exe"); var target = Path.Combine(root, "tool.exe");
        await File.WriteAllTextAsync(source, "new"); await File.WriteAllTextAsync(target, "old");
        using var held = File.Open(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () => { await Task.Delay(200); held.Dispose(); });
        await WindowsFileOperations.RetryAsync(() => File.Move(source, target, true)); await release;
        Assert.Equal("new", await File.ReadAllTextAsync(target)); Assert.False(File.Exists(source));
    }
    [Fact] public async Task CancellationDuringLockPreservesBothFiles()
    {
        Directory.CreateDirectory(root); var source = Path.Combine(root, "new.exe"); var target = Path.Combine(root, "tool.exe");
        await File.WriteAllTextAsync(source, "new"); await File.WriteAllTextAsync(target, "old");
        using var held = File.Open(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WindowsFileOperations.RetryAsync(() => File.Move(source, target, true), stop.Token));
        Assert.Equal("new", await File.ReadAllTextAsync(source)); Assert.Equal("old", await File.ReadAllTextAsync(target));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
