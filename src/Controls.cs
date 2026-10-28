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

class Doc
{
    public Panel Page;
    public MultiBox Box;
    public Gutter Gut;
    public string FilePath;
    public string Url;             // where a tab opened from the web came from, until it's saved
    public int Enc, Eol, Untitled; // Eol: 0 CRLF, 1 LF, 2 CR
    public DateTime Stamp;
    public Lang Lang;
    public string Media;           // audio/video played along with an .lrc preview
    public dynamic Tom;            // RichEdit's ITextDocument, for undo-free coloring
    public bool Busy, HighlightQueued, Colored;
    public bool Pinned;
    public bool Folded;            // some lines are hidden; RichEdit then numbers lines by what it shows
    public readonly SyntaxCache Syntax = new SyntaxCache();
    public string SessionFile, SessionText; // what the session last wrote for this tab, to skip rewriting it
    string text;
    int[] starts;

    public bool Dirty { get { return Box.Modified; } }
    public string Title
    {
        get
        {
            if (FilePath != null) return Path.GetFileName(FilePath);
            if (Url != null) { var u = new Uri(Url); string f = Path.GetFileName(u.LocalPath); return f.Length > 0 ? f : u.Host; }
            return Tr.S("Sin título {0}", Untitled);
        }
    }
    public void Invalidate() { text = null; starts = null; }

    // RichTextBox counts every line break as one char, so cache text with "\n" only.
    public string Text
    {
        get
        {
            if (text == null)
            {
                text = Box.Text;
                if (text.IndexOf('\r') >= 0) text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            }
            return text;
        }
    }

    public int[] Starts
    {
        get
        {
            if (starts == null)
            {
                var l = new List<int> { 0 };
                var t = Text;
                for (int i = 0; i < t.Length; i++) if (t[i] == '\n') l.Add(i + 1);
                starts = l.ToArray();
            }
            return starts;
        }
    }

    public int LineOf(int pos)
    {
        int i = Array.BinarySearch(Starts, pos);
        return i >= 0 ? i : ~i - 1;
    }
}

// RichTextBox plus extra carets/selections: Ctrl+click or Ctrl+drag adds one. MainForm applies edits to all.
class MultiBox : RichTextBox
{
    public List<int[]> Extra = new List<int[]>(); // {anchor, caret}
    public bool Editing;
    // Drawn over the text as {start, length, style}: 0 boxes a matching bracket, 1 tints another
    // occurrence of the selected word, 2 tints a find match.
    public List<int[]> Marks = new List<int[]>();
    public bool ShowSpaces;
    public Func<string> Source; // the text with "\n" line breaks, to find the spaces and tabs to draw
    public Color Faint = Color.Gray, WordTint = Color.FromArgb(70, Color.Gray), FindTint = Color.FromArgb(90, Color.Orange);

    public void ClearExtra() { if (Extra.Count > 0) { Extra.Clear(); Invalidate(); } }

    public void SetMarks(List<int[]> m)
    {
        if (m.Count == Marks.Count && m.Zip(Marks, (x, y) => x[0] == y[0] && x[1] == y[1] && x[2] == y[2]).All(same => same)) return;
        Marks = m;
        Invalidate();
    }

    public static bool Collide(int[] x, int[] y)
    {
        int a = Math.Min(x[0], x[1]), b = Math.Max(x[0], x[1]), c = Math.Min(y[0], y[1]), e = Math.Max(y[0], y[1]);
        return a < e && c < b || a == c && b == e;
    }

