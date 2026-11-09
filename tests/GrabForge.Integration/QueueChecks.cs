using GrabForge.Core;

internal static class QueueChecks
{
    public static async Task RunAsync(PortableStore source, ToolManager tools, string fixtures, string origin)
    {
        var store = new PortableStore(Path.Combine(source.Root, "queue-control-" + Guid.NewGuid().ToString("N")));
        foreach (var name in new[] { "control-one.wav", "control-two.wav", "control-three.wav" }) File.Copy(Path.Combine(fixtures, "quiet.wav"), Path.Combine(fixtures, name), true);
        async Task Wait(Func<bool> condition) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)); while (!condition()) await Task.Delay(75, timeout.Token); }
        void Require(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
        var options = new DownloadOptions { Mode = DownloadMode.Audio, AudioFormatId = "best", EmbedThumbnail = false, EmbedMetadata = false, EmbedChapters = false, RateLimit = "40K", OutputDirectory = "downloads" };
        Guid cancelled;
        await using (var queue = new DownloadQueue(store, tools) { Concurrency = 2 })
        {
            await queue.AddAsync(new[] { "one", "two", "three" }.Select(s => origin + "/control-" + s + ".wav"), options);
            await Wait(() => queue.Jobs.Count(j => j.State == JobState.Downloading && queue.GetLog(j).Any()) == 2);
            Require(queue.Jobs.Count(j => j.State == JobState.Queued) == 1, "concurrency limit keeps third job queued");
            var job = queue.Jobs.First(j => j.State == JobState.Downloading); cancelled = job.Id;
            await queue.PauseAsync(job); var progress = job.Progress; await Task.Delay(800);
            Require(job.State == JobState.Paused && job.Progress == progress, "pause suspends live yt-dlp progress");
            await queue.PauseAsync(job); Require(job.State == JobState.Downloading, "resume restores running state");
            await queue.PauseAsync(job); await queue.CancelAsync(job);
            // Retry is allowed only once the worker has unwound after cancellation.
            await Wait(() => !queue.IsBusy);
            Require(job.State == JobState.Cancelled && queue.Jobs.Count(j => j.State == JobState.Complete) == 2, "cancel of paused job releases a worker; other jobs complete");
            await queue.RetryAsync(job); await Wait(() => !queue.IsBusy);
            Require(job.State == JobState.Complete && File.Exists(job.OutputPath), "retry resumes cancelled acquisition successfully");
            await queue.AddAsync([origin + "/control-queued.wav"], options); var queued = queue.Jobs.First(); await queue.CancelAsync(queued);
            await Wait(() => !queue.IsBusy); Require(queued.State == JobState.Cancelled, "immediate cancellation is not overwritten by worker startup");
        }
        await using var restored = new DownloadQueue(store, tools);
        Require(restored.Jobs.Single(j => j.Id == cancelled).State == JobState.Complete && restored.Jobs.Count == 4, "history persists final states across application restart");
        await restored.ClearFinishedAsync(); Require(restored.Jobs.Count == 0, "clearing history preserves downloaded files");
    }
}
