// Copies the player's FM27 saves before the first edit of each game session, so
// any edit can be undone by loading the copy. Keeps the newest five copies.
using System;
using System.IO;
using System.Linq;

namespace FM27LiveEditor;

public static class Backup
{
    static readonly object gate = new();
    public static DateTime? Done;
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FM27 Live Editor", "Save backups");

    // Game Pass keeps saves in the app's package folder; Steam in Documents.
    static string[] Sources() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "SportsInteractive.FootballManager27_5w3tn6tb6stnm", "SystemAppData", "wgs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 27", "games"),
    }.Where(Directory.Exists).ToArray();

    public static void EnsureBackedUp()
    {
        lock (gate)
        {
            if (Done != null) return;
            var sources = Sources();
            if (sources.Length == 0) throw new EditorError("Couldn't find your FM27 saves to back them up, so nothing was changed.", 500);
            var dest = Path.Combine(Folder, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
            try
            {
                foreach (var src in sources) Copy(src, Path.Combine(dest, Path.GetFileName(src)));
            }
            catch (Exception e)
            {
                throw new EditorError($"Couldn't back up your saves ({e.Message}), so nothing was changed.", 500);
            }
            Done = DateTime.Now;
            Log.Info("Saves backed up to " + dest);
            foreach (var old in new DirectoryInfo(Folder).GetDirectories().OrderByDescending(d => d.Name).Skip(5))
                try { old.Delete(true); } catch { }
        }
    }

    static void Copy(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src)) Copy(d, Path.Combine(dest, Path.GetFileName(d)));
    }
}
