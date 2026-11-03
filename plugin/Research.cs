// Research only: off unless BepInEx\config\FM27LiveEditor.research exists.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;

namespace FM27LiveEditor;

public static class Research
{
    public static bool On => File.Exists(Path.Combine(Paths.ConfigPath, "FM27LiveEditor.research"));
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
