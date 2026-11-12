using System.Text.Json;
using GrabForge.Core;
using Xunit;

namespace GrabForge.Tests;

public sealed class PortableLayoutTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GrabForge-layout-日本語-" + Guid.NewGuid().ToString("N"));
    private string Write(string name, string content = "fixture")
    {
        var path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); return path;
    }
    private void NewLayout()
    {
        Write("GrabForge.exe"); Write("app/GrabForge.dll");
        var manifest = JsonSerializer.Serialize(new ReleaseManifest("GrabForge", AppUpdates.CurrentVersion, "win-x64", 2));
        Write("app/release.json", manifest); Write("release.json", manifest);
    }
    [Fact] public void ResolvesPackagedRootWithoutChangingDevelopmentRoots()
    {
        NewLayout();
        Assert.Equal(root, PortableLayout.FindRoot(Path.Combine(root, "app") + Path.DirectorySeparatorChar));
        Assert.Equal(Path.Combine(root, "bin"), PortableLayout.FindRoot(Path.Combine(root, "bin")));
        Assert.Equal(Path.Combine(root, "app", "release.json"), PortableLayout.Manifest(root));
    }
    [Fact] public void ToolsUseDataForNewInstallsAndKeepExistingLegacyTools()
    {
        var store = new PortableStore(root); Assert.Equal(Path.Combine(root, "data", "tools"), store.Tools);
        Write("tools/keep.txt"); Assert.Equal(Path.Combine(root, "tools"), store.Tools);
    }
    [Fact] public async Task StartupBacksUpOnlyKnownLegacyFilesAndTidiesManifest()
    {
        NewLayout(); Write("GrabForge.dll", "old app"); Write("System.Runtime.dll", "old runtime");
        Write("updater/GrabForge.Updater.dll", "old updater");
        Write("user.dll", "user file"); Write("notes.txt", "user notes"); Write("data/presets.json", "preserve presets"); Write("tools/sentinel", "preserve tools");
        var oldBackup = ".update-backup-" + Guid.NewGuid().ToString("N"); Write(oldBackup + "/GrabForge.exe", "old backup");
        await PortableLayout.PrepareAsync(root);
        Assert.False(File.Exists(Path.Combine(root, "release.json"))); Assert.False(File.Exists(Path.Combine(root, "GrabForge.dll")));
        Assert.False(Directory.Exists(Path.Combine(root, "updater"))); Assert.False(Directory.Exists(Path.Combine(root, oldBackup)));
        var backup = Assert.Single(Directory.GetDirectories(Path.Combine(root, "data", "backups")));
        Assert.Equal("old app", File.ReadAllText(Path.Combine(backup, "GrabForge.dll")));
        Assert.Equal("old backup", File.ReadAllText(Path.Combine(backup, oldBackup, "GrabForge.exe")));
        Assert.Equal("user file", File.ReadAllText(Path.Combine(root, "user.dll")));
        Assert.Equal("user notes", File.ReadAllText(Path.Combine(root, "notes.txt")));
        Assert.Equal("preserve presets", File.ReadAllText(Path.Combine(root, "data", "presets.json")));
        Assert.Equal("preserve tools", File.ReadAllText(Path.Combine(root, "tools", "sentinel")));
        await PortableLayout.PrepareAsync(root); Assert.Single(Directory.GetDirectories(Path.Combine(root, "data", "backups")));
    }
    [Fact] public async Task InterruptedMigrationRestoresAlreadyMovedFiles()
    {
        NewLayout(); Write("GrabForge.dll", "old app"); Write("Accessibility.dll", "first"); var locked = Write("System.Runtime.dll", "locked");
        using var handle = File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PortableLayout.PrepareAsync(root, cancel.Token));
        Assert.Equal("first", File.ReadAllText(Path.Combine(root, "Accessibility.dll")));
        Assert.Equal("locked", File.ReadAllText(locked)); Assert.True(File.Exists(Path.Combine(root, "release.json")));
    }
    [Fact] public async Task FreshInstallDoesNotTidyTheUsersOwnMatchingFilenames()
    {
        NewLayout(); Write("README.md", "user notes"); Write("System.Runtime.dll", "user file");
        await PortableLayout.PrepareAsync(root);
        Assert.Equal("user notes", File.ReadAllText(Path.Combine(root, "README.md")));
        Assert.Equal("user file", File.ReadAllText(Path.Combine(root, "System.Runtime.dll")));
        Assert.False(Directory.Exists(Path.Combine(root, "data", "backups")));
    }
    [Fact] public async Task PendingMigrationResumesWhenTheOldMainAssemblyWasAlreadyMoved()
    {
        NewLayout(); Write("data/flat-layout-migration.pending", "interrupted"); Write("System.Runtime.dll", "remaining runtime");
        await PortableLayout.PrepareAsync(root);
        Assert.False(File.Exists(Path.Combine(root, "System.Runtime.dll")));
        Assert.False(File.Exists(Path.Combine(root, "data", "flat-layout-migration.pending")));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "data", "backups"), "System.Runtime.dll", SearchOption.AllDirectories));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
