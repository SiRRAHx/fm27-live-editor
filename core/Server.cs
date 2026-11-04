// A tiny JSON API for the editor window, on 127.0.0.1 only. Requests must carry
// the X-FM27-Editor header and a 127.0.0.1 Host, so web pages in a browser can't
// call it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace FM27LiveEditor;

public static class Server
{
    public const int Port = 27270;
    static DateTime lastIndexTry;

    public static void Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Start();
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    var client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => Handle(client));
                }
                catch (Exception e) { Log.Warn("server: " + e.Message); Thread.Sleep(1000); }
            }
        }) { IsBackground = true, Name = "FM27LiveEditor server" }.Start();
        Log.Info($"Editor connection open on 127.0.0.1:{Port}");
    }

    static void Handle(TcpClient client)
    {
        using var conn = client;
        client.ReceiveTimeout = 5000;
        var stream = client.GetStream();
        int status = 200;
        object body;
        try
        {
            var (method, path, headers, content) = ReadRequest(stream);
            if (!headers.TryGetValue("x-fm27-editor", out _) || !headers.TryGetValue("host", out var host) || host != $"127.0.0.1:{Port}")
                throw new EditorError("Not allowed.", 403);
            body = Route(method, path, content);
        }
        catch (EditorError e) { status = e.Status; body = new Dictionary<string, object> { ["error"] = e.Message }; }
        catch (Exception e)
        {
            status = 500;
            body = new Dictionary<string, object> { ["error"] = "Something went wrong in the editor plugin: " + e.Message };
            Log.Warn("request: " + e);
        }
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {json.Length}\r\nConnection: close\r\n\r\n");
        try { stream.Write(head); stream.Write(json); } catch { }
    }

    static (string, string, Dictionary<string, string>, string) ReadRequest(NetworkStream stream)
    {
        var raw = new MemoryStream();
        var buf = new byte[8192];
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) throw new EditorError("Bad request.", 400);
            raw.Write(buf, 0, n);
            if (raw.Length > 1 << 20) throw new EditorError("Too big.", 400);
            headerEnd = IndexOf(raw.GetBuffer(), (int)raw.Length, "\r\n\r\n");
        }
        var all = raw.ToArray();
        var lines = Encoding.ASCII.GetString(all, 0, headerEnd).Split("\r\n");
        var first = lines[0].Split(' ');
        var headers = new Dictionary<string, string>();
        foreach (var l in lines[1..])
        {
            int c = l.IndexOf(':');
            if (c > 0) headers[l[..c].Trim().ToLowerInvariant()] = l[(c + 1)..].Trim();
        }
        int length = headers.TryGetValue("content-length", out var cl) ? int.Parse(cl) : 0;
        if (length > 1 << 20) throw new EditorError("Too big.", 400);
        var content = new MemoryStream();
        content.Write(all, headerEnd + 4, all.Length - headerEnd - 4);
        while (content.Length < length)
        {
            int n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) break;
            content.Write(buf, 0, n);
        }
        return (first[0], first.Length > 1 ? first[1] : "/", headers, Encoding.UTF8.GetString(content.ToArray()));
    }

    static int IndexOf(byte[] b, int len, string s)
    {
        for (int i = 0; i + s.Length <= len; i++)
        {
            int k = 0;
            while (k < s.Length && b[i + k] == s[k]) k++;
            if (k == s.Length) return i;
        }
        return -1;
    }

    static object Route(string method, string path, string content)
    {
        var q = "";
        int qi = path.IndexOf('?');
        if (qi >= 0) { q = path[(qi + 1)..]; path = path[..qi]; }
        var parts = path.Trim('/').Split('/');
        if (parts.Length < 2 || parts[0] != "api") throw new EditorError("Not found.", 404);

        if (parts[1] == "status" && method == "GET") return Status();
        if (!Host.Editing) throw new EditorError(Host.Reason, 503);
        if (parts[1] == "research" && parts.Length == 3) return Research.Route(method, parts[2], q, content);
        if (parts[1] == "players")
        {
            if (parts.Length == 2 && method == "GET")
            {
                return Players.Search(Query(q, "q"));
            }
            if (parts.Length == 3 && int.TryParse(parts[2], out int id))
            {
                if (method == "GET") return Players.Get(id);
                if (method == "PUT")
                {
                    var req = JsonSerializer.Deserialize<PlayerChanges>(content) ?? new PlayerChanges();
                    // Check the loan first, so a bad loan value stops the whole save.
                    long p = Players.Find(id);
                    var loan = Loans.Plan(p, req.loan);
                    var player = Players.Set(id, req.attributes, req.positions, req.ca, req.pa, req.contract, req.status);
                    int more = Loans.Write(p, loan);
                    if (req.heal) more += Health.Heal(p);
                    if (req.liftBans) more += Health.LiftBans(p);
                    if (req.nation is int nation) more += Players.SetNation(p, nation);
                    if (more > 0) player = Players.Get(id);
                    return new Dictionary<string, object> { ["ok"] = true, ["player"] = player };
                }
            }
        }
        if (parts[1] == "nations" && parts.Length == 2 && method == "GET") return Players.Nations();
        if (parts[1] == "clubs")
        {
            if (parts.Length == 2 && method == "GET") return Clubs.Search(Query(q, "q"));
            if (parts.Length == 3 && int.TryParse(parts[2], out int id))
            {
                if (method == "GET") return Clubs.Get(id);
                if (method == "POST")
                {
                    var values = JsonSerializer.Deserialize<Dictionary<string, int>>(content) ?? new();
                    return new Dictionary<string, object> { ["ok"] = true, ["players"] = Clubs.SetSquad(id, values) };
                }
                if (method == "PUT")
                {
                    var req = JsonSerializer.Deserialize<FinanceChanges>(content) ?? new FinanceChanges();
                    return new Dictionary<string, object> { ["ok"] = true, ["club"] = Clubs.Set(id, req) };
                }
            }
        }
        if (parts[1] == "transfers")
        {
            if (parts.Length == 2 && method == "GET") return Transfers.Status();
            if (parts.Length == 3 && method == "POST")
            {
                if (parts[2] == "undo") return Transfers.Undo();
                if (parts[2] == "loan") return LoanStart.Start(JsonSerializer.Deserialize<LoanStart.Request>(content) ?? throw new EditorError("Bad request.", 400));
                var req = JsonSerializer.Deserialize<MoveRequest>(content) ?? throw new EditorError("Bad request.", 400);
                if (parts[2] == "move") return Transfers.Move(req);
                if (parts[2] == "swap") return Transfers.Swap(req);
                if (parts[2] == "release") return Transfers.Release(req);
                if (parts[2] == "end-loan") return Transfers.EndLoan(req);
            }
        }
        throw new EditorError("Not found.", 404);
    }

    static string Query(string q, string key)
    {
        foreach (var kv in q.Split('&'))
            if (kv.StartsWith(key + "=")) return WebUtility.UrlDecode(kv[(key.Length + 1)..]);
        return "";
    }

    public class PlayerChanges
    {
        public Dictionary<string, int> attributes { get; set; }
        public Dictionary<string, int> positions { get; set; }
        public int? ca { get; set; }
        public int? pa { get; set; }
        public ContractChanges contract { get; set; }
        public LoanChanges loan { get; set; }
        public bool heal { get; set; }
        public bool liftBans { get; set; }
        public int? nation { get; set; }
        public Dictionary<string, int> status { get; set; }
    }

    static object Status()
    {
        bool editing = Host.Editing;
        // Index the career once one is loaded, and again after another save is loaded.
        if (Host.Editing && !Players.Indexing && (Players.Count == 0 || !Players.StillValid()) && DateTime.Now - lastIndexTry > TimeSpan.FromSeconds(20))
        {
            lastIndexTry = DateTime.Now;
            Players.StartIndex();
        }
        else if (Host.Editing && !Players.Indexing && !Players.Rechecked && Players.Count > 0 && DateTime.Now - Players.IndexedAt > TimeSpan.FromSeconds(30))
            Players.StartIndex(recheck: true);
        return new Dictionary<string, object>
        {
            ["game"] = "FM27",
            ["fmRunning"] = Mem.Pid != 0,
            ["editorVersion"] = Host.Version,
            ["editing"] = editing,
            ["reason"] = editing ? null : Host.Reason,
            ["indexing"] = Players.Indexing,
            ["players"] = Players.Count,
            ["saveLoaded"] = Players.Count > 0,
            ["backup"] = Backup.Done?.ToString("HH:mm"),
        };
    }
}
