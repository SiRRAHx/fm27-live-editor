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
        // Every loan in the career grouped by its clause bits (loan +0xB0), with a few examples each,
        // to match the bits to FM's loan screen.
        // The objects a club's first team points at: offset, class, and their first bytes.
        if (what == "team-refs" && method == "GET")
        {
            long mod = Mem.ModuleBase("game_plugin.dll");
            long team = Clubs.SeniorTeam(Clubs.Find(int.Parse(args["club"])));
            var b = Mem.Read(team, 0x800);
            var list = new List<object>();
            for (int o = 0; b != null && o + 8 <= b.Length; o += 8)
            {
                long ptr = BitConverter.ToInt64(b, o);
                if (ptr < 0x10000 || ptr > 0x7FFFFFFFFFFF) continue;
                long vt = Mem.ReadPtr(ptr);
                if (vt < mod || vt > mod + 0x20000000) continue;
                string cls = LoanStart.ClassOf(mod, vt);
                if (cls == null) continue;
                var head = Mem.Read(ptr, 0x60);
                var texts = new List<string>();
                if (cls == ".?AVCOMP@db@@")
                    for (int k = 0x10; k < 0x200; k += 8)
                        if (Mem.ReadLenString(Mem.ReadPtr(ptr + k)) is { Length: > 1 and < 80 } t) texts.Add($"+0x{k:X} {t}");
                list.Add(new { at = $"0x{o:X}", cls, ptr = $"0x{ptr:X}", head = head == null ? null : Convert.ToHexString(head), texts });
            }
            return list;
        }
        if (what == "loans" && method == "GET")
        {
            // ?ids=1,2: the whole loan contracts of these players, as hex.
            if (args.TryGetValue("ids", out var ids))
                return ids.Split(',').Select(int.Parse).Select(id =>
                {
                    long pp = Players.Find(id), ln = pp == 0 ? 0 : Loans.Find(pp);
                    var all = ln == 0 ? null : Mem.Read(ln, 0x180);
                    return new { id, name = pp == 0 ? null : Players.Summary(pp)?.name, loan = $"0x{ln:X}", raw = all == null ? null : Convert.ToHexString(all) };
                }).ToList();
            var groups = new Dictionary<string, (int n, List<object> ex)>();
            foreach (var (id, p) in Players.All())
            {
                long loan = Loans.Find(p);
                var b = loan == 0 ? null : Mem.Read(loan, 0x100);
                if (b == null) continue;
                // ?by=cond: loans with a future fee, by the condition number next to it (+0xC4).
                bool byCond = args.TryGetValue("by", out var by) && by == "cond";
                if (byCond && BitConverter.ToInt32(b, 0xC0) <= 0) continue;
                string key = byCond ? BitConverter.ToInt32(b, 0xC4).ToString() : $"0x{BitConverter.ToUInt32(b, 0xB0):X}";
                if (!groups.TryGetValue(key, out var g)) g = (0, new List<object>());
                if (g.ex.Count < (byCond ? 10 : 4))
                    g.ex.Add(new
                    {
                        id, name = Players.Summary(p)?.name, at = Players.TeamClub(BitConverter.ToInt64(b, 0x10)),
                        future = BitConverter.ToInt32(b, 0xC0), buyBack = BitConverter.ToInt32(b, 0xC8),
                        monthly = BitConverter.ToInt32(b, 0xCC), recallFrom = Players.Date(b, 0xF0),
                        raw = Convert.ToHexString(b, 0xA0, 0x60),
                    });
                groups[key] = (g.n + 1, g.ex);
            }
            return groups.OrderByDescending(kv => kv.Value.n).Select(kv => new { bits = kv.Key, count = kv.Value.n, examples = kv.Value.ex }).ToList();
        }
        throw new EditorError("Not found.", 404);
    }
}