    // EM_POSFROMCHAR directly: the .NET wrapper fetches the whole text per call and refuses the end position.
    Point Pos(int i)
    {
        Point p;
        Native.SendMessage(Handle, 0xD6, out p, (IntPtr)i);
        return p;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg >= 0x200 && m.Msg <= 0x203) // mouse move / button down / up / double click
        {
            int keys = (int)m.WParam;
            if (m.Msg == 0x201)
            {
                if ((keys & 8) == 0) ClearExtra();
                else { int a = SelectionStart; Extra.Add(new[] { a, a + SelectionLength }); Invalidate(); }
            }
            m.WParam = (IntPtr)(keys & ~8); // RichEdit sees a plain click/drag
            base.WndProc(ref m);
            if (m.Msg == 0x202 && Extra.Count > 0)
            {
                var p = new[] { SelectionStart, SelectionStart + SelectionLength };
                Extra.RemoveAll(x => Collide(x, p));
                Invalidate();
            }
            return;
        }
        if (m.Msg != 0xF /*WM_PAINT*/ || Extra.Count == 0 && Marks.Count == 0 && !ShowSpaces) { base.WndProc(ref m); return; }
        // Draw only over what RichEdit just repainted, so translucent fills never stack up.
        IntPtr rgn = Native.CreateRectRgn(0, 0, 0, 0);
        Native.GetUpdateRgn(Handle, rgn, false);
        base.WndProc(ref m);
        using (var g = Graphics.FromHwnd(Handle))
        using (var clip = Region.FromHrgn(rgn))
        {
            g.Clip = clip;
            PaintExtra(g);
        }
        Native.DeleteObject(rgn);
    }

    void PaintExtra(Graphics g)
    {
        int lh = (int)(Font.Height * ZoomFactor), h = ClientSize.Height, w = ClientSize.Width;
        int top = GetFirstCharIndexFromLine(GetLineFromCharIndex(GetCharIndexFromPosition(Point.Empty)));
        if (ShowSpaces && Source != null)
        {
            string t = Source();
            int end = Math.Min(t.Length, GetCharIndexFromPosition(new Point(w - 1, h - 1)) + 1);
            using (var dot = new SolidBrush(Faint))
            using (var pen = new Pen(Faint))
                for (int i = Math.Max(0, top); i < end; i++)
                {
                    char c = t[i];
                    if (c != ' ' && c != '\t') continue;
                    Point p = Pos(i), q = Pos(i + 1);
                    int cw = q.Y == p.Y ? q.X - p.X : lh / 2;
                    if (cw <= 0 || p.X > w || p.Y > h) continue; // folded away, or off screen
                    int y = p.Y + lh / 2;
                    if (c == ' ') g.FillRectangle(dot, p.X + cw / 2 - 1, y - 1, 2, 2);
                    else
                    {
                        int x2 = p.X + Math.Max(6, cw - 4);
                        g.DrawLine(pen, p.X + 2, y, x2, y);
                        g.DrawLine(pen, x2 - 3, y - 3, x2, y);
                        g.DrawLine(pen, x2 - 3, y + 3, x2, y);
                    }
                }
        }
        using (var word = new SolidBrush(WordTint))
        using (var found = new SolidBrush(FindTint))
        using (var box = new Pen(Faint))
            foreach (var k in Marks)
            {
                if (k[0] + k[1] < top) continue;
                Point p = Pos(k[0]), q = Pos(k[0] + k[1]);
                if (p.Y > h) continue;
                int x2 = q.Y == p.Y ? q.X : p.X + lh / 2;
                if (k[2] == 0) g.DrawRectangle(box, p.X, p.Y, Math.Max(2, x2 - p.X) - 1, lh - 1);
                else g.FillRectangle(k[2] == 1 ? word : found, p.X, p.Y, Math.Max(2, x2 - p.X), lh);
            }
        using (var fill = new SolidBrush(Color.FromArgb(90, SystemColors.Highlight)))
        using (var caret = new SolidBrush(ForeColor))
            foreach (var x in Extra)
            {
                int lo = Math.Max(top, Math.Min(x[0], x[1])), hi = Math.Max(x[0], x[1]);
                for (int line = GetLineFromCharIndex(lo); lo < hi; line++)
                {
                    int next = GetFirstCharIndexFromLine(line + 1), end = next < 0 || next > hi ? hi : next;
                    var p = Pos(lo);
                    if (p.Y > h) break;
                    int x2 = end == next ? Pos(end - 1).X + lh / 2 : Pos(end).X; // line break: show a bit of it
                    g.FillRectangle(fill, p.X, p.Y, Math.Max(1, x2 - p.X), lh);
                    lo = end;
                }
                var c = Pos(x[1]);
                g.FillRectangle(caret, c.X, c.Y, 2, lh);
            }
    }
}

