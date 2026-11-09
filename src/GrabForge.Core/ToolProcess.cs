using System.Diagnostics;
using System.Text;

namespace GrabForge.Core;

public sealed record ProcessResult(int ExitCode, string Output, string Error);
public sealed class ToolProcess
{
    public async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments,
        Action<string>? output = null, Action<string>? error = null,
        Action<Process>? started = null, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        cancellationToken.ThrowIfCancellationRequested();
        if (!process.Start()) throw new IOException($"ツールを起動できません: {executable}");
        using var registration = cancellationToken.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var stdout = new StringBuilder(); var stderr = new StringBuilder();
        async Task Read(StreamReader reader, StringBuilder text, Action<string>? callback)
        {
            try
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (text.Length > 16 * 1024 * 1024) throw new IOException("ツール出力が上限を超えました。");
                    text.AppendLine(line); callback?.Invoke(line);
                }
            }
            catch { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } throw; }
        }
        try
        {
            started?.Invoke(process);
            await Task.WhenAll(Read(process.StandardOutput, stdout, output), Read(process.StandardError, stderr, error), process.WaitForExitAsync(cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            return new(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } }
    }
    public static void RequireSuccess(ProcessResult result)
    {
        if (result.ExitCode != 0) throw new IOException($"処理に失敗しました（終了コード {result.ExitCode}）。\n{result.Error[^Math.Min(4000, result.Error.Length)..]}");
    }
}
