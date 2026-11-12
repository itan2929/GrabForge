using System.Text.Json;
using System.Text.RegularExpressions;

namespace GrabForge.Core;

public static class PortableLayout
{
    public static string FindRoot(string applicationBase)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationBase));
        var parent = Path.GetDirectoryName(directory);
        return Path.GetFileName(directory).Equals("app", StringComparison.OrdinalIgnoreCase)
            && parent is not null && File.Exists(Path.Combine(parent, "GrabForge.exe"))
            && File.Exists(Path.Combine(directory, "release.json")) ? parent : directory;
    }

    public static string Manifest(string directory) => File.Exists(Path.Combine(directory, "app", "release.json"))
        ? Path.Combine(directory, "app", "release.json") : Path.Combine(directory, "release.json");

    public static string Updater(string root) => Directory.Exists(Path.Combine(root, "app", "updater"))
        ? Path.Combine(root, "app", "updater") : Path.Combine(root, "updater");

    public static async Task PrepareAsync(string root, CancellationToken ct = default)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var app = Path.Combine(root, "app");
        if (!File.Exists(Path.Combine(app, "GrabForge.dll"))) return;
        RejectLinks(root, app); RejectLinks(root, Path.Combine(root, "data"));
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(await File.ReadAllTextAsync(Path.Combine(app, "release.json"), ct), Json.Options);
        if (manifest?.Product != "GrabForge" || manifest.Version != AppUpdates.CurrentVersion || manifest.Runtime != "win-x64" || manifest.Layout != 2) throw new InvalidDataException("アプリの配布情報を確認できません。");
        var store = new PortableStore(root);
        var pending = Path.Combine(store.Data, "flat-layout-migration.pending");
        // A fresh install may share its folder with the user's own README/DLLs. Only tidy a detected old app.
        // The marker also lets startup resume safely after a process/power interruption.
        var flat = File.Exists(Path.Combine(root, "GrabForge.dll")) || File.Exists(pending);
        using var inventory = typeof(PortableLayout).Assembly.GetManifestResourceStream("GrabForge.Core.legacy-files.json")!;
        var legacy = await JsonSerializer.DeserializeAsync<string[]>(inventory, cancellationToken: ct) ?? [];
        var files = (flat ? legacy : []).Where(p => p != "GrabForge.exe" && p != "release.json")
            .Select(p => Path.Combine(root, p)).Where(File.Exists).ToArray();
        var backups = Directory.GetDirectories(root, ".update-backup-*").Where(p => Regex.IsMatch(Path.GetFileName(p), "^\\.update-backup-[a-f0-9]{32}$")).ToArray();
        var moved = new List<(string Source, string Destination)>();
        var backup = Path.Combine(store.Data, "backups", "flat-layout-" + Guid.NewGuid().ToString("N"));
        if (files.Length > 0 || backups.Length > 0) await File.WriteAllTextAsync(pending, AppUpdates.CurrentVersion, ct);
        try
        {
            foreach (var source in files.Concat(backups))
            {
                ct.ThrowIfCancellationRequested(); RejectLinks(root, source); RejectLinks(root, Path.GetDirectoryName(backup)!);
                var destination = Path.Combine(backup, Path.GetRelativePath(root, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await WindowsFileOperations.RetryAsync(() =>
                {
                    if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination);
                }, ct);
                moved.Add((source, destination));
            }
            // Old helpers require this at ZIP root. Once launched, the app stores it internally.
            var compatibility = Path.Combine(root, "release.json");
            if (File.Exists(compatibility))
            {
                RejectLinks(root, compatibility);
                var old = JsonSerializer.Deserialize<ReleaseManifest>(await File.ReadAllTextAsync(compatibility, ct), Json.Options);
                if (old == manifest) File.Move(compatibility, Path.Combine(app, "release.json"), true);
            }
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var pair in moved.AsEnumerable().Reverse())
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(pair.Source)!);
                    if (Directory.Exists(pair.Destination)) Directory.Move(pair.Destination, pair.Source); else File.Move(pair.Destination, pair.Source);
                }
                catch (Exception rollback) { errors.Add(rollback); }
            if (errors.Count > 1) throw new AggregateException("旧版の整理を復元できませんでした。バックアップ: " + backup, errors);
            throw;
        }
        // Delete only empty known package directories, without recursive user-content removal.
        foreach (var directory in (flat ? legacy : []).Select(p => Path.GetDirectoryName(Path.Combine(root, p))).Where(p => p is not null && p != root)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(p => p!.Length))
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        if (File.Exists(pending)) File.Delete(pending);
    }

    internal static void RejectLinks(string root, string path)
    {
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        for (var current = path; !string.Equals(current, root, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current)!)
        {
            if (string.IsNullOrEmpty(current) || !current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("整理対象のパスが範囲外です。");
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("旧版のファイル領域にリンクがあります。");
        }
    }
}
