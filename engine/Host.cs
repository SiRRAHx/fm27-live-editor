// Whether editing is available right now: FM27 running, its game module loaded, and a version
// the editor knows. Checked again whenever FM is started again.
namespace FM27LiveEditor;

public static class Host
{
    public const string Version = "0.3.4";
    static int checkedPid;
    static bool? editing;
    public static string Reason = "Start FM27 and load a save.";

    public static bool Editing
    {
        get
        {
            if (!Mem.Attach())
            {
                editing = null; checkedPid = 0;
                Reason = "Start FM27 and load a save.";
                return false;
            }
            if (Mem.Pid != checkedPid) { editing = null; checkedPid = Mem.Pid; Backup.Done = null; }
            if (editing != null) return editing.Value;
            if (Mem.ModuleBase("game_plugin.dll") == 0) { Reason = "FM27 is still starting."; return false; }
            editing = Layout.Matches();
            Reason = editing.Value ? null : "This version of FM27 isn't supported yet, so editing is switched off. Check for an editor update, or report it with the editor's log.";
            Log.Info(editing.Value ? "Game module found, editing available" : Reason);
            return editing.Value;
        }
    }
}
