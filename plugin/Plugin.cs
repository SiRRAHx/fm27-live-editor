// FM27 Live Editor: the in-game half. It serves the editor window (see Server.cs)
// and reads or changes players in the loaded career (see Players.cs). It hooks
// nothing in the game, except a watch-only event log in research builds.
using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace FM27LiveEditor;

[BepInPlugin("com.local.fm27liveeditor", "FM27 Live Editor", Version)]
public class Plugin : BasePlugin
{
    public const string Version = "0.2.0";
    internal static ManualLogSource Logger;
    static bool? editing;
    public static string Reason = "FM27 is still starting.";

    // FM loads its game module after plugins start, so this is checked when the
    // editor first asks, and again until the module is there.
    public static bool Editing
    {
        get
        {
            if (editing != null) return editing.Value;
            if (!Players.LayoutOk()) return false;
            editing = Layout.Matches();
            Reason = editing.Value ? null : "This version of FM27 isn't supported yet, so editing is switched off. Check for an editor update.";
            Logger.LogInfo(editing.Value ? "Game module found, editing available" : Reason);
            return editing.Value;
        }
    }

    public override void Load()
    {
        Logger = Log;
        try { Server.Start(); }
        catch (Exception e) { Log.LogError("The editor couldn't start: " + e); }
        AddComponent<MenuButton>();
        if (Research.On)
        {
            try { Events.Hook(); }
            catch (Exception e) { Log.LogWarning("research events: " + e.Message); }
        }
    }
}
