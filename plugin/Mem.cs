// Reads and writes the game's own memory through ReadProcessMemory/WriteProcessMemory
// on our own process. Unlike plain pointer reads, these fail safely (return false)
// if the game frees or moves something mid-read, so a bad address can never crash FM.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FM27LiveEditor;

static class Mem
{
    static readonly IntPtr Self = GetCurrentProcess();

    public static bool Read(long addr, byte[] buf, int len)
    {
        if (addr <= 0x10000) return false;
        return ReadProcessMemory(Self, (IntPtr)addr, buf, (IntPtr)len, out var read) && (long)read == len;
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
        if (addr <= 0x10000) return false;
        return WriteProcessMemory(Self, (IntPtr)addr, data, (IntPtr)data.Length, out var written) && (long)written == data.Length;
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

    public static long ModuleBase(string name) => (long)GetModuleHandle(name);

    // Committed, private, read-write memory, which is where the game keeps its database.
    public static System.Collections.Generic.IEnumerable<(long start, long size)> HeapRegions()
    {
        long addr = 0x10000;
        while (VirtualQuery((IntPtr)addr, out var mbi, (IntPtr)Marshal.SizeOf<MBI>()) != IntPtr.Zero)
        {
            long size = (long)mbi.RegionSize;
            if (size <= 0) break;
            if (mbi.State == 0x1000 && mbi.Type == 0x20000 && mbi.Protect == 0x04) yield return ((long)mbi.BaseAddress, size);
            addr = (long)mbi.BaseAddress + size;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MBI { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public ushort PartitionId; public IntPtr RegionSize; public uint State, Protect, Type; }

    [DllImport("kernel32")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32")] static extern IntPtr VirtualQuery(IntPtr addr, out MBI mbi, IntPtr len);
    [DllImport("kernel32")] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("kernel32")] static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);
}
