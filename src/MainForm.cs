// Part of BlocPlus; see BlocPlus.cs. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

partial class MainForm : Form
{
    const string App = "BlocPlus";
    static readonly string Version = Setup.Version;
    static readonly string[] EolNames = { "CRLF (Windows)", "LF (Unix)", "CR (Mac)" };
    static readonly string[] EolText = { "\r\n", "\n", "\r" };
    static readonly string[] ThemeNames = { Tr.S("Sistema"), Tr.S("Claro"), Tr.S("Oscuro") };

    readonly string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), App);
    readonly List<Doc> docs = new List<Doc>();
    readonly TabStrip strip = new TabStrip { AllowDrop = true };
    readonly Panel host = new Panel { Dock = DockStyle.Fill };
    readonly SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, Panel2Collapsed = true, SplitterWidth = 5 };
    readonly WebBrowser web = new WebBrowser
    {
        Dock = DockStyle.Fill, ScriptErrorsSuppressed = true, AllowWebBrowserDrop = false, WebBrowserShortcutsEnabled = false
    };
    readonly Timer previewTimer = new Timer { Interval = 300 };
    readonly ToolStripStatusLabel lPreview = new ToolStripStatusLabel { Visible = false, Padding = new Padding(10, 0, 10, 0), Margin = new Padding(4, 2, 4, 2) };
    ToolStripMenuItem mPreview;
    bool previewOn, previewSized;
    string previewShell; // identifies the page loaded for Markdown/RST/CSV, so edits only swap its body
    readonly MenuStrip menu = new MenuStrip();
    readonly StatusStrip status = new StatusStrip { SizingGrip = false };
    readonly ToolStripStatusLabel lPos = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft }, lLang = Lbl(), lZoom = Lbl(), lEol = Lbl(), lEnc = Lbl();
    readonly ToolStripStatusLabel lUpdate = new ToolStripStatusLabel { Visible = false, Padding = new Padding(10, 0, 10, 0) };
    readonly ContextMenuStrip ctx = new ContextMenuStrip();
    readonly List<string> recent = new List<string>();
    readonly Stack<string> closed = new Stack<string>();
    ToolStripMenuItem mWrap, mLines, mRecent, mEnc, mEol, mLang, mSpaces, mCompare;
    Font font = new Font("Consolas", 11f);
    int sel = -1, untitled, themeMode, tabSize = 4;
    bool wrap, lineNumbers = true, syntax = true, autoIndent = true, spaces, restoreSession = true, checkUpdates = true, sessionDirty, checking;
    bool showSpaces, autoClose, singleInstance = true;
    string lang = Tr.Lang; // saved to settings.ini; Tr reads it at startup
    string restartExe; // set when an update or install asks to relaunch after closing
    readonly FindBar find;

    string SessDir { get { return Path.Combine(dir, "session"); } }
    Doc Cur { get { return sel >= 0 && sel < docs.Count ? docs[sel] : null; } }
    Theme T { get { return themeMode == 2 || themeMode == 0 && SystemDark() ? Theme.Dark : Theme.Light; } }

    static ToolStripStatusLabel Lbl() { return new ToolStripStatusLabel { Padding = new Padding(10, 0, 10, 0) }; }

    static ToolStripMenuItem Item(string text, Keys keys, Action a)
    {
        var i = new ToolStripMenuItem(text);
        if (keys != Keys.None) i.ShortcutKeys = keys;
        i.Click += (s, e) => a();
        return i;
    }

    static bool SystemDark()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                return v is int && (int)v == 0;
            }
        }
        catch { return false; }
    }

    public MainForm(string[] args)
    {
        Text = App;
        Font = SystemFonts.MessageBoxFont;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Size = new Size(1000, 700);
        AllowDrop = true;
        DragEnter += OnDragEnter; DragDrop += OnDragDrop;
        strip.DragEnter += OnDragEnter; strip.DragDrop += OnDragDrop;
        strip.Docs = docs;
        strip.OnSelect = i => SwitchTo(docs[i]);
        strip.OnClose = i => CloseTab(docs[i]);
        strip.OnNew = () => SwitchTo(NewDoc());
        strip.OnContext = TabMenu;
        strip.OnReorder = (from, to) =>
        {
            if (docs[from].Pinned != docs[to].Pinned) return; // pinned tabs stay in front
            var d = docs[from];
            docs.RemoveAt(from);
            docs.Insert(to, d);
            SwitchTo(d);
        };

        BuildMenu();
        status.Items.AddRange(new ToolStripItem[] { lPos, lUpdate, lPreview, lLang, lZoom, lEol, lEnc });
        lPreview.Click += (s, e) => TogglePreview();
        lPreview.Font = new Font(lPreview.Font, FontStyle.Bold);
        lPreview.MouseEnter += (s, e) => { StylePreviewButton(true); status.Cursor = Cursors.Hand; };
        lPreview.MouseLeave += (s, e) => { StylePreviewButton(false); status.Cursor = Cursors.Default; };
        lUpdate.Click += (s, e) => { var r = lUpdate.Tag as Updater.Release; if (r != null) PromptUpdate(r); };
        foreach (var x in new[] { Tuple.Create(lLang, mLang), Tuple.Create(lEnc, mEnc), Tuple.Create(lEol, mEol) })
        {
            var p = x;
            p.Item1.Click += (s, e) => PopupFrom(p.Item2, p.Item1);
            p.Item1.MouseEnter += (s, e) => status.Cursor = Cursors.Hand;
            p.Item1.MouseLeave += (s, e) => status.Cursor = Cursors.Default;
        }
        ctx.Items.AddRange(new ToolStripItem[] {
            Item(Tr.S("Deshacer"), Keys.None, () => Cur.Box.Undo()), new ToolStripSeparator(),
            Item(Tr.S("Cortar"), Keys.None, () => CopySel(true)), Item(Tr.S("Copiar"), Keys.None, () => CopySel(false)),
            Item(Tr.S("Pegar"), Keys.None, PastePlain), Item(Tr.S("Eliminar"), Keys.None, () => Cur.Box.SelectedText = ""),
            new ToolStripSeparator(), Item(Tr.S("Seleccionar todo"), Keys.None, () => { Cur.Box.ClearExtra(); Cur.Box.SelectAll(); }) });
        split.Panel1.Controls.Add(host);
        find = new FindBar(this) { Changed = OnFindChanged };
        split.Panel1.Controls.Add(find);
        split.Panel2.Controls.Add(web);
        try { Native.CoInternetSetFeatureEnabled(21 /*FEATURE_DISABLE_NAVIGATION_SOUNDS*/, 2 /*SET_FEATURE_ON_PROCESS*/, true); } catch { }
        web.Navigating += OnPreviewNavigating;
        web.DocumentCompleted += (s, e) => SyncPreviewScroll();
        previewTimer.Tick += (s, e) => { previewTimer.Stop(); RenderPreview(); };
        Controls.Add(split); BuildResults(); Controls.Add(strip); Controls.Add(menu); Controls.Add(status);
        MainMenuStrip = menu;

        try { LoadSettings(); } catch (Exception ex) { Log.Write("settings", ex); }
        mWrap.Checked = wrap;
        mLines.Checked = lineNumbers;
        mSpaces.Checked = showSpaces;
        mPreview.Checked = previewOn;
        if (restoreSession) RestoreSession();
        foreach (var a in args) OpenArg(a);
        if (docs.Count == 0) NewDoc();
        SwitchTo(Cur ?? docs[0]);
        ApplyThemeAll();

        // Crash protection: unsaved work also lands in the session every 30 s.
        var timer = new Timer { Interval = 30000 };
        timer.Tick += (s, e) => { if (sessionDirty && restoreSession) try { SaveSession(); } catch (Exception ex) { Log.Write("autosave", ex); } };
        timer.Start();
        UserPreferenceChangedEventHandler pref = (s, e) => { if (themeMode == 0 && IsHandleCreated) BeginInvoke((Action)ApplyThemeAll); };
        SystemEvents.UserPreferenceChanged += pref;
        FormClosed += (s, e) =>
        {
            SystemEvents.UserPreferenceChanged -= pref;
            // After the session is saved. This window still owns the instance pipe, so the new one must not hand off to it.
            if (restartExe != null) Process.Start(restartExe, "--new-instance");
        };
        Shown += (s, e) =>
        {
            Cur.Box.Focus();
            Cur.Box.ScrollToCaret();
            Highlight(Cur);
            previewShell = null; // the page set before the browser had a handle never loaded
            RenderPreview();
            if (checkUpdates && !Setup.Packaged) CheckUpdates(false);
            if (singleInstance)
                Instance.Listen(a =>
                {
                    try { BeginInvoke((Action)(() => FromOtherInstance(a))); }
                    catch (InvalidOperationException) { } // closing
                });
        };
        Activated += (s, e) => CheckExternal();
        FormClosing += OnClosing;
    }

    void BuildMenu()
    {
        mRecent = new ToolStripMenuItem(Tr.S("Abrir &reciente"));
        mRecent.DropDownItems.Add(Tr.S("(vacío)")); // placeholder so DropDownOpening fires
        mRecent.DropDownOpening += (s, e) => FillRecent();
        var settings = Item(Tr.S("A&justes..."), Keys.Control | Keys.Oemcomma, () => ShowSettings());
        settings.ShortcutKeyDisplayString = "Ctrl+,";
        var exit = Item(Tr.S("&Salir"), Keys.None, Close);
        exit.ShortcutKeyDisplayString = "Alt+F4";
        var file = new ToolStripMenuItem(Tr.S("&Archivo"));
        file.DropDownItems.AddRange(new ToolStripItem[] {
            Item(Tr.S("&Nueva pestaña"), Keys.Control | Keys.T, () => SwitchTo(NewDoc())),
            Item(Tr.S("&Abrir..."), Keys.Control | Keys.O, OpenDialog),
            Item(Tr.S("Abrir desde &URL..."), Keys.Control | Keys.Shift | Keys.O, OpenUrlDialog),
            mRecent,
            Item(Tr.S("Reabrir pestaña &cerrada"), Keys.Control | Keys.Shift | Keys.T, ReopenClosed),
            Item(Tr.S("Recargar desde disco"), Keys.None, () => Reload(Cur, true)),
            new ToolStripSeparator(),
            Item(Tr.S("&Guardar"), Keys.Control | Keys.S, () => Save(Cur, false)),
            Item(Tr.S("Guardar &como..."), Keys.Control | Keys.Shift | Keys.S, () => Save(Cur, true)),
            Item(Tr.S("Guardar t&odo"), Keys.Control | Keys.Alt | Keys.S, SaveAll),
            new ToolStripSeparator(),
            Item(Tr.S("&Imprimir..."), Keys.Control | Keys.P, PrintDoc),
            new ToolStripSeparator(),
            Item(Tr.S("Cerrar &pestaña"), Keys.Control | Keys.W, () => CloseTab(Cur)),
            new ToolStripSeparator(),
            settings,
            exit });

        var edit = new ToolStripMenuItem(Tr.S("&Editar"));
        edit.DropDownItems.AddRange(new ToolStripItem[] {
            Item(Tr.S("&Deshacer"), Keys.Control | Keys.Z, () => Cur.Box.Undo()),
            Item(Tr.S("&Rehacer"), Keys.Control | Keys.Y, () => Cur.Box.Redo()),
            new ToolStripSeparator(),
            Item(Tr.S("Cor&tar"), Keys.Control | Keys.X, () => CopySel(true)),
            Item(Tr.S("&Copiar"), Keys.Control | Keys.C, () => CopySel(false)),
            Item(Tr.S("&Pegar"), Keys.Control | Keys.V, PastePlain),
            Item(Tr.S("Seleccionar &todo"), Keys.Control | Keys.A, () => { Cur.Box.ClearExtra(); Cur.Box.SelectAll(); }),
            new ToolStripSeparator(),
            Item(Tr.S("&Buscar..."), Keys.Control | Keys.F, () => ShowFind(false)),
            Item(Tr.S("Buscar &siguiente"), Keys.F3, () => FindNext(false)),
            Item(Tr.S("Buscar &anterior"), Keys.Shift | Keys.F3, () => FindNext(true)),
            Item(Tr.S("R&eemplazar..."), Keys.Control | Keys.H, () => ShowFind(true)),
            Item(Tr.S("Buscar en todas las pestañas"), Keys.Control | Keys.Shift | Keys.F, () => { ShowFind(false); FindInTabs(); }),
            Item(Tr.S("Buscar en una carpeta..."), Keys.None, () => { ShowFind(false); FindInFolder(); }),
            Item(Tr.S("&Ir a línea..."), Keys.Control | Keys.G, GoToLine),
            new ToolStripSeparator(),
            Item(Tr.S("D&uplicar línea"), Keys.Control | Keys.D, Duplicate),
            Item(Tr.S("Eliminar línea"), Keys.Control | Keys.Shift | Keys.K, DeleteLine),
            Item(Tr.S("Subir línea"), Keys.Alt | Keys.Up, () => MoveLine(-1)),
            Item(Tr.S("Bajar línea"), Keys.Alt | Keys.Down, () => MoveLine(1)),
            new ToolStripSeparator(),
            Item(Tr.S("MAYÚSCULAS"), Keys.Control | Keys.Shift | Keys.U, () => ChangeCase(true)),
            Item(Tr.S("minúsculas"), Keys.Control | Keys.U, () => ChangeCase(false)),
            Item(Tr.S("Ordenar líneas"), Keys.None, SortLines),
            Item(Tr.S("Quitar espacios finales"), Keys.None, TrimTrailing),
            Item(Tr.S("Insertar fecha y hora"), Keys.F5, () => Cur.Box.SelectedText = DateTime.Now.ToShortTimeString() + " " + DateTime.Now.ToShortDateString()) });

        mLang = new ToolStripMenuItem(Tr.S("&Lenguaje"));
        mLang.DropDownItems.Add(Item(Tr.S("Texto plano"), Keys.None, () => SetLang(Cur, null)));
        foreach (var l in Lang.All)
        {
            var lang = l;
            mLang.DropDownItems.Add(Item(lang.Name, Keys.None, () => SetLang(Cur, lang)));
        }
        mLang.DropDownOpening += (s, e) => CheckOnly(mLang, Cur.Lang == null ? 0 : Array.IndexOf(Lang.All, Cur.Lang) + 1);
        mEnc = new ToolStripMenuItem(Tr.S("&Codificación"));
        for (int i = 0; i < Enc.Names.Length; i++)
        {
            int k = i;
            mEnc.DropDownItems.Add(Item(Enc.Names[i], Keys.None, () => { Cur.Enc = k; MarkChanged(Cur); }));
        }
        mEnc.DropDownOpening += (s, e) => CheckOnly(mEnc, Cur.Enc);
        mEol = new ToolStripMenuItem(Tr.S("&Fin de línea"));
        for (int i = 0; i < EolNames.Length; i++)
        {
            int k = i;
            mEol.DropDownItems.Add(Item(EolNames[i], Keys.None, () => { Cur.Eol = k; MarkChanged(Cur); }));
        }
        mEol.DropDownOpening += (s, e) => CheckOnly(mEol, Cur.Eol);
        mCompare = new ToolStripMenuItem(Tr.S("&Comparar con"));
        mCompare.DropDownItems.Add(Tr.S("(vacío)"));
        mCompare.DropDownOpening += (s, e) => FillCompare();
        var docMenu = new ToolStripMenuItem(Tr.S("&Documento"));
        docMenu.DropDownItems.AddRange(new ToolStripItem[] { mLang, mEnc, mEol, new ToolStripSeparator(),
            Item(Tr.S("&Formatear JSON o XML"), Keys.Control | Keys.Alt | Keys.F, () => Reformat(true)),
            Item(Tr.S("Co&mpactar JSON o XML"), Keys.None, () => Reformat(false)),
            mCompare });

        mWrap = Item(Tr.S("A&juste de línea"), Keys.Alt | Keys.Z, () => { wrap = !wrap; ApplyEditorSettings(); });
        mLines = Item(Tr.S("&Números de línea"), Keys.None, () => { lineNumbers = !lineNumbers; ApplyEditorSettings(); });
        mSpaces = Item(Tr.S("Mostrar &espacios y tabulaciones"), Keys.None, () => { showSpaces = !showSpaces; ApplyEditorSettings(); });
        mPreview = Item(Tr.S("&Vista previa"), Keys.Control | Keys.Shift | Keys.V, TogglePreview);
        var palette = Item(Tr.S("&Paleta de comandos..."), Keys.Control | Keys.Shift | Keys.P, ShowPalette);
        // Alt+arrows, like Alt+Up/Down for moving lines: bracket keys sit in different places on each keyboard layout.
        var fold = Item(Tr.S("Plegar bloque"), Keys.Alt | Keys.Left, () => FoldAt(Cur, Cur.LineOf(Cur.Box.SelectionStart), true));
        var unfold = Item(Tr.S("Desplegar bloque"), Keys.Alt | Keys.Right, () => FoldAt(Cur, Cur.LineOf(Cur.Box.SelectionStart), false));
        var zin = Item(Tr.S("Acercar"), Keys.Control | Keys.Oemplus, () => Zoom(0.1f));
        var zout = Item(Tr.S("Alejar"), Keys.Control | Keys.OemMinus, () => Zoom(-0.1f));
        zin.ShortcutKeyDisplayString = "Ctrl++";
        zout.ShortcutKeyDisplayString = "Ctrl+-";
        var view = new ToolStripMenuItem(Tr.S("&Ver"));
        view.DropDownItems.AddRange(new ToolStripItem[] {
            mWrap, mLines, mSpaces, mPreview, new ToolStripSeparator(),
            fold, unfold, Item(Tr.S("Desplegar todo"), Keys.None, () => UnfoldAll(Cur)), new ToolStripSeparator(),
            zin, zout, Item(Tr.S("Restablecer zoom"), Keys.Control | Keys.D0, () => Zoom(0)), new ToolStripSeparator(),
            palette });

        var help = new ToolStripMenuItem(Tr.S("Ay&uda"));
        help.DropDownItems.Add(Item(Tr.S("&Buscar actualizaciones"), Keys.None, () =>
        {
            if (Setup.Packaged) Process.Start("ms-windows-store://downloadsandupdates");
            else CheckUpdates(true);
        }));
        if (Setup.RunningInstalled || Setup.Packaged) help.DropDownItems.Add(Item(Tr.S("Usar como editor &predeterminado..."), Keys.None, Setup.OpenDefaultApps));
        else help.DropDownItems.Add(Item(Tr.S("&Instalar en este equipo..."), Keys.None, () => { if (Setup.InstallInteractive()) { restartExe = Setup.Exe; Close(); } }));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(Item(Tr.S("&Acerca de {0} {1}", App, Version), Keys.None, () => ShowSettings()));

        menu.Items.AddRange(new ToolStripItem[] { file, edit, docMenu, view, help });
    }

    static void CheckOnly(ToolStripMenuItem m, int idx)
    {
        for (int i = 0; i < m.DropDownItems.Count; i++) ((ToolStripMenuItem)m.DropDownItems[i]).Checked = i == idx;
    }

    // ---------- documents ----------

    Doc NewDoc()
    {
        var d = new Doc { Untitled = ++untitled };
        d.Box = new MultiBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, AcceptsTab = true, DetectUrls = false,
            HideSelection = false, WordWrap = wrap, Font = font, EnableAutoDragDrop = false, AllowDrop = true,
            ScrollBars = RichTextBoxScrollBars.Both, ContextMenuStrip = ctx
        };
        d.Box.Source = () => d.Text;
        d.Box.ShowSpaces = showSpaces;
        d.Gut = new Gutter { Dock = DockStyle.Left, D = d, Width = 40, Visible = lineNumbers, FoldState = k => FoldState(d, k), OnFold = k => FoldAt(d, k, FoldState(d, k) == 1) };
        d.Page = new Panel { Dock = DockStyle.Fill, Visible = false };
        d.Page.Controls.Add(d.Box);
        d.Page.Controls.Add(d.Gut);
        host.Controls.Add(d.Page);
        docs.Add(d);
        d.Box.HandleCreated += (s, e) =>
        {
            Native.SendMessage(d.Box.Handle, 0xD3 /*EM_SETMARGINS*/, (IntPtr)1, (IntPtr)6);
            IntPtr p;
            Native.SendMessage(d.Box.Handle, 0x43C /*EM_GETOLEINTERFACE*/, IntPtr.Zero, out p);
            if (p != IntPtr.Zero) { d.Tom = Marshal.GetObjectForIUnknown(p); Marshal.Release(p); }
            ApplyTabs(d);
        };
        var h = d.Box.Handle; // create now so Modified/ClearUndo apply to the real control

        d.Box.TextChanged += (s, e) =>
        {
            if (d.Busy) return;
            if (!d.Box.Editing) d.Box.ClearExtra();
            d.Invalidate();
            d.Gut.Invalidate();
            sessionDirty = true;
            UpdateTitle(d);
            if (d == Cur) { UpdateStatus(); QueueHighlight(d); if (!split.Panel2Collapsed) { previewTimer.Stop(); previewTimer.Start(); } }
        };
        d.Box.SelectionChanged += (s, e) => { if (d == Cur && !d.Busy) { UpdateStatus(); UpdateMarks(d); } };
        d.Box.VScroll += (s, e) => { d.Gut.Invalidate(); QueueHighlight(d); if (d == Cur) { SyncPreviewScroll(); UpdateMarks(d); } };
        d.Box.Resize += (s, e) => { d.Gut.Invalidate(); QueueHighlight(d); };
        d.Box.MouseWheel += (s, e) =>
        {
            if ((ModifierKeys & Keys.Control) != 0) BeginInvoke((Action)(() => { d.Gut.Invalidate(); UpdateStatus(); QueueHighlight(d); }));
        };
        d.Box.KeyDown += BoxKeyDown;
        d.Box.KeyPress += BoxKeyPress;
        d.Box.DragEnter += OnDragEnter;
        d.Box.DragDrop += OnDragDrop;
        ApplyTheme(d, T);
        UpdateTitle(d);
        return d;
    }

    void SwitchTo(Doc d)
    {
        sel = docs.IndexOf(d);
        strip.Selected = sel;
        foreach (var x in docs) x.Page.Visible = x == d;
        strip.Invalidate();
        UpdateTitle(d);
        UpdateStatus();
        d.Gut.Invalidate();
        QueueHighlight(d);
        RenderPreview();
        if (Visible) d.Box.Focus();
    }

    void BoxKeyDown(object sender, KeyEventArgs e)
    {
        var b = (RichTextBox)sender;
        var d = Cur;
        var k = e.KeyCode;
        if (b == d.Box && d.Box.Extra.Count > 0 && MultiKey(d, e)) { e.SuppressKeyPress = true; return; }
        if (k == Keys.Escape && find.Visible) { CloseFind(); return; }
        if (k == Keys.Back && !e.Control && autoClose && b.SelectionLength == 0 && ClosePairAround(d)) { e.SuppressKeyPress = true; return; }
        if (k == Keys.Enter && !e.Alt)
        {
            // Plain newline (no RTF soft breaks), keeping the current line's indentation.
            e.SuppressKeyPress = true;
            b.SelectedText = "\n" + (autoIndent ? IndentAt(d.Text, b.SelectionStart) : "");
        }
        else if (k == Keys.Tab && !e.Control && !e.Alt)
        {
            e.SuppressKeyPress = true;
            string unit = spaces ? new string(' ', tabSize) : "\t";
            bool multi = b.SelectionLength > 0 && d.Text.IndexOf('\n', b.SelectionStart, b.SelectionLength) >= 0;
            if (!multi && !e.Shift) { b.SelectedText = unit; return; }
            int a, en;
            LineRange(d, out a, out en);
            var lines = d.Text.Substring(a, en - a).Split('\n');
            for (int i = 0; i < lines.Length; i++)
                lines[i] = e.Shift ? Outdent(lines[i]) : lines[i].Length > 0 ? unit + lines[i] : lines[i];
            string r = string.Join("\n", lines);
            Replace(d, a, en, r);
            b.Select(a, r.Length);
        }
        else if (e.Shift && !e.Control && k == Keys.Insert) { e.SuppressKeyPress = true; PastePlain(); }
        else if (e.Control && e.Shift && !e.Alt && k == Keys.Z) { e.SuppressKeyPress = true; b.Redo(); }
        else if (e.Control && !e.Alt && ((k >= Keys.A && k <= Keys.Z) || (k >= Keys.D0 && k <= Keys.D9) || (k >= Keys.OemSemicolon && k <= Keys.OemBackslash)))
            e.SuppressKeyPress = true; // swallow RichEdit formatting hotkeys (Ctrl+L/E/R/J/1/2/5, Ctrl+B/I, ...)
    }

    // Leading whitespace of the line containing p, up to p.
    static string IndentAt(string t, int p)
    {
        int ls = p == 0 ? 0 : t.LastIndexOf('\n', p - 1) + 1, i = ls;
        while (i < p && (t[i] == ' ' || t[i] == '\t')) i++;
        return t.Substring(ls, i - ls);
    }

    // ---------- multiple cursors ----------

    // All selections as {anchor, caret}, the RichTextBox's own first.
    static List<int[]> Sels(Doc d)
    {
        int a = d.Box.SelectionStart, e = a + d.Box.SelectionLength;
        if (d.Tom != null && (d.Tom.Selection.Flags & 1 /*tomSelStartActive*/) != 0) { int x = a; a = e; e = x; }
        var l = new List<int[]> { new[] { a, e } };
        l.AddRange(d.Box.Extra.Select(x => (int[])x.Clone()));
        return l;
    }

    static void SetSels(Doc d, List<int[]> l)
    {
        var keep = new List<int[]>();
        foreach (var x in l) if (!keep.Any(y => MultiBox.Collide(x, y))) keep.Add(x);
        d.Box.Extra = keep.Skip(1).ToList();
        var p = keep[0];
        if (d.Tom != null) d.Tom.Selection.SetRange(p[0], p[1]);
        else d.Box.Select(Math.Min(p[0], p[1]), Math.Abs(p[1] - p[0]));
        d.Box.ScrollToCaret();
        d.Box.Invalidate();
    }

    // f(text, start, end) gives {from, to, insert} for each selection, in document order; carets land after
    // each insert. Everything goes in as one replacement so a single Ctrl+Z undoes it.
    void MultiEdit(Doc d, Func<string, int, int, Tuple<int, int, string>> f)
    {
        string t = d.Text;
        var sels = Sels(d);
        var res = new List<int[]>(sels);
        var sb = new StringBuilder();
        int lo = -1, prev = 0;
        foreach (int i in Enumerable.Range(0, sels.Count).OrderBy(i => Math.Min(sels[i][0], sels[i][1])))
        {
            var s = sels[i];
            var r = f(t, Math.Min(s[0], s[1]), Math.Max(s[0], s[1]));
            int a = Math.Max(r.Item1, prev), b = Math.Max(r.Item2, a);
            if (lo < 0) lo = prev = a;
            sb.Append(t, prev, a - prev).Append(r.Item3);
            int c = lo + sb.Length;
            res[i] = new[] { c, c };
            prev = b;
        }
        string span = sb.ToString();
        if (span != t.Substring(lo, prev - lo))
        {
            d.Box.Editing = true;
            try { if (d.Tom != null) d.Tom.Range(lo, prev).Text = span; else Replace(d, lo, prev, span); }
            finally { d.Box.Editing = false; }
        }
        SetSels(d, res);
    }

    void MultiMove(Doc d, Keys k, bool shift)
    {
        string t = d.Text;
        var st = d.Starts;
        var l = Sels(d);
        foreach (var s in l)
        {
            int c = s[1], lo = Math.Min(s[0], c), hi = Math.Max(s[0], c), line = d.LineOf(c), ls = st[line];
            int le = line + 1 < st.Length ? st[line + 1] - 1 : t.Length;
            if (k == Keys.Left) c = !shift && lo < hi ? lo : Math.Max(0, c - 1);
            else if (k == Keys.Right) c = !shift && lo < hi ? hi : Math.Min(t.Length, c + 1);
            else if (k == Keys.End) c = le;
            else if (k == Keys.Home)
            {
                int i = ls;
                while (i < le && (t[i] == ' ' || t[i] == '\t')) i++;
                c = c == i ? ls : i;
            }
            else
            {
                int n = line + (k == Keys.Up ? -1 : 1);
                c = n < 0 ? 0 : n >= st.Length ? t.Length : Math.Min(st[n] + c - ls, n + 1 < st.Length ? st[n + 1] - 1 : t.Length);
            }
            s[1] = c;
            if (!shift) s[0] = c;
        }
        SetSels(d, l);
    }

    // Keys while there are extra cursors; false lets BoxKeyDown/RichEdit handle it.
    bool MultiKey(Doc d, KeyEventArgs e)
    {
        var k = e.KeyCode;
        if (k == Keys.Escape) d.Box.ClearExtra();
        else if (e.Alt || k == Keys.ControlKey || k == Keys.ShiftKey) return false;
        else if (e.Control || k == Keys.PageUp || k == Keys.PageDown) { d.Box.ClearExtra(); return false; }
        else if (k == Keys.Back) MultiEdit(d, (t, s, en) => Tuple.Create(s == en ? Math.Max(0, s - 1) : s, en, ""));
        else if (k == Keys.Delete) MultiEdit(d, (t, s, en) => Tuple.Create(s, s == en ? Math.Min(t.Length, en + 1) : en, ""));
        else if (k == Keys.Enter) MultiEdit(d, (t, s, en) => Tuple.Create(s, en, "\n" + (autoIndent ? IndentAt(t, s) : "")));
        else if (k == Keys.Tab) { if (!e.Shift) MultiEdit(d, (t, s, en) => Tuple.Create(s, en, spaces ? new string(' ', tabSize) : "\t")); }
        else if (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || k == Keys.Home || k == Keys.End) MultiMove(d, k, e.Shift);
        else return false;
        return true;
    }

    void BoxKeyPress(object sender, KeyPressEventArgs e)
    {
        var d = Cur;
        if (sender != d.Box || e.KeyChar < ' ' || e.KeyChar == '\x7f') return;
        if (d.Box.Extra.Count == 0) { if (autoClose && AutoPair(d, e.KeyChar)) e.Handled = true; return; }
        e.Handled = true;
        string c = e.KeyChar.ToString();
        MultiEdit(d, (t, s, en) => Tuple.Create(s, en, c));
    }

    void CopySel(bool cut)
    {
        var d = Cur;
        if (d.Box.Extra.Count == 0) { if (cut) d.Box.Cut(); else d.Box.Copy(); return; }
        string t = d.Text;
        var parts = Sels(d).OrderBy(s => Math.Min(s[0], s[1])).Select(s => t.Substring(Math.Min(s[0], s[1]), Math.Abs(s[1] - s[0]))).ToList();
        if (parts.All(x => x.Length == 0)) return;
        try { Clipboard.SetText(string.Join("\r\n", parts)); } catch (ExternalException) { return; }
        if (cut) MultiEdit(d, (x, s, en) => Tuple.Create(s, en, ""));
    }

    string Outdent(string s)
    {
        if (s.StartsWith("\t")) return s.Substring(1);
        int n = 0;
        while (n < tabSize && n < s.Length && s[n] == ' ') n++;
        return s.Substring(n);
    }

    void UpdateTitle(Doc d)
    {
        strip.Invalidate();
        if (d != Cur) return;
        string t = (d.Dirty ? "*" : "") + d.Title + " - " + App;
        if (Text != t) Text = t;
    }

    void UpdateStatus()
    {
        var d = Cur;
        if (d == null) return;
        var b = d.Box;
        int p = b.SelectionStart, line, col;
        if (!b.WordWrap && !d.Folded) { line = b.GetLineFromCharIndex(p); col = p - b.GetFirstCharIndexFromLine(line); }
        else { line = d.LineOf(p); col = p - d.Starts[line]; }
        lPos.Text = b.SelectionLength > 0 ? Tr.S("Lín {0}, Col {1}   ({2} seleccionados)", line + 1, col + 1, b.SelectionLength) : Tr.S("Lín {0}, Col {1}", line + 1, col + 1);
        lLang.Text = d.Lang == null ? Tr.S("Texto plano") : d.Lang.Name;
        lZoom.Text = (int)Math.Round(b.ZoomFactor * 100) + "%";
        lEol.Text = EolNames[d.Eol];
        lEnc.Text = Enc.Names[d.Enc];
    }

    void MarkChanged(Doc d)
    {
        d.Box.Modified = true;
        sessionDirty = true;
        UpdateTitle(d);
        UpdateStatus();
    }

    static string Normalize(string s) { return s.Replace("\r\n", "\n").Replace('\r', '\n'); }

    static int DetectEol(string s)
    {
        int i = s.IndexOfAny(new[] { '\r', '\n' });
        if (i < 0 || s[i] == '\r' && i + 1 < s.Length && s[i + 1] == '\n') return 0;
        return s[i] == '\n' ? 1 : 2;
    }

    void SetText(Doc d, string s)
    {
        bool folded = d.Folded;
        d.Box.Text = s;
        if (folded && d.Tom != null) Silently(d, () => d.Tom.Range(0, d.Box.TextLength).Font.Hidden = 0);
        d.Box.ClearUndo();
        d.Box.Modified = false;
        d.Box.Select(0, 0);
        d.Invalidate();
        d.Colored = d.Folded = false;
        UpdateTitle(d);
        QueueHighlight(d);
    }

    void LoadInto(Doc d, string path, byte[] bytes)
    {
        int enc;
        string s = Enc.Decode(bytes, out enc);
        d.FilePath = path;
        d.Enc = enc;
        d.Eol = DetectEol(s);
        d.Lang = Lang.Detect(path);
        SetText(d, Normalize(s));
        d.Stamp = File.GetLastWriteTimeUtc(path);
    }

    void OpenPath(string path)
    {
        path = Path.GetFullPath(path);
        foreach (var x in docs)
            if (string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase)) { SwitchTo(x); return; }
        byte[] bytes;
        try
        {
            long size = new FileInfo(path).Length;
            if (size > 50L << 20 && !Confirm(Tr.S("{0} ocupa {1} MB y puede tardar en abrirse. ¿Abrirlo de todos modos?", Path.GetFileName(path), size >> 20))) return;
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) { Err(ex); return; }
        // RichEdit takes a while to swallow tens of MB; say so instead of looking frozen.
        bool big = bytes.Length > 5 << 20;
        if (big) { lPos.Text = Tr.S("Abriendo {0}...", Path.GetFileName(path)); status.Refresh(); Cursor.Current = Cursors.WaitCursor; }
        var d = BlankOrNew();
        LoadInto(d, path, bytes);
        AddRecent(path);
        SwitchTo(d);
        if (big) Cursor.Current = Cursors.Default;
    }

    // A command line argument: a file, "file:line" or "file:line:column", or a URL.
    void OpenArg(string a)
    {
        if (IsUrl(a)) { OpenUrl(a); return; }
        int line = 0, col = 0;
        var m = Regex.Match(a, @"^(.+?):(\d+)(?::(\d+))?$");
        if (!File.Exists(a) && m.Success && File.Exists(m.Groups[1].Value))
        {
            a = m.Groups[1].Value;
            int.TryParse(m.Groups[2].Value, out line);
            int.TryParse(m.Groups[3].Value, out col);
        }
        if (!File.Exists(a)) return;
        OpenPath(a);
        if (line > 0) GoTo(Cur, line, col);
    }

    // Files sent by a BlocPlus started while this one was running; nothing means "a new tab".
    void FromOtherInstance(string[] args)
    {
        if (args.Length == 0) SwitchTo(BlankOrNew());
        foreach (var a in args) OpenArg(a);
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    Doc BlankOrNew()
    {
        var c = Cur;
        return c != null && c.FilePath == null && c.Url == null && !c.Dirty && c.Box.TextLength == 0 ? c : NewDoc(); // reuse a blank tab
    }

    static bool IsUrl(string s) { return Regex.IsMatch(s, @"^https?://\S+$", RegexOptions.IgnoreCase); }

    void OpenUrlDialog()
    {
        string clip = "";
        try { clip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : ""; } catch { }
        string url = Prompt(Tr.S("Abrir desde URL"), Tr.S("Dirección del archivo de texto:"), IsUrl(clip) ? clip : "https://");
        if (url != null && IsUrl(url.Trim())) OpenUrl(url.Trim());
    }

    // Opens the text at a URL in an unsaved tab; saving asks where to put it.
    void OpenUrl(string url)
    {
        // A GitHub file page is HTML; the file itself is on raw.githubusercontent.com.
        url = Regex.Replace(url, @"^https?://github\.com/([^/]+/[^/]+)/blob/", "https://raw.githubusercontent.com/$1/", RegexOptions.IgnoreCase);
        foreach (var x in docs)
            if (x.Url == url) { SwitchTo(x); return; }
        byte[] bytes;
        Cursor = Cursors.WaitCursor;
        try
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = App;
                using (var r = wc.OpenRead(url))
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[81920];
                    for (int k; (k = r.Read(buf, 0, buf.Length)) > 0; )
                    {
                        ms.Write(buf, 0, k);
                        if (ms.Length > 20 << 20) throw new InvalidDataException(Tr.S("El archivo es demasiado grande (máximo {0} MB).", 20));
                    }
                    bytes = ms.ToArray();
                }
            }
        }
        catch (Exception ex) { Err(ex); return; }
        finally { Cursor = Cursors.Default; }
        int enc;
        string s = Enc.Decode(bytes, out enc);
        if (s.IndexOf('\0') >= 0) { Err(new InvalidDataException(Tr.S("No es un archivo de texto."))); return; }
        var d = BlankOrNew();
        d.Url = url;
        d.Enc = enc;
        d.Eol = DetectEol(s);
        d.Lang = Lang.Detect(new Uri(url).LocalPath);
        SetText(d, Normalize(s));
        SwitchTo(d);
    }

    void OpenDialog()
    {
        using (var o = new OpenFileDialog { Multiselect = true, Filter = Tr.S("Todos los archivos (*.*)|*.*|Texto (*.txt)|*.txt") })
        {
            if (Cur.FilePath != null) o.InitialDirectory = Path.GetDirectoryName(Cur.FilePath);
            if (o.ShowDialog(this) == DialogResult.OK) foreach (var f in o.FileNames) OpenPath(f);
        }
    }

    void Reload(Doc d, bool ask)
    {
        if (d.FilePath == null) return;
        if (ask && d.Dirty && !Confirm(Tr.S("¿Descartar los cambios de {0} y recargar desde disco?", d.Title))) return;
        int p = d.Box.SelectionStart;
        try { LoadInto(d, d.FilePath, File.ReadAllBytes(d.FilePath)); }
        catch (Exception ex) { Err(ex); return; }
        d.Box.Select(Math.Min(p, d.Box.TextLength), 0);
        d.Box.ScrollToCaret();
        if (d == Cur) UpdateStatus();
    }

    // Picks up edits made by other programs when the window regains focus.
    void CheckExternal()
    {
        if (checking) return;
        checking = true;
        try
        {
            foreach (var d in docs.ToList())
            {
                if (d.FilePath == null || !File.Exists(d.FilePath)) continue;
                var t = File.GetLastWriteTimeUtc(d.FilePath);
                if (t == d.Stamp) continue;
                d.Stamp = t;
                if (!d.Dirty || Confirm(Tr.S("{0} ha cambiado fuera del editor.\n¿Recargarlo y perder tus cambios?", d.Title))) Reload(d, false);
            }
        }
        finally { checking = false; }
    }

    bool Save(Doc d, bool saveAs)
    {
        string path = d.FilePath;
        if (path == null || saveAs)
        {
            using (var s = new SaveFileDialog { Filter = Tr.S("Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*"), FileName = path != null ? Path.GetFileName(path) : d.Url != null ? d.Title : "" })
            {
                if (path != null) s.InitialDirectory = Path.GetDirectoryName(path);
                if (s.ShowDialog(this) != DialogResult.OK) return false;
                path = s.FileName;
            }
        }
        string text = d.Eol == 1 ? d.Text : d.Text.Replace("\n", EolText[d.Eol]);
        var e = Enc.Get(d.Enc);
        byte[] body = e.GetBytes(text);
        if (d.Enc == 4 && e.GetString(body) != text &&
            Confirm(Tr.S("Algunos caracteres no existen en ANSI y se perderían.\n¿Guardar en UTF-8 en su lugar?")))
        {
            d.Enc = 0;
            e = Enc.Get(0);
            body = e.GetBytes(text);
        }
        try
        {
            // Write to a temp file first so a failed write never truncates the original.
            byte[] pre = e.GetPreamble(), all = new byte[pre.Length + body.Length];
            Buffer.BlockCopy(pre, 0, all, 0, pre.Length);
            Buffer.BlockCopy(body, 0, all, pre.Length, body.Length);
            string tmp = path + ".blocplus.tmp";
            File.WriteAllBytes(tmp, all);
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }
        catch (Exception ex) { Err(ex); return false; }
        if (path != d.FilePath) { d.FilePath = path; SetLang(d, Lang.Detect(path)); }
        d.Box.Modified = false;
        d.Stamp = File.GetLastWriteTimeUtc(path);
        sessionDirty = true;
        AddRecent(path);
        UpdateTitle(d);
        UpdateStatus();
        return true;
    }

    void SaveAll()
    {
        foreach (var d in docs.ToList())
            if (d.Dirty) { SwitchTo(d); if (!Save(d, false)) return; }
    }

    bool ConfirmClose(Doc d)
    {
        if (!(d.Dirty || d.FilePath == null) || d.Box.TextLength == 0) return true;
        SwitchTo(d);
        var r = MessageBox.Show(this, Tr.S("¿Guardar los cambios de {0}?", d.Title), App, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return r == DialogResult.No || r == DialogResult.Yes && Save(d, false);
    }

    bool CloseTab(Doc d)
    {
        var cur = Cur;
        if (!ConfirmClose(d)) return false;
        if (d.FilePath != null) closed.Push(d.FilePath);
        int i = docs.IndexOf(d);
        docs.Remove(d);
        host.Controls.Remove(d.Page);
        d.Page.Dispose();
        sessionDirty = true;
        if (docs.Count == 0) SwitchTo(NewDoc());
        else SwitchTo(cur != d ? cur : docs[Math.Min(i, docs.Count - 1)]);
        return true;
    }

    void ReopenClosed()
    {
        while (closed.Count > 0)
        {
            var p = closed.Pop();
            if (File.Exists(p)) { OpenPath(p); return; }
        }
    }

    void AddRecent(string p)
    {
        recent.RemoveAll(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, p);
        if (recent.Count > 12) recent.RemoveAt(recent.Count - 1);
    }

    void FillRecent()
    {
        mRecent.DropDownItems.Clear();
        foreach (var p in recent)
        {
            var path = p;
            mRecent.DropDownItems.Add(Item(path.Replace("&", "&&"), Keys.None, () => OpenPath(path)));
        }
        if (recent.Count == 0) mRecent.DropDownItems.Add(new ToolStripMenuItem(Tr.S("(vacío)")) { Enabled = false });
        Recolor(mRecent.DropDownItems, T);
    }

    void OnDragEnter(object s, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
    }

    void OnDragDrop(object s, DragEventArgs e)
    {
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files != null) foreach (var f in files) if (File.Exists(f)) OpenPath(f);
    }

    // ---------- session & settings persistence ----------

    // Like modern Notepad: closing never nags; open tabs and unsaved text come back next time.
    void SaveSession()
    {
        Directory.CreateDirectory(SessDir);
        var lines = new List<string> { sel.ToString() };
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in docs)
        {
            string tmp = "";
            if ((d.Dirty || d.FilePath == null) && d.Box.TextLength > 0)
            {
                // Each tab keeps its file, rewritten only when its text changed since the last save.
                tmp = d.SessionFile ?? (d.SessionFile = "tab" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".txt");
                string f = Path.Combine(SessDir, tmp), text = d.Text;
                if (!ReferenceEquals(text, d.SessionText) || !File.Exists(f))
                {
                    File.WriteAllText(f + ".new", text, Encoding.UTF8);
                    if (File.Exists(f)) File.Replace(f + ".new", f, null); else File.Move(f + ".new", f);
                    d.SessionText = text;
                }
                keep.Add(tmp);
            }
            else if (d.FilePath == null) continue;
            lines.Add(string.Join("\t", d.FilePath ?? "", tmp, d.Enc, d.Eol, d.Dirty ? 1 : 0, d.Box.SelectionStart, d.Lang == null ? "" : d.Lang.Name, d.Url ?? "", d.Pinned ? 1 : 0));
        }
        File.WriteAllLines(Path.Combine(SessDir, "session.txt"), lines);
        foreach (var f in Directory.GetFiles(SessDir, "tab*.txt"))
            if (!keep.Contains(Path.GetFileName(f))) File.Delete(f);
        sessionDirty = false;
    }

    void RestoreSession()
    {
        string[] lines;
        try { lines = File.ReadAllLines(Path.Combine(SessDir, "session.txt")); } catch { return; }
        for (int i = 1; i < lines.Length; i++)
        {
            var c = lines[i].Split('\t');
            if (c.Length < 6) continue;
            try
            {
                string path = c[0].Length > 0 ? c[0] : null, tmp = c[1].Length > 0 ? Path.Combine(SessDir, c[1]) : null;
                Doc d;
                if (tmp != null && File.Exists(tmp))
                {
                    d = NewDoc();
                    d.FilePath = path;
                    SetText(d, Normalize(File.ReadAllText(tmp)));
                    d.Enc = int.Parse(c[2]);
                    d.Eol = int.Parse(c[3]);
                    d.Box.Modified = c[4] == "1";
                    if (path != null && File.Exists(path)) d.Stamp = File.GetLastWriteTimeUtc(path);
                    d.SessionFile = c[1];
                    d.SessionText = d.Text;
                }
                else if (path != null && File.Exists(path))
                {
                    d = NewDoc();
                    LoadInto(d, path, File.ReadAllBytes(path));
                }
                else continue;
                if (c.Length > 6) d.Lang = Lang.All.FirstOrDefault(l => l.Name == c[6]);
                if (c.Length > 7 && c[7].Length > 0) d.Url = c[7];
                d.Pinned = c.Length > 8 && c[8] == "1";
                int s;
                if (int.TryParse(c[5], out s)) d.Box.Select(Math.Min(s, d.Box.TextLength), 0);
                UpdateTitle(d);
            }
            catch (Exception ex) { Log.Write("restore tab", ex); }
        }
        int a;
        if (int.TryParse(lines[0], out a) && a >= 0 && a < docs.Count) sel = a;
    }

    void LoadSettings()
    {
        var kv = new Dictionary<string, string>();
        var f = Path.Combine(dir, "settings.ini");
        if (!File.Exists(f)) return;
        foreach (var l in File.ReadAllLines(f))
        {
            int i = l.IndexOf('=');
            if (i > 0) kv[l.Substring(0, i)] = l.Substring(i + 1);
        }
        Func<string, bool, bool> flag = (k, def) => { string x; return kv.TryGetValue(k, out x) ? x == "1" : def; };
        string v;
        float size;
        int n;
        if (kv.TryGetValue("font", out v) && kv.ContainsKey("size") && float.TryParse(kv["size"], NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            font = new Font(v, size);
        if (kv.TryGetValue("theme", out v) && int.TryParse(v, out n) && n >= 0 && n < 3) themeMode = n;
        if (kv.TryGetValue("tab", out v) && int.TryParse(v, out n) && n > 0) tabSize = n;
        wrap = flag("wrap", false);
        lineNumbers = flag("lines", true);
        syntax = flag("syntax", true);
        autoIndent = flag("indent", true);
        spaces = flag("spaces", false);
        restoreSession = flag("session", true);
        checkUpdates = flag("updates", true);
        previewOn = flag("preview", false);
        showSpaces = flag("whitespace", false);
        autoClose = flag("pairs", false);
        singleInstance = flag("single", true);
        TopMost = flag("top", false);
        if (kv.TryGetValue("recent", out v)) recent.AddRange(v.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
        if (kv.TryGetValue("bounds", out v))
        {
            var p = v.Split(',').Select(int.Parse).ToArray();
            var r = new Rectangle(p[0], p[1], p[2], p[3]);
            if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r))) { StartPosition = FormStartPosition.Manual; Bounds = r; }
        }
        if (flag("max", false)) WindowState = FormWindowState.Maximized;
    }

    void SaveSettings()
    {
        var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        Func<bool, string> b = x => x ? "1" : "0";
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "settings.ini"), new[] {
            "font=" + font.Name,
            "size=" + font.Size.ToString(CultureInfo.InvariantCulture),
            "theme=" + themeMode,
            "lang=" + lang,
            "tab=" + tabSize,
            "wrap=" + b(wrap),
            "lines=" + b(lineNumbers),
            "syntax=" + b(syntax),
            "indent=" + b(autoIndent),
            "spaces=" + b(spaces),
            "session=" + b(restoreSession),
            "updates=" + b(checkUpdates),
            "preview=" + b(previewOn),
            "whitespace=" + b(showSpaces),
            "pairs=" + b(autoClose),
            "single=" + b(singleInstance),
            "top=" + b(TopMost),
            "max=" + b(WindowState == FormWindowState.Maximized),
            "bounds=" + r.X + "," + r.Y + "," + r.Width + "," + r.Height,
            "recent=" + string.Join("|", recent) });
    }

    void OnClosing(object s, FormClosingEventArgs e)
    {
        if (restoreSession)
        {
            try { SaveSession(); }
            catch (Exception ex)
            {
                if (!Confirm(Tr.S("No se pudo guardar la sesión:\n{0}\n\n¿Cerrar de todos modos? Se perderá el texto sin guardar.", ex.Message)))
                {
                    e.Cancel = true;
                    restartExe = null;
                    return;
                }
            }
        }
        else
        {
            foreach (var d in docs.ToList()) if (!ConfirmClose(d)) { e.Cancel = true; restartExe = null; return; }
            try { if (Directory.Exists(SessDir)) Directory.Delete(SessDir, true); } catch { }
        }
        try { SaveSettings(); } catch { }
    }

    bool Confirm(string q) { return MessageBox.Show(this, q, App, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes; }
    void Err(Exception ex) { MessageBox.Show(this, ex.Message, App, MessageBoxButtons.OK, MessageBoxIcon.Error); }
}
