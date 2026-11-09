using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GrabForge.Core;
using Xunit;

namespace GrabForge.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GrabForge-update-tests-" + Guid.NewGuid().ToString("N"));
    private UpdateRequest Package(params (string Path, string Content)[] extra)
    {
        Directory.CreateDirectory(root); var archive = Path.Combine(root, "fixture.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            foreach (var (path, content) in new[] { ("GrabForge.exe", "new executable"), ("release.json", JsonSerializer.Serialize(new ReleaseManifest("GrabForge", "0.2.0", "win-x64"), Json.Options)) }.Concat(extra))
            { using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write(content); }
        var install = Path.Combine(root, "application"); Directory.CreateDirectory(install);
        return new(-1, install, archive, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))), "0.2.0");
    }
    [Fact] public async Task UpdateReplacesApplicationAndPreservesPortableData()
    {
        var request = Package(("updater/helper.dll", "helper")); var store = new PortableStore(request.Root);
        File.WriteAllText(Path.Combine(request.Root, "GrabForge.exe"), "old executable");
        await store.SaveAsync("settings.json", new { Sentinel = "keep settings" });
        Directory.CreateDirectory(store.Tools); File.WriteAllText(Path.Combine(store.Tools, "sentinel"), "keep tools");
        var backup = await UpdateInstaller.ApplyAsync(request);
        Assert.Equal("new executable", File.ReadAllText(Path.Combine(request.Root, "GrabForge.exe")));
        Assert.Equal("old executable", File.ReadAllText(Path.Combine(backup, "GrabForge.exe")));
        Assert.Contains("keep settings", File.ReadAllText(Path.Combine(store.Data, "settings.json")));
        Assert.Equal("keep tools", File.ReadAllText(Path.Combine(store.Tools, "sentinel")));
        Assert.Contains("0.2.0", File.ReadAllText(Path.Combine(store.Data, "update-result.txt")));
    }
    [Theory] [InlineData("../outside.txt")] [InlineData("data/settings.json")] [InlineData("TOOLS/yt-dlp.exe")] [InlineData("nested/a:stream")] [InlineData("nested/../escape")]
    public async Task UnsafePackagesLeaveApplicationUnchanged(string path)
    {
        var request = Package((path, "unsafe")); var exe = Path.Combine(request.Root, "GrabForge.exe"); File.WriteAllText(exe, "original");
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => UpdateInstaller.ApplyAsync(request));
        Assert.Equal("original", File.ReadAllText(exe));
    }
    [Fact] public async Task CorruptAndWrongVersionPackagesFailBeforeMutation()
    {
        var request = Package();
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateInstaller.ApplyAsync(request with { Sha256 = new string('0', 64) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateInstaller.ApplyAsync(request with { ExpectedVersion = "9.9.9" }));
        Assert.False(File.Exists(Path.Combine(request.Root, "GrabForge.exe")));
    }
    [Fact] public async Task LockedDestinationRollsBackPreviouslyAppliedFiles()
    {
        var request = Package(("zz-locked.dll", "new locked"));
        var exe = Path.Combine(request.Root, "GrabForge.exe"); File.WriteAllText(exe, "original executable");
        var locked = Path.Combine(request.Root, "zz-locked.dll"); File.WriteAllText(locked, "original dll");
        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => UpdateInstaller.ApplyAsync(request));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString() ?? "Update unexpectedly succeeded");
        }
        Assert.Equal("original executable", File.ReadAllText(exe)); Assert.Equal("original dll", File.ReadAllText(locked));
        Assert.False(File.Exists(Path.Combine(request.Root, "release.json")));
    }
    [Fact] public async Task UpdateFeedFindsCorrectProductAndVerifiedAsset()
    {
        var bytes = "package fixture"u8.ToArray(); var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var payload = JsonSerializer.Serialize(new { tag_name = "v0.2.0", body = "notes", html_url = "https://github.com/example/app/releases/tag/v0.2.0", assets = new[] { new { name = "GrabForge-v0.2.0-win-x64.zip", browser_download_url = "https://example.com/package", digest = "sha256:" + hash } } });
        using var http = new HttpClient(new FakeHandler(r => new(System.Net.HttpStatusCode.OK) { Content = r.RequestUri!.Host == "api.github.com" ? new StringContent(payload) : new ByteArrayContent(bytes) }));
        var updates = new AppUpdates(new(root), http); var release = await updates.CheckAsync("example/app"); Assert.NotNull(release);
        var requestPath = await updates.PrepareAsync(release, _ => { });
        var request = JsonSerializer.Deserialize<UpdateRequest>(File.ReadAllText(requestPath), Json.Options)!;
        Assert.Equal(hash, request.Sha256); Assert.Equal(bytes, File.ReadAllBytes(request.Archive));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