// Line numbers, plus a fold marker column for documents with a language.
class Gutter : Control
{
    public Doc D;
    public Func<int, int> FoldState; // per line: 0 nothing to fold, 1 can fold, 2 folded
    public Action<int> OnFold;
    readonly List<int[]> markers = new List<int[]>(); // {y, line} painted last time, for clicks
    int lineHeight, foldX = int.MaxValue;

    public Gutter()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var b = D.Box;
        e.Graphics.Clear(BackColor);
        markers.Clear();
        using (var f = new Font(b.Font.FontFamily, b.Font.Size * b.ZoomFactor))
        {
            // Wrapped or folded, RichEdit's lines are not the file's lines, so number from the text instead.
            bool logical = b.WordWrap || D.Folded, folds = FoldState != null && D.Lang != null && b.TextLength <= 3000000;
            int len = b.TextLength, lh = lineHeight = f.Height, fw = folds ? lh : 0;
            int total = logical ? D.Starts.Length : b.GetLineFromCharIndex(len) + 1;
            int w = TextRenderer.MeasureText(new string('9', Math.Max(3, total.ToString().Length)), f).Width + 14 + fw;
            if (Width != w) { Width = w; return; } // relayout repaints us
            foldX = w - fw - 4;

            for (int line = b.GetLineFromCharIndex(b.GetCharIndexFromPosition(new Point(1, 1))); ; line++)
            {
                int ci = b.GetFirstCharIndexFromLine(line);
                if (ci < 0) break;
                // Empty last line has no char to ask the position of.
                int y = ci >= len && len > 0 ? b.GetPositionFromCharIndex(len - 1).Y + lh : b.GetPositionFromCharIndex(ci).Y;
                if (y > Height) break;
                int k = logical ? D.LineOf(ci) : line;
                bool first = !logical || D.Starts[k] == ci;
                TextRenderer.DrawText(e.Graphics, first ? (k + 1).ToString() : "", f, new Rectangle(0, y, w - 8 - fw, lh), ForeColor, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                int state = folds && first ? FoldState(k) : 0;
                if (state == 0) continue;
                markers.Add(new[] { y, k });
                TextRenderer.DrawText(e.Graphics, state == 2 ? "›" : "⌄", f, new Rectangle(w - fw - 4, y, fw, lh), ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.X < foldX || OnFold == null) return;
        foreach (var m in markers) if (e.Y >= m[0] && e.Y < m[0] + lineHeight) { OnFold(m[1]); return; }
    }
}

// Owner-drawn tab bar: themable (TabControl is not), with close buttons, "+", drag to reorder,
// pinned tabs (a pin instead of the close button) and a right-click menu.
class TabStrip : Control
{
    public List<Doc> Docs;
    public int Selected;
    public Theme T = Theme.Light;
    public Action<int> OnSelect, OnClose;
    public Action<int, Point> OnContext;
    public Action<int, int> OnReorder;
    public Action OnNew;
    int hover = -1, drag = -1;
    bool hoverClose, hoverPlus;
    readonly ToolTip tip = new ToolTip();

    public TabStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = Font.Height + 16;
    }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Height = Font.Height + 16; }

    Rectangle[] Rects(out Rectangle plus)
    {
        int n = Docs.Count, h = Height - 1, closeW = Font.Height + 4, plusW = Height;
        var w = new int[n];
        int sum = 0;
        for (int i = 0; i < n; i++)
        {
            w[i] = Math.Min(Font.Height * 14, TextRenderer.MeasureText(Docs[i].Title, Font).Width + 24 + closeW);
            sum += w[i];
        }
        int avail = Width - plusW - 4;
        if (sum > avail && n > 0) for (int i = 0; i < n; i++) w[i] = Math.Max(Font.Height * 3, avail / n);
        var r = new Rectangle[n];
        int x = 0;
        for (int i = 0; i < n; i++) { r[i] = new Rectangle(x, 0, w[i], h); x += w[i]; }
        plus = new Rectangle(Math.Min(x + 2, Width - plusW + 2), 3, plusW - 6, h - 6);
        return r;
    }

    Rectangle CloseRect(Rectangle r)
    {
        int s = Font.Height + 2;
        return new Rectangle(r.Right - s - 8, r.Y + (r.Height - s) / 2, s, s);
    }

