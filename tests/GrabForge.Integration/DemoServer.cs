using System.Net;
using System.Net.Sockets;
using GrabForge.Core;

internal static class DemoServer
{
    public static async Task RunAsync(PortableStore store, ToolManager tools)
    {
        var folder = Path.Combine(store.Root, "demo"); Directory.CreateDirectory(folder);
        var process = new ToolProcess();
        var video = Path.Combine(folder, "sample.mp4");
        ToolProcess.RequireSuccess(await process.RunAsync(tools.Paths.FFmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30:duration=8", "-f", "lavfi", "-i", "sine=frequency=440:duration=8", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", video]));
        var previousDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = folder;
            ToolProcess.RequireSuccess(await process.RunAsync(tools.Paths.FFmpeg, ["-v", "error", "-y", "-i", video,
            "-map", "0:v", "-map", "0:v", "-map", "0:a", "-filter:v:0", "scale=1280:720", "-filter:v:1", "scale=640:360",
            "-c:v", "libx264", "-preset", "ultrafast", "-b:v:0", "1600k", "-b:v:1", "600k", "-c:a", "aac", "-b:a", "128k",
            "-use_template", "1", "-use_timeline", "1", "-f", "dash", Path.Combine(folder, "sample.mpd")]));
        }
        finally { Environment.CurrentDirectory = previousDirectory; }
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        Console.WriteLine($"DEMO_URL=http://127.0.0.1:{port}/demo.html");
        await File.WriteAllTextAsync(Path.Combine(store.Root, "demo-url.txt"), $"http://127.0.0.1:{port}/demo.html");
        while (true)
        {
            var context = await listener.GetContextAsync();
            try
            {
                var html = "<!doctype html><meta charset=utf-8><title>GrabForge demo</title><video controls src='/sample.mpd'></video>";
                var requested = Path.Combine(folder, Path.GetFileName(context.Request.Url!.AbsolutePath));
                var media = File.Exists(requested);
                var bytes = media ? await File.ReadAllBytesAsync(requested) : System.Text.Encoding.UTF8.GetBytes(html);
                context.Response.ContentType = !media ? "text/html; charset=utf-8" : requested.EndsWith(".mpd") ? "application/dash+xml" : "video/mp4";
                context.Response.ContentLength64 = bytes.Length;
                if (context.Request.HttpMethod != "HEAD") await context.Response.OutputStream.WriteAsync(bytes);
            }
            catch (HttpListenerException) { }
            finally { context.Response.Close(); }
        }
    }
}
