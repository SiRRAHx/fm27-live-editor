// Adds a "Live Editor" button to FM's main menu, in the bar with "Manager
// Profiles" (FM's manager-button-container, which FM Kits Hub also uses), and
// a pencil icon next to Search in the in-game top bar.
// Clicking it opens the editor app through the shortcut the app writes to
// Documents\FM27 Live Editor each time it starts. Game Pass FM runs sandboxed
// and can't see fm27editor:// style links, but it can read Documents.
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace FM27LiveEditor;

public class MenuButton : MonoBehaviour
{
    const string ButtonName = "fm27liveeditor-button", IconName = "fm27liveeditor-icon";
    public static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FM27 Live Editor", "Open FM27 Live Editor.lnk");

    // Behind the carbon texture (menubg.png, 80% solid), so only a little shows through.
    static readonly Color Back = new(0.03f, 0.13f, 0.10f, 0f);
    static readonly Color BackHover = new(0.06f, 0.30f, 0.22f, 0.9f);
    static readonly Color Rim = new(0.37f, 0.92f, 0.69f, 0.55f);
    static readonly Color RimHover = new(0.98f, 0.75f, 0.14f, 0.95f);
    static readonly Color Mint = new(0.37f, 0.92f, 0.69f);
    static readonly Color Gold = new(0.98f, 0.75f, 0.14f);

    float next = 5f;
    int failures, iconFailures;
    float nextIcon = 5f;
    SI.Bindable.PanelManager panels;
    static Texture2D logo;

    void Update()
    {
        MainThread.Drain();
        if (Time.realtimeSinceStartup < next || failures > 5) return;
        next = Time.realtimeSinceStartup + 1f;
        try { AddIfMissing(); }
        catch (Exception e) { failures++; Plugin.Logger.LogWarning("menu button: " + e.Message); }
        if (Time.realtimeSinceStartup < nextIcon || iconFailures > 5) return;
        nextIcon = Time.realtimeSinceStartup + 3f;
        try { AddIconIfMissing(); }
        catch (Exception e) { iconFailures++; Plugin.Logger.LogWarning("top bar icon: " + e.Message); }
    }

    // In a career: a pencil button right after FM's Search button. FM nests the search
    // button in single-child holders (SearchButton, GlobalSearchTutorialAnchor); the icon
    // goes after the outermost holder, in the first row that lays its children out across.
    const string SearchButtonName = "dashboard-hud-search-button";
    void AddIconIfMissing()
    {
        if (panels == null) return;
        var open = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Panel>();
        panels.GetAllOpenPanels(open);
        foreach (var panel in open)
        {
            if (UQueryExtensions.Q(panel, IconName, (string)null) != null) return;
            var search = UQueryExtensions.Q(panel, SearchButtonName, (string)null);
            if (search == null) continue;
            VisualElement slot = search;
            while (slot.parent != null && !(slot.parent.resolvedStyle.flexDirection == FlexDirection.Row && slot.parent.childCount > 1))
                slot = slot.parent;
            var row = slot.parent;
            if (row == null) continue;
            if (Research.On) Describe(search);
            row.Insert(row.IndexOf(slot) + 1, BuildIcon(search));
            Plugin.Logger.LogInfo("Added the Live Editor icon to the top bar");
            return;
        }
    }

