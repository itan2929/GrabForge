namespace GrabForge.Core;

public static class WindowsFileOperations
{
    // Executable probes can leave a short-lived loader/scanner handle on Windows.
    public static async Task RetryAsync(Action operation, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { operation(); return; }
            catch (Exception error) when (attempt < 5 && error is IOException or UnauthorizedAccessException)
            { await Task.Delay(250 * (attempt + 1), ct); }
        }
    }
}
