// FM27 Live Editor engine: runs beside FM27 (the editor app starts and stops it), attaches to
// fm.exe from outside and serves the editor window's API on 127.0.0.1 (see Server.cs). Nothing is
// installed into the game for this; the in-game button add-on is optional.
using System;
using System.IO;
using System.Threading;

namespace FM27LiveEditor;

static class Program
{
    static readonly object logGate = new();

    static int Main(string[] args)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FM27 Live Editor");
        Directory.CreateDirectory(dir);
        string logFile = Path.Combine(dir, "engine.log");
        try { if (File.Exists(logFile) && new FileInfo(logFile).Length > 2_000_000) File.Delete(logFile); } catch { }
        Log.Sink = (level, text) =>
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {text}";
            Console.WriteLine(line);
            lock (logGate) try { File.AppendAllText(logFile, line + Environment.NewLine); } catch { }
        };
        Log.Info($"FM27 Live Editor engine {Host.Version} starting");
        try { Server.Start(); }
        catch (Exception e)
        {
            // Most likely another engine is already running.
            Log.Error("The editor couldn't open its connection: " + e.Message);
            return 1;
        }
        // Stop when the editor app that started us closes.
        int parent = args.Length > 1 && args[0] == "--parent" && int.TryParse(args[1], out var p) ? p : 0;
        while (true)
        {
            Thread.Sleep(2000);
            if (parent != 0 && !Alive(parent)) { Log.Info("Editor app closed; engine stopping"); return 0; }
        }
    }

    static bool Alive(int pid)
    {
        try { return !System.Diagnostics.Process.GetProcessById(pid).HasExited; }
        catch { return false; }
    }
}