    static Texture2D pencil;
    static Texture2D Pencil()
    {
        if (pencil != null) return pencil;
        using var s = typeof(MenuButton).Assembly.GetManifestResourceStream("FM27LiveEditor.pencil64.png");
        if (s == null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
        return ImageConversion.LoadImage(tex, ms.ToArray()) ? pencil = tex : null;
    }

    static void CopyClasses(VisualElement from, VisualElement to, string skip = null)
    {
        var classes = new Il2CppSystem.Collections.Generic.List<string>(from.GetClasses());
        for (int i = 0; i < classes.Count; i++) if (classes[i] != skip) to.AddToClassList(classes[i]);
    }

    // A copy of FM's search button (its classes give the size, colour and hover), with the
    // pencil in place of the magnifier and no label.
    static VisualElement BuildIcon(VisualElement search)
    {
        var b = new Button { name = IconName, tooltip = "Open FM27 Live Editor" };
        CopyClasses(search, b);
        b.text = "";
        var image = UQueryExtensions.Q(search, "image", (string)null);
        var icon = new VisualElement();
        if (image != null) CopyClasses(image, icon, "nd-search");
        float size = image != null && image.layout.width > 4 ? image.layout.width : 16;
        icon.style.width = size; icon.style.height = size;
        var tex = Pencil();
        if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
        b.Add(icon);
        b.add_clicked((Il2CppSystem.Action)(System.Action)(() => Open(null)));
        return b;
    }

    // Research only: logs the elements around the Search label, to place the icon.
    static void Describe(VisualElement e)
    {
        static string One(VisualElement v)
        {
            var classes = new Il2CppSystem.Collections.Generic.List<string>(v.GetClasses());
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < classes.Count; i++) names.Add(classes[i]);
            var t = v.TryCast<TextElement>();
            return $"{v.GetType().Name} '{v.name}' [{string.Join(" ", names)}] children={v.childCount} dir={v.resolvedStyle.flexDirection} rect={v.layout}" + (t != null ? $" text='{t.text}'" : "");
        }
        var v = e;
        for (int depth = 0; v != null && depth < 6; depth++, v = v.parent)
        {
            Plugin.Logger.LogInfo($"ui up {depth}: {One(v)}");
            if (depth >= 1)
                for (int i = 0; i < v.childCount && i < 12; i++) Plugin.Logger.LogInfo($"ui   child {i}: {One(v[i])}");
        }
    }

    void AddIfMissing()
    {
        panels ??= FindObjectOfType<FM.Game.FMInitialiser>()?.m_panelManager;
        if (panels == null) return;
        var open = new Il2CppSystem.Collections.Generic.List<SI.Bindable.Panel>();
        panels.GetAllOpenPanels(open);
        foreach (var panel in open)
        {
            var bar = UQueryExtensions.Q(panel, "manager-button-container", (string)null);
            if (bar == null) continue;
            var mine = UQueryExtensions.Q(bar, ButtonName, (string)null);
            if (mine != null) { Match(bar, mine); continue; }
            var b = Build(Neighbour(bar));
            bar.Insert(0, b);
            Plugin.Logger.LogInfo("Added the Live Editor button to the main menu");
        }
    }

    // The app's logo, built into this DLL (logo64.png).
    static Texture2D Logo() => logo ??= Embedded("logo64.png");

    // The app's default "carbon" background, rendered by gen_menubg.py.
    static Texture2D backTexture;
    static Texture2D BackTexture() => backTexture ??= Embedded("menubg.png");