    int Hit(Point p, out bool close, out bool plus)
    {
        Rectangle pr;
        var rs = Rects(out pr);
        close = false;
        plus = pr.Contains(p);
        if (plus) return -1;
        for (int i = 0; i < rs.Length; i++)
            if (rs[i].Contains(p)) { close = !Docs[i].Pinned && CloseRect(rs[i]).Contains(p); return i; }
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(T.Chrome);
        Rectangle plus;
        var rs = Rects(out plus);
        const TextFormatFlags text = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        const TextFormatFlags center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        using (var border = new Pen(T.Border))
        {
            g.DrawLine(border, 0, Height - 1, Width, Height - 1);
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                var d = Docs[i];
                bool sel = i == Selected, hot = i == hover;
                if (sel)
                {
                    using (var br = new SolidBrush(T.Back)) g.FillRectangle(br, r.X, r.Y, r.Width, r.Height + 1);
                    using (var br = new SolidBrush(T.Accent)) g.FillRectangle(br, r.X, r.Y, r.Width, 2);
                }
                else if (hot) using (var br = new SolidBrush(T.Hover)) g.FillRectangle(br, r);
                else if (i + 1 != Selected) g.DrawLine(border, r.Right - 1, r.Y + 8, r.Right - 1, r.Bottom - 8);

                var c = CloseRect(r);
                TextRenderer.DrawText(g, d.Title, Font, new Rectangle(r.X + 12, r.Y, c.X - r.X - 14, r.Height), sel ? T.Fore : T.ChromeFore, text);
                if (hot && hoverClose) using (var br = new SolidBrush(T.Border)) g.FillRectangle(br, c);
                string glyph = d.Pinned ? (d.Dirty ? "●" : "📌") : hot || (sel && !d.Dirty) ? "×" : d.Dirty ? "●" : "";
                TextRenderer.DrawText(g, glyph, Font, c, sel ? T.Fore : T.ChromeFore, center);
            }
        }
        if (hoverPlus) using (var br = new SolidBrush(T.Hover)) g.FillRectangle(br, plus);
        using (var f = new Font(Font.FontFamily, Font.Size * 1.5f)) TextRenderer.DrawText(g, "+", f, plus, T.ChromeFore, center);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        bool close, plus;
        int i = Hit(e.Location, out close, out plus);
        if (e.Button == MouseButtons.Left && i >= 0 && !close) { OnSelect(i); drag = i; }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool close, plus;
        int i = Hit(e.Location, out close, out plus);
        if (e.Button == MouseButtons.Left) { if (plus) OnNew(); else if (i >= 0 && close) OnClose(i); }
        else if (e.Button == MouseButtons.Middle && i >= 0 && !Docs[i].Pinned) OnClose(i);
        else if (e.Button == MouseButtons.Right && i >= 0) OnContext(i, e.Location);
        drag = -1;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        bool close, plus;
        if (Hit(e.Location, out close, out plus) < 0 && !plus) OnNew();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool close, plus;
        int i = Hit(e.Location, out close, out plus);
        if (i != hover || close != hoverClose || plus != hoverPlus)
        {
            if (i != hover) tip.SetToolTip(this, i >= 0 ? Docs[i].FilePath ?? Docs[i].Url ?? Docs[i].Title : "");
            hover = i; hoverClose = close; hoverPlus = plus;
            Invalidate();
        }
        if (drag >= 0 && e.Button == MouseButtons.Left && i >= 0 && i != drag) { OnReorder(drag, i); drag = i; }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = -1; hoverPlus = hoverClose = false;
        Invalidate();
    }
}

