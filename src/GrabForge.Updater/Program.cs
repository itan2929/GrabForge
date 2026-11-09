using System.Diagnostics;
using System.Text.Json;
using GrabForge.Core;

if (args.Length != 1) { Console.Error.WriteLine("更新リクエストを指定してください。"); return 2; }
UpdateRequest? request = null;
try
{
    request = JsonSerializer.Deserialize<UpdateRequest>(await File.ReadAllTextAsync(args[0]), Json.Options) ?? throw new InvalidDataException("更新リクエストが空です。");
    try { using var parent = Process.GetProcessById(request.ProcessId); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); await parent.WaitForExitAsync(timeout.Token); }
    catch (ArgumentException) { }
    await UpdateInstaller.ApplyAsync(request);
    var root = Path.GetFullPath(request.Root);
    Process.Start(new ProcessStartInfo(Path.Combine(root, "GrabForge.exe")) { UseShellExecute = true, WorkingDirectory = root });
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine(e);
    if (request is not null) { Directory.CreateDirectory(Path.Combine(request.Root, "data")); await File.WriteAllTextAsync(Path.Combine(request.Root, "data", "update-error.txt"), e.ToString()); }
    return 1;
}
