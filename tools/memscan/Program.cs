// Read-only memory scanner for FM27. Finds windows of bytes whose values contain a
// known set of attribute values (order unknown), as 1-20 or as 1-100 (shown = v/5).
// Usage: memscan <pid> <window> <v1,v2,...> [maxHits]
using System.Diagnostics;
using System.Runtime.InteropServices;

if (args[0] == "dump")
{
    // memscan dump <pid> <hexaddr> <len>
    var hp = Native.OpenProcess(0x0010 | 0x0400, false, int.Parse(args[1]));
    var a = Convert.ToInt64(args[2], 16); var len = int.Parse(args[3]);
    var d = new byte[len];
    Native.ReadProcessMemory(hp, (IntPtr)a, d, (IntPtr)len, out _);
    for (int r = 0; r < len; r += 16)
        Console.WriteLine($"0x{a + r:X}  +{r,3}: " + string.Join(" ", d.Skip(r).Take(16).Select(x => x.ToString().PadLeft(3))));
    return;
}
if (args[0] == "poke")
{
    // memscan poke <pid> <hexaddr> <expectedOld> <new> : writes one byte only if it still holds expectedOld
    var hp = Native.OpenProcess(0x0010 | 0x0020 | 0x0008 | 0x0400, false, int.Parse(args[1]));
    var a = Convert.ToInt64(args[2], 16);
    var cur = new byte[1];
    Native.ReadProcessMemory(hp, (IntPtr)a, cur, (IntPtr)1, out _);
    if (cur[0] != byte.Parse(args[3])) { Console.WriteLine($"not written: byte is {cur[0]}, expected {args[3]}"); return; }
    var ok = Native.WriteProcessMemory(hp, (IntPtr)a, new[] { byte.Parse(args[4]) }, (IntPtr)1, out _);
    Native.ReadProcessMemory(hp, (IntPtr)a, cur, (IntPtr)1, out _);
    Console.WriteLine($"{(ok ? "written" : "write failed")}: 0x{a:X} {args[3]} -> {cur[0]}");
    return;
}
if (args[0] == "findptr")
{
    // memscan findptr <pid> <hexvalue> [dumpLen] : finds 8-byte-aligned qwords equal to value
    var hp = Native.OpenProcess(0x0010 | 0x0400, false, int.Parse(args[1]));
    // Several targets can be given, comma-separated.
    var targets = args[2].Split(',').Select(t => Convert.ToUInt64(t, 16)).ToHashSet(); var dl = args.Length > 3 ? int.Parse(args[3]) : 0;
    long ad = 0; int count = 0; var bb = new byte[0];
    while (Native.VirtualQueryEx(hp, (IntPtr)ad, out var mi, (uint)Marshal.SizeOf<Native.MBI>()) != 0)
    {
        long sz = (long)mi.RegionSize;
        if (mi.State == 0x1000 && (mi.Protect & 0x04) != 0 && (mi.Protect & 0x100) == 0 && sz < 1L << 30)
        {
            if (bb.Length < sz) bb = new byte[sz];
            if (Native.ReadProcessMemory(hp, (IntPtr)ad, bb, (IntPtr)sz, out var rd))
                for (long k = 0; k + 8 <= (long)rd; k += 8)
                    if (targets.Contains(BitConverter.ToUInt64(bb, (int)k)))
                    {
                        count++;
                        if (count <= 60) Console.WriteLine($"0x{ad + k:X} -> 0x{BitConverter.ToUInt64(bb, (int)k):X}: " + (dl > 0 ? string.Join(" ", bb.Skip((int)k + 8).Take(dl)) : ""));
                    }
        }
        ad += sz;
    }
    Console.WriteLine($"total {count}");
    return;
}
if (args[0] == "findrange")
{
    // memscan findrange <pid> <hexlo> <hexhi> : finds 8-byte-aligned qwords pointing into [lo, hi)
    var hp = Native.OpenProcess(0x0010 | 0x0400, false, int.Parse(args[1]));
    var lo = Convert.ToUInt64(args[2], 16); var hi = Convert.ToUInt64(args[3], 16);
    long ad = 0; int count = 0; var bb = new byte[0];
    while (Native.VirtualQueryEx(hp, (IntPtr)ad, out var mi, (uint)Marshal.SizeOf<Native.MBI>()) != 0)
    {
        long sz = (long)mi.RegionSize;
        if (mi.State == 0x1000 && (mi.Protect & 0x04) != 0 && (mi.Protect & 0x100) == 0 && sz < 1L << 30)
        {
            if (bb.Length < sz) bb = new byte[sz];
            if (Native.ReadProcessMemory(hp, (IntPtr)ad, bb, (IntPtr)sz, out var rd))
                for (long k = 0; k + 8 <= (long)rd; k += 8)
                {
                    var v = BitConverter.ToUInt64(bb, (int)k);
                    if (v < lo || v >= hi) continue;
                    count++;
                    if (count <= 60) Console.WriteLine($"0x{ad + k:X} -> 0x{v:X} (+0x{v - lo:X})");
                }
        }
        ad += sz;
    }
    Console.WriteLine($"total {count}");
    return;
}
if (args[0] == "findstr")
{
    // memscan findstr <pid> <text> : finds the text as UTF-8 and UTF-16, with 32 bytes before each hit
    var hp = Native.OpenProcess(0x0010 | 0x0400, false, int.Parse(args[1]));
    var pats = new[] { ("utf8", System.Text.Encoding.UTF8.GetBytes(args[2])), ("utf16", System.Text.Encoding.Unicode.GetBytes(args[2])) };
    // "len:Name" finds FM's name-table form: an int32 length, then the UTF-8 text.
    if (args[2].StartsWith("len:"))
    {
        var t = System.Text.Encoding.UTF8.GetBytes(args[2][4..]);
        pats = new[] { ("len", BitConverter.GetBytes(t.Length).Concat(t).ToArray()) };
    }
    long ad = 0; int count = 0; var bb = new byte[0];
    while (Native.VirtualQueryEx(hp, (IntPtr)ad, out var mi, (uint)Marshal.SizeOf<Native.MBI>()) != 0)
    {
        long sz = (long)mi.RegionSize;
        if (mi.State == 0x1000 && (mi.Protect & 0x04) != 0 && (mi.Protect & 0x100) == 0 && sz < 1L << 30)
        {
            if (bb.Length < sz) bb = new byte[sz];
            if (Native.ReadProcessMemory(hp, (IntPtr)ad, bb, (IntPtr)sz, out var rd))
                foreach (var (name, p) in pats)
                    for (int k = 32; k + p.Length <= (int)rd; k++)
                    {
                        if (bb[k] != p[0] || !bb.AsSpan(k, p.Length).SequenceEqual(p)) continue;
                        count++;
                        if (count <= 60) Console.WriteLine($"0x{ad + k:X} {name}: before " + string.Join(" ", bb.Skip(k - 32).Take(32)));
                    }
        }
        ad += sz;
    }
    Console.WriteLine($"total {count}");
    return;
}
var pid = int.Parse(args[0]);
var window = int.Parse(args[1]);
var want = args[2].Split(',').Select(int.Parse).ToArray();
var maxHits = args.Length > 3 ? int.Parse(args[3]) : 40;
var loose = args.Contains("loose");
// 1 = stored as 1-20; 5 = 1-100 rounded; 6 = 1-100 floored; 7 = 1-100 rounded up
var scales = args.FirstOrDefault(a => a.StartsWith("scales="))?.Substring(7).Split('+').Select(int.Parse).ToArray() ?? new[] { 1, 5 };
var need = new int[21];
foreach (var v in want) need[v]++;

