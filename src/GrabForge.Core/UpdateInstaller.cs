using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace GrabForge.Core;

public sealed record ReleaseManifest(string Product, string Version, string Runtime, int Layout = 1);

/// <summary>Applies a verified portable package while preserving data and tools.</summary>
public static class UpdateInstaller
{
    public static async Task<string> ApplyAsync(UpdateRequest request)
    {
        await using (var input = File.OpenRead(request.Archive))
            if (!Convert.ToHexString(await SHA256.HashDataAsync(input)).Equals(request.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新ZIPのハッシュが一致しません。");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Root));
        var rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("更新先フォルダを確認できません。");
        var stage = Path.Combine(root, "data", "updates", "stage-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(root, "data", "backups", "update-" + Guid.NewGuid().ToString("N"));
        PortableLayout.RejectLinks(root, Path.Combine(root, "data", "updates"));
        PortableLayout.RejectLinks(root, Path.Combine(root, "data", "backups"));
        Directory.CreateDirectory(stage);
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long size = 0;
            using (var zip = ZipFile.OpenRead(request.Archive))
                foreach (var entry in zip.Entries)
                {
                    var relative = entry.FullName.Replace('\\', '/'); var parts = relative.TrimEnd('/').Split('/');
                    if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.Contains(':') || p.EndsWith('.') || p.EndsWith(' ')) || Path.IsPathRooted(relative)) throw new InvalidDataException("更新ZIPに不正なパスがあります。");
                    if (parts[0].Equals("data", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("tools", StringComparison.OrdinalIgnoreCase) || parts[0].StartsWith('.')) throw new InvalidDataException("更新ZIPに利用者データ領域が含まれています。");
                    if (!names.Add(relative.TrimEnd('/'))) throw new InvalidDataException("更新ZIPに重複パスがあります。");
                    var target = Path.GetFullPath(Path.Combine(stage, relative));
                    if (!target.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新ZIPのパスが範囲外です。");
                    if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) throw new InvalidDataException("更新ZIPにシンボリックリンクがあります。");
                    size += entry.Length; if (size > 2L * 1024 * 1024 * 1024) throw new IOException("更新ZIPの展開サイズが上限を超えました。");
                    if (entry.Name.Length == 0) Directory.CreateDirectory(target);
                    else { Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target); }
                }
            if (!File.Exists(Path.Combine(stage, "GrabForge.exe"))) throw new IOException("更新ZIPにGrabForge.exeがありません。");
            var manifest = JsonSerializer.Deserialize<ReleaseManifest>(await File.ReadAllTextAsync(PortableLayout.Manifest(stage)), Json.Options);
            if (manifest is null || manifest.Product != "GrabForge" || manifest.Runtime != "win-x64" || manifest.Version != request.ExpectedVersion) throw new InvalidDataException("更新ZIPの製品・バージョンが一致しません。");
            var compatibility = Path.Combine(stage, "release.json");
            if (File.Exists(compatibility) && PortableLayout.Manifest(stage) != compatibility &&
                JsonSerializer.Deserialize<ReleaseManifest>(await File.ReadAllTextAsync(compatibility), Json.Options) != manifest)
                throw new InvalidDataException("更新ZIPの互換情報が一致しません。");
            if (manifest.Layout is not (1 or 2) || manifest.Layout == 2 &&
                (!File.Exists(Path.Combine(stage, "app", "GrabForge.dll")) || !File.Exists(Path.Combine(stage, "app", "updater", "GrabForge.Updater.exe"))))
                throw new InvalidDataException("更新ZIPの内部ファイルが不足しています。");
            var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories);
            // Reject existing junctions before making any changes, including parent components.
            foreach (var file in files)
            {
                var target = Path.Combine(root, Path.GetRelativePath(stage, file));
                for (string? current = target; !string.Equals(current, root, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
                {
                    if (current is null || !current.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("更新先のパスを確認できません。");
                    if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("更新先にリンクが含まれています。");
                }
            }
            Directory.CreateDirectory(backup); var applied = new List<string>();
            try
            {
                foreach (var file in files)
                {
                    var relative = Path.GetRelativePath(stage, file); var target = Path.Combine(root, relative); var saved = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (File.Exists(target)) { Directory.CreateDirectory(Path.GetDirectoryName(saved)!); File.Copy(target, saved); }
                    // Rename on the same volume avoids partially overwriting a locked/failed target.
                    File.Move(file, target, true); applied.Add(relative);
                }
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                foreach (var relative in applied.AsEnumerable().Reverse())
                    try
                    {
                        var saved = Path.Combine(backup, relative); var target = Path.Combine(root, relative);
                        if (File.Exists(saved)) File.Move(saved, target, true); else File.Delete(target);
                    }
                    catch (Exception rollback) { errors.Add(rollback); }
                if (errors.Count > 1) throw new AggregateException("更新を復元できませんでした。バックアップ: " + backup, errors);
                throw;
            }
            Directory.CreateDirectory(Path.Combine(root, "data"));
            await File.WriteAllTextAsync(Path.Combine(root, "data", "update-result.txt"), $"{DateTimeOffset.Now:O}: v{request.ExpectedVersion}へ更新しました。バックアップ: {backup}");
            return backup;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