// Find bar above the editor: Ctrl+F shows the find row, Ctrl+H the replace row too.
class FindBar : Panel
{
    public readonly TextBox What = new TextBox(), With = new TextBox();
    public readonly CheckBox MatchCase = Toggle("Aa"), Word = Toggle("ab"), Rx = Toggle(".*");
    public readonly Label Info = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 0, 8, 0) };
    public readonly ContextMenuStrip More = new ContextMenuStrip();
    public Action Changed; // the search or its options changed
    readonly FlowLayoutPanel replaceRow;
    readonly ToolTip tip = new ToolTip();

    static CheckBox Toggle(string t)
    {
        return new CheckBox { Text = t, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(2, 0, 0, 0) };
    }

    Button Btn(string text, string hint, Action a)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left, Margin = new Padding(2, 0, 0, 0), Padding = new Padding(6, 0, 6, 0) };
        b.Click += (s, e) => a();
        if (hint != null) tip.SetToolTip(b, hint);
        return b;
    }

    static FlowLayoutPanel Row(params Control[] cs)
    {
        var r = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        r.Controls.AddRange(cs);
        return r;
    }

    public FindBar(MainForm main)
    {
        Font = main.Font;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(6, 2, 6, 2);
        Visible = false;
        What.Width = With.Width = Font.Height * 18;
        tip.SetToolTip(MatchCase, Tr.S("Mayús/minús"));
        tip.SetToolTip(Word, Tr.S("Palabra completa"));
        tip.SetToolTip(Rx, Tr.S("Expresión regular"));
        var more = Btn("⋯", null, () => { });
        more.Click += (s, e) => More.Show(more, 0, more.Height);
        replaceRow = Row(With, Btn(Tr.S("Reemplazar"), null, main.ReplaceOne), Btn(Tr.S("Reemplazar todo"), null, main.ReplaceAll));
        var rows = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill };
        rows.Controls.Add(Row(What, MatchCase, Word, Rx, Btn("↑", Tr.S("Anterior") + " (Shift+F3)", () => main.FindNext(true)),
            Btn("↓", Tr.S("Siguiente") + " (F3)", () => main.FindNext(false)), more, Btn("×", null, main.CloseFind), Info));
        rows.Controls.Add(replaceRow);
        Controls.Add(rows);
        HandleCreated += (s, e) =>
        {
            Cue(What, Tr.S("Buscar:").TrimEnd(':', ' ', '：'));
            Cue(With, Tr.S("Reemplazar:").TrimEnd(':', ' ', '：'));
        };

        What.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; main.FindNext(e.Shift); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; main.CloseFind(); }
        };
        With.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; main.ReplaceOne(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; main.CloseFind(); }
        };
        What.TextChanged += (s, e) => { if (Changed != null) Changed(); };
        foreach (var c in new[] { MatchCase, Word, Rx }) c.CheckedChanged += (s, e) => { if (Changed != null) Changed(); };
    }

    // Grey hint text inside an empty box (EM_SETCUEBANNER).
    static void Cue(TextBox b, string text)
    {
        var p = Marshal.StringToHGlobalUni(text);
        Native.SendMessage(b.Handle, 0x1501, (IntPtr)1, p);
        Marshal.FreeHGlobal(p);
    }

    public bool ReplaceMode { get { return replaceRow.Visible; } set { replaceRow.Visible = value; } }

    public void Style(Theme t)
    {
        BackColor = t.Chrome;
        ForeColor = t.Fore;
        foreach (var c in new[] { MatchCase, Word, Rx })
        {
            c.BackColor = t.Chrome;
            c.FlatAppearance.BorderColor = t.Border;
            c.FlatAppearance.CheckedBackColor = t.IsDark ? Color.FromArgb(9, 71, 113) : Color.FromArgb(204, 228, 247);
        }
    }

    public Regex Build()
    {
        string p = What.Text;
        if (p.Length == 0) return null;
        if (!Rx.Checked) p = Regex.Escape(p);
        if (Word.Checked) p = @"\b(?:" + p + @")\b";
        var o = RegexOptions.Multiline;
        if (!MatchCase.Checked) o |= RegexOptions.IgnoreCase;
        try { return new Regex(p, o, TimeSpan.FromSeconds(3)); } // a runaway pattern must not hang the editor
        catch (ArgumentException ex) { Info.Text = ex.Message; return null; }
    }

    public string Replacement(Match m)
    {
        if (!Rx.Checked) return With.Text;
        string r;
        try { r = Regex.Unescape(With.Text); } catch (ArgumentException) { r = With.Text; } // lets "\n" and "\t" work
        return m.Result(r);
    }
}
