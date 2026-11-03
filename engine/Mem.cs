// Reads and writes FM27's memory from outside, through ReadProcessMemory/WriteProcessMemory on
// fm.exe. These fail safely (return false) if the game frees or moves something mid-read, so a
// bad address can never crash FM. Attach() finds FM again after it restarts.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FM27LiveEditor;

static class Mem
{
    static IntPtr handle;
    static int pid;
    static long gameModule;

    // FM's process id, or 0 when FM isn't running. A new id means a new FM session.
    public static int Pid => pid;

    public static bool Attach()
    {
        lock (typeof(Mem))
        {
            if (handle != IntPtr.Zero && WaitForSingleObject(handle, 0) == WaitTimeout) return true;
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; pid = 0; gameModule = 0; }
            foreach (var p in Process.GetProcessesByName("fm"))
            {
                var h = OpenProcess(Access, false, p.Id);
                if (h == IntPtr.Zero) continue;
                handle = h; pid = p.Id; gameModule = 0;
                Log.Info($"Attached to FM27 (process {pid})");
                return true;
            }
            return false;
        }
    }

    public static bool Read(long addr, byte[] buf, int len)
    {
        if (addr <= 0x10000 || handle == IntPtr.Zero) return false;
        return ReadProcessMemory(handle, (IntPtr)addr, buf, (IntPtr)len, out var read) && (long)read == len;
    }

    public static byte[] Read(long addr, int len)
    {
        var b = new byte[len];
        return Read(addr, b, len) ? b : null;
    }

    public static long ReadPtr(long addr)
    {
        var b = Read(addr, 8);
        return b == null ? 0 : BitConverter.ToInt64(b, 0);
    }

    public static bool Write(long addr, byte[] data)
    {
        if (addr <= 0x10000 || handle == IntPtr.Zero) return false;
        return WriteProcessMemory(handle, (IntPtr)addr, data, (IntPtr)data.Length, out var written) && (long)written == data.Length;
    }

    // FM's strings: an int32 length, then UTF-8 text.
    public static string ReadLenString(long addr)
    {
        var len = Read(addr, 4);
        if (len == null) return null;
        int n = BitConverter.ToInt32(len, 0);
        if (n <= 0 || n > 200) return null;
        var text = Read(addr + 4, n);
        return text == null ? null : Encoding.UTF8.GetString(text);
    }

    // Only FM's game module is ever asked for. FM loads it a little after starting.
    public static long ModuleBase(string name)
    {
        if (gameModule != 0 || handle == IntPtr.Zero) return gameModule;
        try
        {
            foreach (ProcessModule m in Process.GetProcessById(pid).Modules)
                if (string.Equals(m.ModuleName, name, StringComparison.OrdinalIgnoreCase)) return gameModule = (long)m.BaseAddress;
        }
        catch (Exception e) { Log.Warn("modules: " + e.Message); }
        return 0;
    }

    // Committed, private, read-write memory, which is where the game keeps its database.
    public static System.Collections.Generic.IEnumerable<(long start, long size)> HeapRegions()
    {
        long addr = 0x10000;
        while (VirtualQueryEx(handle, (IntPtr)addr, out var mbi, (IntPtr)Marshal.SizeOf<MBI>()) != IntPtr.Zero)
        {
            long size = (long)mbi.RegionSize;
            if (size <= 0) break;
            if (mbi.State == 0x1000 && mbi.Type == 0x20000 && mbi.Protect == 0x04) yield return ((long)mbi.BaseAddress, size);
            addr = (long)mbi.BaseAddress + size;
        }
    }

    // Runs a short piece of code inside FM on a thread of its own, passing it the address of a
    // copy of `data`, and waits for it to finish. Used only to call FM's own functions.
    public static void CallInGame(byte[] code, byte[] data, int ms)
    {
        if (!Attach()) throw new EditorError("FM27 isn't running.", 503);
        IntPtr codeAt = VirtualAllocEx(handle, IntPtr.Zero, (IntPtr)code.Length, MemCommitReserve, PageReadWrite);
        IntPtr dataAt = VirtualAllocEx(handle, IntPtr.Zero, (IntPtr)data.Length, MemCommitReserve, PageReadWrite);
        bool finished = false;
        try
        {
            if (codeAt == IntPtr.Zero || dataAt == IntPtr.Zero) throw new EditorError("FM didn't make room for the change, so nothing was changed.", 500);
            if (!Write((long)codeAt, code) || !Write((long)dataAt, data) || !VirtualProtectEx(handle, codeAt, (IntPtr)code.Length, PageExecuteRead, out _))
                throw new EditorError("Couldn't prepare the change in FM, so nothing was changed.", 500);
            IntPtr thread = CreateRemoteThread(handle, IntPtr.Zero, IntPtr.Zero, codeAt, dataAt, 0, out _);
            if (thread == IntPtr.Zero) throw new EditorError("FM didn't let the editor make the change, so nothing was changed.", 500);
            try
            {
                if (WaitForSingleObject(thread, (uint)ms) == WaitTimeout) throw new EditorError("FM is taking a long time to make the change. Check the game before trying again.", 504);
                finished = true;
            }
            finally { CloseHandle(thread); }
        }
        finally
        {
            // Memory is only given back once FM has finished with it.
            if (finished || codeAt == IntPtr.Zero || dataAt == IntPtr.Zero)
            {
                if (codeAt != IntPtr.Zero) VirtualFreeEx(handle, codeAt, IntPtr.Zero, MemRelease);
                if (dataAt != IntPtr.Zero) VirtualFreeEx(handle, dataAt, IntPtr.Zero, MemRelease);
            }
        }
    }

    const int Access = 0x0002 | 0x0008 | 0x0010 | 0x0020 | 0x0400 | 0x00100000; // create thread, vm op/read/write, query, synchronize
    const uint WaitTimeout = 0x102, MemCommitReserve = 0x3000, MemRelease = 0x8000, PageReadWrite = 0x04, PageExecuteRead = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    struct MBI { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public ushort PartitionId; public IntPtr RegionSize; public uint State, Protect, Type; }

    [DllImport("kernel32", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32")] static extern IntPtr VirtualQueryEx(IntPtr h, IntPtr addr, out MBI mbi, IntPtr len);
    [DllImport("kernel32")] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("kernel32")] static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);
    [DllImport("kernel32")] static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr addr, IntPtr size, uint type, uint protect);
    [DllImport("kernel32")] static extern bool VirtualProtectEx(IntPtr h, IntPtr addr, IntPtr size, uint protect, out uint old);
    [DllImport("kernel32")] static extern bool VirtualFreeEx(IntPtr h, IntPtr addr, IntPtr size, uint type);
    [DllImport("kernel32")] static extern IntPtr CreateRemoteThread(IntPtr h, IntPtr attrs, IntPtr stack, IntPtr start, IntPtr param, uint flags, out uint id);
}