var h = Native.OpenProcess(0x0010 | 0x0400, false, pid); // VM_READ | QUERY_INFORMATION
if (h == IntPtr.Zero) { Console.WriteLine("OpenProcess failed " + Marshal.GetLastWin32Error()); return; }

long addr = 0, scanned = 0; int hits = 0;
var sw = Stopwatch.StartNew();
var buf = new byte[0];
while (Native.VirtualQueryEx(h, (IntPtr)addr, out var mbi, (uint)Marshal.SizeOf<Native.MBI>()) != 0)
{
    long size = (long)mbi.RegionSize;
    bool ok = mbi.State == 0x1000 && (mbi.Protect & 0x04) != 0 && (mbi.Protect & 0x100) == 0; // COMMIT, READWRITE, not GUARD
    if (ok && size < 1L << 30)
    {
        if (buf.Length < size) buf = new byte[size];
        if (Native.ReadProcessMemory(h, (IntPtr)addr, buf, (IntPtr)size, out var read))
        {
            int n = (int)read;
            scanned += n;
            foreach (var scale in scales)
            {
                if (scale >= 32)
                {
                    // 32 = int32 1-20, 33 = float 1-20, 34 = int32 1-100: map each 4-byte value to one byte and scan that
                    for (int off = 0; off < 4; off++)
                    {
                        int m = (n - off) / 4;
                        var conv = new byte[m];
                        for (int k = 0; k < m; k++)
                        {
                            int at = off + k * 4;
                            double v = scale == 33 ? BitConverter.ToSingle(buf, at) : BitConverter.ToInt32(buf, at);
                            if (scale == 34) v = Math.Round(v / 5.0);
                            // 35 = float 1-100 (shown = round(v/5)), 36 = float 1-20 with fractions, 37 = float 0-1 (shown = round(v*20))
                            if (scale == 35 || scale == 36 || scale == 37) v = BitConverter.ToSingle(buf, at);
                            if (scale == 35) v = v >= 1 && v <= 100 ? Math.Round(v / 5.0) : -1;
                            if (scale == 36) v = v >= 1 && v <= 20.5 ? Math.Round(v) : -1;
                            if (scale == 37) v = v > 0 && v <= 1 ? Math.Round(v * 20) : -1;
                            conv[k] = v >= 1 && v <= 20 && v == Math.Floor(v) ? (byte)v : (byte)255;
                        }
                        hits += Scan(conv, m, addr + off, 1, 4);
                    }
                }
                else hits += Scan(buf, n, addr, scale);
            }
        }
    }
    addr += size;
    if (hits >= maxHits) break;
}
Console.WriteLine($"scanned {scanned / (1 << 20)} MB in {sw.Elapsed.TotalSeconds:F1}s, hits {hits}");

