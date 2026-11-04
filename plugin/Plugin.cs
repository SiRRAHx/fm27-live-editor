// FM27 Live Editor's in-game add-on (optional): puts the Live Editor button on FM's main menu
// and in the in-game top bar. All editing is done by the editor app from outside the game, so
// FM and the editor work the same without this add-on.
using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace FM27LiveEditor;

[BepInPlugin("com.local.fm27liveeditor", "FM27 Live Editor", Version)]
public class Plugin : BasePlugin
{
    public const string Version = "0.3.2";
    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;
        AddComponent<MenuButton>();
        if (Research.On)
        {
            try { Events.Hook(); }
            catch (Exception e) { Log.LogWarning("research events: " + e.Message); }
        }
    }
}
