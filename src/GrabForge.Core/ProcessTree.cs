using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GrabForge.Core;

public static class ProcessTree
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Entry
    {
        public uint Size, Usage, Id; public UIntPtr Heap; public uint Module, Threads, ParentId; public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32FirstW(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32NextW(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr process);
    [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr process);
    public static void Suspend(int pid) => Apply(pid, true);
    public static void Resume(int pid) => Apply(pid, false);
    private static void Apply(int pid, bool suspend)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("一時停止はWindows専用です。");
        var snapshot = CreateToolhelp32Snapshot(2, 0); if (snapshot == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception();
        var children = new Dictionary<int, List<int>>();
        try
        {
            var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>(), Name = "" };
            if (Process32FirstW(snapshot, ref entry)) do
            {
                if (!children.TryGetValue((int)entry.ParentId, out var list)) children[(int)entry.ParentId] = list = [];
                list.Add((int)entry.Id);
            } while (Process32NextW(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        var seen = new HashSet<int>();
        void Walk(int id)
        {
            if (!seen.Add(id)) return;
            try
            {
                using var process = Process.GetProcessById(id);
                if (process.HasExited) return;
                if (suspend) { var code = NtSuspendProcess(process.Handle); if (code != 0) throw new IOException($"一時停止に失敗しました: {code}"); }
                if (children.TryGetValue(id, out var list)) foreach (var child in list) Walk(child);
                if (!suspend) { var code = NtResumeProcess(process.Handle); if (code != 0) throw new IOException($"再開に失敗しました: {code}"); }
            }
            catch (ArgumentException) { } catch (InvalidOperationException) { }
        }
        Walk(pid);
    }
}