    static Texture2D Embedded(string file)
    {
        using var s = typeof(MenuButton).Assembly.GetManifestResourceStream("FM27LiveEditor." + file);
        if (s == null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
        return ImageConversion.LoadImage(tex, ms.ToArray()) ? tex : null;
    }

    // The last of FM's own buttons in the bar ("Manager Profiles"), which ours copies.
    static VisualElement Neighbour(VisualElement bar)
    {
        for (int i = bar.childCount - 1; i >= 0; i--)
        {
            var c = bar[i];
            if (c.name != ButtonName) return c;
        }
        return null;
    }

    // FM's chevron inside a button: the last element with a background image (its classes
    // carry the image and size, so a new element with the same classes looks the same).
    static VisualElement Chevron(VisualElement e)
    {
        if (e == null) return null;
        for (int i = e.childCount - 1; i >= 0; i--)
        {
            var found = Chevron(e[i]);
            if (found != null) return found;
        }
        bool image = e.resolvedStyle.backgroundImage.texture != null || e.resolvedStyle.backgroundImage.sprite != null
            || e.resolvedStyle.backgroundImage.vectorImage != null;
        return image && e.childCount == 0 && e.layout.width < 60 ? e : null;
    }

    // Keeps our button as tall as FM's (its layout is only known after a frame).
    static void Match(VisualElement bar, VisualElement mine)
    {
        var other = Neighbour(bar);
        float h = other?.layout.height ?? 0;
        if (h > 10 && Math.Abs(mine.layout.height - h) > 0.5f)
        {
            mine.style.height = h;
            mine.style.alignSelf = Align.Center;
            var icon = UQueryExtensions.Q(mine, "le-icon", (string)null);
            if (icon != null) icon.style.width = icon.style.height = Math.Max(16, h - 14);
        }
    }

    static void Corners(IStyle s, float r) => s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = r;
    static void Border(IStyle s, Color c) => s.borderTopColor = s.borderBottomColor = s.borderLeftColor = s.borderRightColor = c;

    static VisualElement Build(VisualElement neighbour)
    {
        var b = new Button { name = ButtonName, tooltip = "Edit the players in your career with FM27 Live Editor" };
        var s = b.style;
        s.flexDirection = FlexDirection.Row;
        s.alignItems = Align.Center;
        s.paddingLeft = 6; s.paddingRight = 10; s.paddingTop = 0; s.paddingBottom = 0;
        s.marginRight = 8;
        s.backgroundColor = Back;
        var back = BackTexture();
        if (back != null) s.backgroundImage = new StyleBackground(back);
        else s.backgroundColor = new Color(Back.r, Back.g, Back.b, 0.8f);
        Border(s, Rim);
        s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 1;
        Corners(s, 6);

        var icon = new VisualElement { name = "le-icon" };
        icon.style.width = 26; icon.style.height = 26; icon.style.marginRight = 8;
        var tex = Logo();
        if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
        else { icon.style.backgroundColor = Gold; Corners(icon.style, 7); }
        b.Add(icon);

        var text = new VisualElement();
        text.style.flexDirection = FlexDirection.Column;
        var small = new Label("FM27");
        small.style.color = Gold; small.style.fontSize = 9;
        small.style.unityFontStyleAndWeight = FontStyle.Bold;
        small.style.letterSpacing = 1.5f;
        small.style.marginBottom = -4;
        small.style.paddingLeft = small.style.paddingRight = small.style.paddingTop = small.style.paddingBottom = 0;
        var name = new Label("Live Editor");
        name.style.color = Color.white; name.style.fontSize = 14;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.paddingLeft = name.style.paddingRight = name.style.paddingTop = name.style.paddingBottom = 0;
        text.Add(small);
        text.Add(name);
        b.Add(text);

        // FM's own double chevron, tinted; a text one if it can't be found.
        VisualElement arrow;
        var theirs = Chevron(neighbour);
        if (theirs != null)
        {
            arrow = new VisualElement();
            var classes = new Il2CppSystem.Collections.Generic.List<string>(theirs.GetClasses());
            for (int i = 0; i < classes.Count; i++) arrow.AddToClassList(classes[i]);
            arrow.style.backgroundImage = theirs.resolvedStyle.backgroundImage;
            arrow.style.width = theirs.layout.width; arrow.style.height = theirs.layout.height;
            arrow.style.marginLeft = 12;
            arrow.style.unityBackgroundImageTintColor = Mint;
        }
        else
        {
            var label = new Label("»");
            label.style.color = Mint;
            label.style.fontSize = 22;
            label.style.marginLeft = 12;
            arrow = label;
        }
        void Tint(Color c) { if (arrow is Label l) l.style.color = c; else arrow.style.unityBackgroundImageTintColor = c; }
        b.Add(arrow);

        try
        {
            b.RegisterCallback<PointerEnterEvent>((EventCallback<PointerEnterEvent>)(System.Action<PointerEnterEvent>)(_ =>
            {
                b.style.backgroundColor = BackHover; Border(b.style, RimHover); Tint(Gold);
            }), TrickleDown.NoTrickleDown);
            b.RegisterCallback<PointerLeaveEvent>((EventCallback<PointerLeaveEvent>)(System.Action<PointerLeaveEvent>)(_ =>
            {
                b.style.backgroundColor = Back; Border(b.style, Rim); Tint(Mint);
            }), TrickleDown.NoTrickleDown);
        }
        catch (Exception e) { Plugin.Logger.LogWarning("menu button hover: " + e.Message); }

        b.add_clicked((Il2CppSystem.Action)(System.Action)(() => Open(name)));
        return b;
    }

    static void Open(Label name)
    {
        if (!File.Exists(Shortcut))
        {
            Plugin.Logger.LogWarning("The editor app hasn't been run yet, so there's no shortcut at " + Shortcut);
            if (name != null) name.text = "Open the app once first";
            return;
        }
        // Explorer opens the shortcut as an ordinary desktop app, outside FM's sandbox.
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Shortcut + "\"") { UseShellExecute = false }); }
        catch (Exception e) { Plugin.Logger.LogWarning("Couldn't open the editor app: " + e.Message); }
    }
}
