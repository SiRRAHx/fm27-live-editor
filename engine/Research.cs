// Read-only routes used while working out more of FM's layout. They're off unless the file
// %LOCALAPPDATA%\FM27 Live Editor\research exists, and they never write anything.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FM27LiveEditor;

public static class Research
{
    public static bool On => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FM27 Live Editor", "research"));

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
        if (what == "module" && method == "GET") return Mem.ModuleBase("game_plugin.dll").ToString("X");
        if (what == "club" && method == "GET") return Clubs.Find(Arg("id", 0)).ToString("X");
        throw new EditorError("Not found.", 404);
    }
}
