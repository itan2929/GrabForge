using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace GrabForge.Core;

public sealed record ToolDefinition(string Id, string Repository, string AssetName, string[] Executables, string LicenseUrl);
public sealed record ToolInstallation(string Version, string Sha256, DateTimeOffset InstalledAt, string Source, string? ExecutableVersion = null);
public sealed class ToolManager(PortableStore store, HttpClient http)
{
    public static readonly ToolDefinition[] Definitions = [
        new("yt-dlp", "yt-dlp/yt-dlp", "yt-dlp.exe", ["yt-dlp.exe"], "https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE"),
        new("ffmpeg", "BtbN/FFmpeg-Builds", "ffmpeg-master-latest-win64-gpl.zip", ["ffmpeg.exe", "ffprobe.exe"], "https://github.com/BtbN/FFmpeg-Builds"),
        new("deno", "denoland/deno", "deno-x86_64-pc-windows-msvc.zip", ["deno.exe"], "https://github.com/denoland/deno/blob/main/LICENSE.md")
    ];
    private readonly SemaphoreSlim mutation = new(1);
    private int activeUsers;
    public ToolPaths Paths => new(Path.Combine(store.Tools, "yt-dlp", "yt-dlp.exe"), Path.Combine(store.Tools, "ffmpeg", "ffmpeg.exe"), Path.Combine(store.Tools, "ffmpeg", "ffprobe.exe"), Path.Combine(store.Tools, "deno", "deno.exe"));
    public IDisposable AcquireUsage()
    {
        lock (mutation) { if (mutation.CurrentCount == 0) throw new InvalidOperationException("ツール更新中です。完了後に操作してください。"); activeUsers++; }
        return new Usage(() => { lock (mutation) activeUsers--; });
    }
    private sealed class Usage(Action dispose) : IDisposable { private int disposed; public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) dispose(); } }
    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"GrabForge/{AppUpdates.CurrentVersion} (+https://github.com/itan2929/GrabForge)");
        return client;
    }
    public ToolInstallation? Installed(string id)
    {
        var manifest = Path.Combine(store.Tools, id, "installation.json");
        return File.Exists(manifest) ? JsonSerializer.Deserialize<ToolInstallation>(File.ReadAllText(manifest), Json.Options) : null;
    }
    public async Task<JsonElement> LatestAsync(ToolDefinition tool, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"https://api.github.com/repos/{tool.Repository}/releases/latest", ct);
        response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); return json.RootElement.Clone();
    }
    public async Task InstallAsync(ToolDefinition tool, Action<string> progress, CancellationToken ct = default)
    {
        lock (mutation) { if (activeUsers > 0 || !mutation.Wait(0)) throw new InvalidOperationException("解析・ダウンロード・更新中はツールを変更できません。"); }
        var staging = Path.Combine(store.Tools, ".staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging); var release = await LatestAsync(tool, ct);
            var asset = release.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == tool.AssetName);
            var url = asset.GetProperty("browser_download_url").GetString()!;
            var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
            if (digest is null || !digest.StartsWith("sha256:") || !IsSha256(digest[7..])) throw new IOException("配布元のSHA-256を取得できません。検証なしのインストールは行いません。");
            progress($"{tool.Id}: ダウンロード中…"); var archive = Path.Combine(staging, tool.AssetName);
            await DownloadVerifiedAsync(http, new Uri(url), archive, digest[7..], progress, ct);
            var content = Path.Combine(staging, "content"); Directory.CreateDirectory(content);
            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(archive);
                foreach (var name in tool.Executables)
                {
                    var entry = zip.Entries.Single(e => Path.GetFileName(e.FullName).Equals(name, StringComparison.OrdinalIgnoreCase));
                    await using var source = entry.Open(); await using var target = File.Create(Path.Combine(content, name)); await source.CopyToAsync(target, ct);
                }
                foreach (var entry in zip.Entries.Where(e => e.Length > 0 && (e.FullName.Contains("LICENSE", StringComparison.OrdinalIgnoreCase) || e.FullName.Contains("COPYING", StringComparison.OrdinalIgnoreCase))))
                { var name = "license-" + Guid.NewGuid().ToString("N")[..8] + "-" + Path.GetFileName(entry.FullName); entry.ExtractToFile(Path.Combine(content, name)); }
            }
            else File.Copy(archive, Path.Combine(content, tool.Executables[0]));
            string? executableVersion = null;
            foreach (var name in tool.Executables)
            {
                progress($"{name}: 動作確認中…");
                var args = name.StartsWith("ff") ? new[] { "-version" } : new[] { "--version" };
                using var check = CancellationTokenSource.CreateLinkedTokenSource(ct); check.CancelAfter(TimeSpan.FromSeconds(20));
                var probe = await new ToolProcess().RunAsync(Path.Combine(content, name), args, cancellationToken: check.Token);
                ToolProcess.RequireSuccess(probe); executableVersion ??= probe.Output.Split('\n')[0].Trim();
            }
            await File.WriteAllTextAsync(Path.Combine(content, "SOURCE.txt"), $"Source: {url}\nLicense: {tool.LicenseUrl}\nSHA-256: {digest[7..]}\n", ct);
            var version = release.GetProperty("tag_name").GetString() ?? "unknown";
            await File.WriteAllTextAsync(Path.Combine(content, "installation.json"), JsonSerializer.Serialize(new ToolInstallation(version, digest[7..], DateTimeOffset.UtcNow, url, executableVersion), Json.Options), ct);
            ct.ThrowIfCancellationRequested(); var current = Path.Combine(store.Tools, tool.Id); var backup = current + ".previous";
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            if (Directory.Exists(current)) Directory.Move(current, backup);
            try { Directory.Move(content, current); }
            catch { if (Directory.Exists(backup) && !Directory.Exists(current)) Directory.Move(backup, current); throw; }
            progress($"{tool.Id}: {version} を準備しました。");
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); mutation.Release(); }
    }
    public static async Task DownloadVerifiedAsync(HttpClient http, Uri source, string destination, string expectedSha256, Action<string>? progress, CancellationToken ct)
    {
        if (!IsSha256(expectedSha256)) throw new InvalidDataException("SHA-256が不正です。");
        if (source.Scheme != "https") throw new InvalidDataException("HTTPS以外の配布元は使用できません。");
        using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(destination))
        {
            var buffer = new byte[131072]; long total = 0; long next = 8 * 1024 * 1024;
            int n; while ((n = await input.ReadAsync(buffer, ct)) > 0)
            {
                total += n; if (total > 2L * 1024 * 1024 * 1024) throw new IOException("配布物がサイズ上限を超えました。");
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                if (total >= next) { progress?.Invoke($"受信 {total / 1048576} MB"); next = total + 8 * 1024 * 1024; }
            }
            if (response.Content.Headers.ContentLength is long length && total != length) throw new IOException("ダウンロードが不完全です。");
        }
        await using var file = File.OpenRead(destination); var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("SHA-256が一致しません。インストールを中止しました。");
    }
    public static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
