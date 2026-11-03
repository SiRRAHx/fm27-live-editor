// Read-only routes used while working out more of FM's layout (CA/PA, clubs,
// contracts). They're off unless BepInEx\config\FM27LiveEditor.research exists,
// and they never write anything.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using BepInEx;

namespace FM27LiveEditor;

public static class Research
{
    public static bool On => File.Exists(Path.Combine(Paths.ConfigPath, "FM27LiveEditor.research"));

    public class ReadRequest
    {
        public List<string> addrs { get; set; }
        public int len { get; set; }
    }

    public static object Route(string method, string what, string q, string content)
    {
        if (!On) throw new EditorError("Not found.", 404);
        var args = q.Split('&').Select(kv => kv.Split('=', 2)).Where(a => a.Length == 2).ToDictionary(a => a[0], a => a[1]);
        int Arg(string k, int d) => args.TryGetValue(k, out var v) ? (v.StartsWith("-0x") ? -Convert.ToInt32(v[3..], 16) : v.StartsWith("0x") ? Convert.ToInt32(v[2..], 16) : int.Parse(v)) : d;

        if (what == "dump" && method == "GET")
        {
            int len = Math.Clamp(Arg("len", 0x600), 8, 0x2000);
            return Players.Dump(Math.Clamp(Arg("n", 3000), 1, 20000), Arg("from", -0x288), len);
        }
        if (what == "read" && method == "POST")
        {
            var req = JsonSerializer.Deserialize<ReadRequest>(content) ?? new ReadRequest();
            int len = Math.Clamp(req.len, 1, 0x1000);
            return (req.addrs ?? new()).Take(20000).Select(a =>
            {
                var b = long.TryParse(a, System.Globalization.NumberStyles.HexNumber, null, out long addr) ? Mem.Read(addr, len) : null;
                return b == null ? null : Convert.ToBase64String(b);
            }).ToList();
        }
        if (what == "person" && method == "GET") return Players.Find(Arg("id", 0)).ToString("X");
        // Names of the textures Unity has loaded, to see how FM names its photos and badges.
        if (what == "textures" && method == "GET")
        {
            string filter = args.TryGetValue("q", out var fq) ? Uri.UnescapeDataString(fq) : "";
            return MainThread.Run(() =>
            {
                var all = UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.Texture2D>());
                var names = new List<string>();
                for (int i = 0; i < all.Length && names.Count < 2000; i++)
                {
                    var t = all[i].Cast<UnityEngine.Texture2D>();
                    if (filter == "" || (t.name ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase)) names.Add($"{t.name} {t.width}x{t.height}");
                }
                return new Dictionary<string, object> { ["count"] = all.Length, ["names"] = names };
            });
        }
        if (what == "module" && method == "GET") return Mem.ModuleBase("game_plugin.dll").ToString("X");
        if (what == "events" && method == "GET") return Events.Recent();
        if (what == "club" && method == "GET") return Clubs.Find(Arg("id", 0)).ToString("X");
        // Which heap blocks hold these addresses (to see whether the game's lists
        // come from the C runtime heap). Pauses the game's allocations while it walks.
        if (what == "heap" && method == "POST")
        {
            var req = JsonSerializer.Deserialize<ReadRequest>(content) ?? new ReadRequest();
            var want = (req.addrs ?? new()).Take(200).Select(a => long.Parse(a, System.Globalization.NumberStyles.HexNumber)).ToList();
            return HeapInfo(want);
        }
        throw new EditorError("Not found.", 404);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HeapEntry { public IntPtr lpData; public uint cbData; public byte cbOverhead, iRegionIndex; public ushort wFlags; public IntPtr a, b, c; }
    [DllImport("kernel32")] static extern IntPtr GetProcessHeap();
    [DllImport("kernel32")] static extern bool HeapLock(IntPtr h);
    [DllImport("kernel32")] static extern bool HeapUnlock(IntPtr h);
    [DllImport("kernel32")] static extern bool HeapWalk(IntPtr h, ref HeapEntry e);
    [DllImport("ucrtbase")] static extern IntPtr _get_heap_handle();

    static object HeapInfo(List<long> want)
    {
        var heap = GetProcessHeap();
        var found = new Dictionary<string, object>();
        long blocks = 0;
        var started = DateTime.Now;
        if (!HeapLock(heap)) throw new EditorError("Couldn't lock the heap.", 500);
        try
        {
            var e = new HeapEntry();
            while (HeapWalk(heap, ref e))
            {
                if ((e.wFlags & 0x4) == 0) continue; // busy blocks only
                blocks++;
                long start = (long)e.lpData, end = start + e.cbData;
                foreach (var w in want)
                    if (w >= start && w < end) found[w.ToString("X")] = new { block = start.ToString("X"), size = e.cbData };
            }
        }
        finally { HeapUnlock(heap); }
        return new
        {
            processHeap = ((long)heap).ToString("X"), crtHeap = ((long)_get_heap_handle()).ToString("X"),
            blocks, seconds = (DateTime.Now - started).TotalSeconds, found,
        };
    }
}

// Research only: records the events FM's screens send to the game (e.g. while
// a transfer goes through), so we can see which game actions do the work. It
// only watches; the event still goes to the game unchanged.
public static class Events
{
    static readonly List<string> lines = new();

    public static void Hook()
    {
        var h = new HarmonyLib.Harmony("com.local.fm27liveeditor.research");
        h.Patch(typeof(FM.GamePlugin.GamePlugin).GetMethod(nameof(FM.GamePlugin.GamePlugin.SendEvent)),
            postfix: new HarmonyLib.HarmonyMethod(typeof(Events), nameof(Event)));
        Plugin.Logger.LogInfo("Research: logging game events");
    }

    public static List<string> Recent() { lock (lines) return lines.ToList(); }

    static string Ref(SI.Interop.InteropReference r)
    {
        if (r == null) return "null";
        var sb = new StringBuilder($"{r.ID.ID}{{");
        if (r.m_id != null) foreach (var p in r.m_id) sb.Append($"{p.ID}={p.Value},");
        return sb.Append('}').ToString();
    }

    // A value as text; a Record (numeric keys to values) is listed field by field, so we can
    // see the terms a screen sends (e.g. a loan offer), nested records included.
    static string Value(SI.Core.TypedValue data, int depth)
    {
        if (data == null) return "null";
        string type = data.DataType?.Name ?? "?";
        var rec = depth < 4 ? data.Get()?.TryCast<SI.Core.Record>() : null;
        if (rec == null) return $"{type}:{data.AsString()}";
        var sb = new StringBuilder("Record{");
        foreach (var kv in rec) sb.Append($"{kv.Key}={Value(kv.Value, depth + 1)}, ");
        return sb.Append('}').ToString();
    }

    public static void Event(uint eventID, SI.Interop.InteropReference objectTo, SI.Core.TypedValue data)
    {
        try
        {
            string v;
            try { v = Value(data, 0); } catch { v = "?"; }
            var line = $"{DateTime.Now:HH:mm:ss.fff} EVENT {eventID} to={Ref(objectTo)} data={v}";
            lock (lines) { lines.Add(line); if (lines.Count > 5000) lines.RemoveAt(0); }
            File.AppendAllText(Path.Combine(Paths.BepInExRootPath, "fm27-live-editor-events.txt"), line + Environment.NewLine);
        }
        catch { }
    }
}
