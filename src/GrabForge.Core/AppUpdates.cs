using System.Text.Json;
using System.Text.RegularExpressions;

namespace GrabForge.Core;

public sealed record AppRelease(string Tag, Version Version, string Notes, string PageUrl, string DownloadUrl, string Sha256);
public sealed record UpdateRequest(int ProcessId, string Root, string Archive, string Sha256, string ExpectedVersion);
public sealed class AppUpdates(PortableStore store, HttpClient http)
{
    public static string CurrentVersion => typeof(AppUpdates).Assembly.GetName().Version!.ToString(3);
    public async Task<AppRelease?> CheckAsync(string repository, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) throw new ArgumentException("GitHubリポジトリは owner/name で指定してください。");
        using var response = await http.GetAsync($"https://api.github.com/repos/{repository}/releases/latest", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode(); using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); var release = doc.RootElement;
        var tag = release.GetProperty("tag_name").GetString()!;
        if (!Version.TryParse(tag.TrimStart('v'), out var version) || version <= Version.Parse(CurrentVersion)) return null;
        var name = $"GrabForge-v{version.ToString(3)}-win-x64.zip";
        var asset = release.GetProperty("assets").EnumerateArray().SingleOrDefault(a => a.GetProperty("name").GetString() == name);
        if (asset.ValueKind != JsonValueKind.Object) throw new IOException("このリリースにWindows x64配布物がありません。");
        var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
        if (digest is null || !digest.StartsWith("sha256:") || !ToolManager.IsSha256(digest[7..])) throw new IOException("更新配布物のSHA-256を確認できません。");
        return new(tag, version, release.GetProperty("body").GetString() ?? "", release.GetProperty("html_url").GetString()!, asset.GetProperty("browser_download_url").GetString()!, digest[7..]);
    }
    public async Task<string> PrepareAsync(AppRelease release, Action<string> progress, CancellationToken ct = default)
    {
        var directory = Path.Combine(store.Data, "updates", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var archive = Path.Combine(directory, "release.zip");
        await ToolManager.DownloadVerifiedAsync(http, new Uri(release.DownloadUrl), archive, release.Sha256, progress, ct);
        var request = new UpdateRequest(Environment.ProcessId, store.Root, archive, release.Sha256, release.Version.ToString(3));
        var path = Path.Combine(directory, "request.json"); await File.WriteAllTextAsync(path, JsonSerializer.Serialize(request, Json.Options), ct); return path;
    }
    public string LaunchUpdater(string requestPath)
    {
        var source = PortableLayout.Updater(store.Root);
        if (!Directory.Exists(source)) throw new IOException("更新ヘルパーがありません。ZIP全体を展開し直してください。");
        var helper = Path.Combine(Path.GetTempPath(), "GrabForge-updater-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(helper);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(helper, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
        }
        var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(helper, "GrabForge.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(requestPath); _ = System.Diagnostics.Process.Start(info) ?? throw new IOException("更新ヘルパーを起動できません。"); return helper;
    }
}