int Scan(byte[] b, int n, long baseAddr, int scale, int stride = 1)
{
    int found = 0;
    var have = new int[21];
    int Shown(byte x) => x > 100 ? -1 : scale switch
    {
        1 => x <= 20 ? x : -1,
        6 => x / 5,
        7 => (x + 4) / 5,
        _ => (x + 2) / 5,
    };
    for (int i = 0; i < n; i++)
    {
        var s = Shown(b[i]); if (s >= 0) have[s]++;
        if (i >= window) { var o = Shown(b[i - window]); if (o >= 0) have[o]--; }
        if (i < window - 1) continue;
        bool match = true;
        for (int v = 1; v <= 20; v++) if (have[v] < need[v]) { match = false; break; }
        if (!match) continue;
        // every byte in the window must be a plausible attribute value
        int start = i - window + 1; bool clean = true;
        if (!loose) for (int k = start; k <= i; k++) if (Shown(b[k]) < 0 || (scale == 5 && b[k] == 0)) { clean = false; break; }
        if (!clean) continue;
        Console.WriteLine($"0x{baseAddr + (long)start * stride:X} scale={scale}{(stride > 1 ? " x" + stride : "")}: {string.Join(" ", b.Skip(start).Take(window))}");
        found++; if (hits + found >= maxHits) return found;
        i += window; Array.Clear(have);
    }
    return found;
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MBI { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public ushort PartitionId; public IntPtr RegionSize; public uint State, Protect, Type; }
    [DllImport("kernel32", SetLastError = true)] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32")] public static extern int VirtualQueryEx(IntPtr h, IntPtr addr, out MBI mbi, uint len);
    [DllImport("kernel32")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);
    [DllImport("kernel32")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
}
