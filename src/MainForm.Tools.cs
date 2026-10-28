// Part of BlocPlus; see BlocPlus.cs. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

partial class MainForm
{
    // ---------- marks drawn over the text ----------

    // The bracket pairing the one at the caret, other occurrences of the selected word, and the find
    // matches on screen.
    void UpdateMarks(Doc d)
    {
        if (d != Cur) return;
        var b = d.Box;
        var marks = new List<int[]>();
        if (b.TextLength <= 3000000 && b.Extra.Count == 0)
        {
            string t = d.Text;
            int vs = Math.Max(0, b.GetFirstCharIndexFromLine(b.GetLineFromCharIndex(b.GetCharIndexFromPosition(new Point(1, 1)))));
            int ve = Math.Min(t.Length, b.GetCharIndexFromPosition(new Point(b.ClientSize.Width - 1, b.ClientSize.Height - 1)) + 1);
            int nl = t.IndexOf('\n', ve);
            ve = nl < 0 ? t.Length : nl;
            int p = b.SelectionStart;
            if (b.SelectionLength == 0)
            {
                int q = MatchBracket(t, p);
                if (q < 0 && p > 0 && (q = MatchBracket(t, p - 1)) >= 0) p--;
                if (q >= 0) { marks.Add(new[] { p, 1, 0 }); marks.Add(new[] { q, 1, 0 }); }
            }
            if (find.Visible)
            {
                var re = find.What.Text.Length > 0 ? find.Build() : null;
                if (re != null)
                    Searching(() =>
                    {
                        for (var m = re.Match(t, vs); m.Success && m.Index < ve && marks.Count < 2000; m = m.NextMatch())
                            if (m.Length > 0) marks.Add(new[] { m.Index, m.Length, 2 });
                    });
            }
            else if (b.SelectionLength > 0 && b.SelectionLength <= 100)
            {
                string w = t.Substring(p, b.SelectionLength).TrimEnd(); // a double click also takes the space after the word
                if (w.Length > 0 && w.All(IsWordChar) && IsWordAt(t, p, w.Length))
                    for (int i = t.IndexOf(w, vs, StringComparison.Ordinal); i >= 0 && i < ve; i = t.IndexOf(w, i + 1, StringComparison.Ordinal))
                        if (i != p && IsWordAt(t, i, w.Length)) marks.Add(new[] { i, w.Length, 1 });
            }
        }
        b.SetMarks(marks);
    }

    static bool IsWordChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }
    static bool IsWordAt(string t, int i, int n) { return (i == 0 || !IsWordChar(t[i - 1])) && (i + n >= t.Length || !IsWordChar(t[i + n])); }

    // Index of the bracket that pairs with the one at p, or -1. Plain counting, at most 100 000 chars away.
    public static int MatchBracket(string t, int p)
    {
        const string open = "([{", close = ")]}";
        if (p < 0 || p >= t.Length) return -1;
        char c = t[p];
        int o = open.IndexOf(c), k = close.IndexOf(c);
        if (o < 0 && k < 0) return -1;
        char a = o >= 0 ? c : open[k], z = o >= 0 ? close[o] : c;
        int dir = o >= 0 ? 1 : -1, depth = 0;
        for (int i = p, n = 0; i >= 0 && i < t.Length && n < 100000; i += dir, n++)
        {
            if (t[i] == a) depth += dir;
            else if (t[i] == z) depth -= dir;
            if (depth == 0) return i;
        }
        return -1;
    }

    // ---------- auto-closing pairs ----------

    const string Openers = "([{\"'`", Closers = ")]}\"'`";

    // Typing an opener adds its closer, typing a closer steps over the same one right after the caret,
    // and a selection gets wrapped. Quotes are left alone next to letters, so "don't" stays as typed.
    bool AutoPair(Doc d, char c)
    {
        var b = d.Box;
        string t = d.Text;
        int p = b.SelectionStart, n = b.SelectionLength, o = Openers.IndexOf(c);
        char next = p + n < t.Length ? t[p + n] : '\n', prev = p > 0 ? t[p - 1] : '\n';
        if (n == 0 && Closers.IndexOf(c) >= 0 && next == c) { b.Select(p + 1, 0); return true; }
        if (o < 0) return false;
        if (n > 0)
        {
            string s = t.Substring(p, n);
            b.SelectedText = c + s + Closers[o];
            b.Select(p + 1, n);
            return true;
        }
        if (o >= 3 ? IsWordChar(prev) || IsWordChar(next) || prev == c : IsWordChar(next)) return false;
        b.SelectedText = c.ToString() + Closers[o];
        b.Select(p + 1, 0);
        return true;
    }

    // Backspace between an empty pair deletes both halves.
    bool ClosePairAround(Doc d)
    {
        string t = d.Text;
        int p = d.Box.SelectionStart, o = p > 0 && p < t.Length ? Openers.IndexOf(t[p - 1]) : -1;
        if (o < 0 || t[p] != Closers[o]) return false;
        Replace(d, p - 1, p + 1, "");
        return true;
    }

    // ---------- folding ----------

    // Lines more indented than the one above them can be hidden under it. RichEdit hides text with a
    // character format, so folds are not edits: they never reach undo or the saved file.

    int LineIndent(Doc d, int k)
    {
        var st = d.Starts;
        string t = d.Text;
        int w = 0, e = k + 1 < st.Length ? st[k + 1] - 1 : t.Length;
        for (int i = st[k]; i < e; i++)
        {
            if (t[i] == ' ') w++;
            else if (t[i] == '\t') w += tabSize - w % tabSize;
            else return w;
        }
        return -1; // blank
    }

    static bool Hidden(Doc d, int pos) { return d.Tom != null && d.Tom.Range(pos, pos + 1).Font.Hidden != 0; }

    // 0 nothing to fold under line k, 1 it can be folded, 2 it is folded.
    int FoldState(Doc d, int k)
    {
        var st = d.Starts;
        if (k + 1 >= st.Length) return 0;
        if (d.Folded && Hidden(d, st[k + 1] - 1)) return 2;
        int ind = LineIndent(d, k);
        if (ind < 0) return 0;
        for (int j = k + 1; j < st.Length && j <= k + 50; j++)
        {
            int x = LineIndent(d, j);
            if (x >= 0) return x > ind ? 1 : 0;
        }
        return 0;
    }

    // Folds the block under line k, or the block line k is in; or unfolds what is hidden under line k.
    void FoldAt(Doc d, int k, bool fold)
    {
        if (d.Tom == null || d.Box.TextLength > 3000000) return;
        var st = d.Starts;
        string t = d.Text;
        if (!fold)
        {
            if (k + 1 >= st.Length || !Hidden(d, st[k + 1] - 1)) return;
            int from = st[k + 1] - 1, to = from;
            while (to < t.Length && Hidden(d, to)) { int j = d.LineOf(to) + 1; to = j < st.Length ? st[j] : t.Length; }
            Silently(d, () => d.Tom.Range(from, to).Font.Hidden = 0);
        }
        else
        {
            if (FoldState(d, k) != 1)
            {
                int ind = LineIndent(d, k), j = k - 1;
                if (ind < 0) ind = int.MaxValue;
                while (j >= 0 && (LineIndent(d, j) < 0 || LineIndent(d, j) >= ind)) j--;
                if (j < 0 || FoldState(d, j) != 1) return;
                k = j;
            }
            int head = LineIndent(d, k), last = k;
            for (int j = k + 1; j < st.Length; j++)
            {
                int x = LineIndent(d, j);
                if (x < 0) continue;
                if (x <= head) break;
                last = j;
            }
            int from = st[k + 1] - 1, to = last + 1 < st.Length ? st[last + 1] - 1 : t.Length;
            Silently(d, () => d.Tom.Range(from, to).Font.Hidden = -1);
            d.Folded = true;
            int c = d.Box.SelectionStart;
            if (c > from && c <= to) d.Box.Select(from, 0);
        }
        d.Gut.Invalidate();
        d.Box.Invalidate();
        UpdateStatus();
    }

    void UnfoldAll(Doc d)
    {
        if (!d.Folded || d.Tom == null) return;
        Silently(d, () => d.Tom.Range(0, d.Box.TextLength).Font.Hidden = 0);
        d.Folded = false;
        d.Gut.Invalidate();
        UpdateStatus();
    }

    // ---------- format, compare, print ----------

    // Formats (or compacts) the selection, or the whole document: XML when it looks like XML, else JSON.
    void Reformat(bool pretty)
    {
        var d = Cur;
        var b = d.Box;
        bool part = b.SelectionLength > 0;
        int a = part ? b.SelectionStart : 0, e = part ? a + b.SelectionLength : d.Text.Length;
        string s = d.Text.Substring(a, e - a), r;
        string indent = !pretty ? null : s.Contains("\n\t") ? "\t" : new string(' ', tabSize);
        bool xml = s.TrimStart().StartsWith("<");
        try { r = xml ? TextTools.Xml(s, indent) : TextTools.Json(s, indent); }
        catch (Exception ex)
        {
            if (!(ex is FormatException || ex is System.Xml.XmlException)) throw;
            MessageBox.Show(this, (xml ? Tr.S("No es XML válido:") : Tr.S("No es JSON válido:")) + "\n\n" + ex.Message, App, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (r == s) return;
        UnfoldAll(d);
        Replace(d, a, e, r);
        b.Select(a, part ? r.Length : 0);
        if (!part) b.ScrollToCaret();
    }

    void FillCompare()
    {
        mCompare.DropDownItems.Clear();
        foreach (var x in docs)
            if (x != Cur) { var o = x; mCompare.DropDownItems.Add(Item(o.Title.Replace("&", "&&"), Keys.None, () => CompareWith(o))); }
        if (mCompare.DropDownItems.Count == 0) mCompare.DropDownItems.Add(new ToolStripMenuItem(Tr.S("(vacío)")) { Enabled = false });
        Recolor(mCompare.DropDownItems, T);
    }

    // The differences between another tab (as the old text) and this one, in a new tab with its preview.
    void CompareWith(Doc other)
    {
        var d = Cur;
        string diff = TextTools.Diff(other.Text, d.Text, other.Title, d.Title);
        if (diff.Length == 0)
        {
            MessageBox.Show(this, Tr.S("{0} y {1} son iguales.", other.Title, d.Title), App, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var n = NewDoc();
        SetText(n, diff);
        n.Lang = Lang.All.First(l => l.Name == "Diff");
        SwitchTo(n);
        if (!previewOn) TogglePreview();
    }

    // Plain text in the editor's font (10 pt at most), wrapped to the page, with the name and page number below.
    void PrintDoc()
    {
        var d = Cur;
        string text = d.Text;
        using (var pd = new PrintDocument { DocumentName = d.Title })
        using (var dlg = new PrintDialog { Document = pd, UseEXDialog = true, AllowSelection = d.Box.SelectionLength > 0 })
        using (var f = new Font(font.FontFamily, Math.Min(font.SizeInPoints, 10f)))
        using (var fmt = new StringFormat(StringFormat.GenericTypographic))
        using (var foot = new StringFormat { Alignment = StringAlignment.Center })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (pd.PrinterSettings.PrintRange == PrintRange.Selection) text = text.Substring(d.Box.SelectionStart, d.Box.SelectionLength);
            text = text.Replace("\t", new string(' ', tabSize));
            fmt.FormatFlags |= StringFormatFlags.LineLimit;
            fmt.Trimming = StringTrimming.Word;
            int pos = 0, page = 0;
            pd.BeginPrint += (s, e) => { pos = 0; page = 0; };
            pd.PrintPage += (s, e) =>
            {
                var r = e.MarginBounds;
                var body = new RectangleF(r.Left, r.Top, r.Width, r.Height - f.Height * 2);
                string rest = text.Substring(pos, Math.Min(text.Length - pos, 30000));
                int chars, lines;
                e.Graphics.MeasureString(rest, f, body.Size, fmt, out chars, out lines);
                if (chars == 0 && rest.Length > 0) chars = 1;
                e.Graphics.DrawString(rest.Substring(0, chars), f, Brushes.Black, body, fmt);
                pos += chars;
                e.Graphics.DrawString(d.Title + "  ·  " + ++page, f, Brushes.Gray, new RectangleF(r.Left, r.Bottom - f.Height, r.Width, f.Height), foot);
                e.HasMorePages = pos < text.Length;
            };
            try { pd.Print(); }
            catch (Exception ex) { Err(ex); }
        }
    }

    // ---------- tabs ----------

    readonly ContextMenuStrip tabMenu = new ContextMenuStrip();

    void TabMenu(int i, Point at)
    {
        var d = docs[i];
        string path = d.FilePath ?? d.Url;
        tabMenu.Items.Clear();
        tabMenu.Items.Add(Item(d.Pinned ? Tr.S("Desanclar") : Tr.S("Anclar"), Keys.None, () => Pin(d, !d.Pinned)));
        tabMenu.Items.Add(new ToolStripSeparator());
        tabMenu.Items.Add(Item(Tr.S("Cerrar"), Keys.None, () => CloseTab(d)));
        tabMenu.Items.Add(Item(Tr.S("Cerrar las demás"), Keys.None, () => CloseMany(x => x != d)));
        tabMenu.Items.Add(Item(Tr.S("Cerrar las de la derecha"), Keys.None, () => { int k = docs.IndexOf(d); CloseMany(x => docs.IndexOf(x) > k); }));
        tabMenu.Items.Add(new ToolStripSeparator());
        tabMenu.Items.Add(Item(Tr.S("Copiar ruta"), Keys.None, () => { try { Clipboard.SetText(path); } catch (ExternalException) { } }));
        tabMenu.Items[tabMenu.Items.Count - 1].Enabled = path != null;
        tabMenu.Items.Add(Item(Tr.S("Abrir la carpeta"), Keys.None, () => Process.Start("explorer.exe", "/select,\"" + d.FilePath + "\"")));
        tabMenu.Items[tabMenu.Items.Count - 1].Enabled = d.FilePath != null;
        if (d != Cur)
        {
            tabMenu.Items.Add(new ToolStripSeparator());
            tabMenu.Items.Add(Item(Tr.S("Comparar con la pestaña actual"), Keys.None, () => CompareWith(d)));
        }
        Recolor(tabMenu.Items, T);
        tabMenu.Show(strip, at);
    }

    // Pinned tabs stay at the front, keep a pin instead of the close button and survive "close others".
    void Pin(Doc d, bool on)
    {
        var cur = Cur;
        d.Pinned = on;
        docs.Remove(d);
        docs.Insert(docs.Count(x => x.Pinned), d);
        sessionDirty = true;
        SwitchTo(cur);
    }

    void CloseMany(Func<Doc, bool> which)
    {
        foreach (var x in docs.Where(x => !x.Pinned && which(x)).ToList()) if (!CloseTab(x)) return;
    }

    // The status bar's language, encoding and line ending offer the same choices as the Document menu.
    void PopupFrom(ToolStripMenuItem m, ToolStripItem label)
    {
        var d = Cur;
        int idx = m == mLang ? (d.Lang == null ? 0 : Array.IndexOf(Lang.All, d.Lang) + 1) : m == mEnc ? d.Enc : d.Eol;
        var menu = new ContextMenuStrip { Renderer = ctx.Renderer };
        for (int i = 0; i < m.DropDownItems.Count; i++)
        {
            var src = m.DropDownItems[i];
            menu.Items.Add(new ToolStripMenuItem(src.Text, null, (s, e) => src.PerformClick()) { Checked = i == idx });
        }
        Recolor(menu.Items, T);
        menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
        menu.Show(status, label.Bounds.Location, ToolStripDropDownDirection.AboveRight);
    }

    // ---------- command palette ----------

    // Every menu command in one list, filtered as you type; Enter runs the selected one.
    void ShowPalette()
    {
        FillRecent();
        FillCompare();
        var cmds = new List<KeyValuePair<string, ToolStripMenuItem>>();
        var keys = new KeysConverter();
        Action<ToolStripItemCollection, string> walk = null;
        walk = (items, path) =>
        {
            foreach (ToolStripItem i in items)
            {
                var m = i as ToolStripMenuItem;
                if (m == null || !m.Enabled || !m.Available) continue;
                string name = path + m.Text.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");
                if (m.DropDownItems.Count > 0) { walk(m.DropDownItems, name + " › "); continue; }
                string sc = m.ShortcutKeyDisplayString ?? (m.ShortcutKeys != Keys.None ? keys.ConvertToString(m.ShortcutKeys) : null);
                cmds.Add(new KeyValuePair<string, ToolStripMenuItem>(sc == null ? name : name + "    " + sc, m));
            }
        };
        walk(menu.Items, "");

        using (var f = new Form { FormBorderStyle = FormBorderStyle.FixedToolWindow, ControlBox = false, Text = "", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Font = Font, KeyPreview = true })
        {
            var box = new TextBox { Dock = DockStyle.Top };
            var list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false };
            f.Controls.Add(list);
            f.Controls.Add(box);
            f.ClientSize = new Size(Font.Height * 34, Font.Height * 20);
            f.Location = PointToScreen(new Point((ClientSize.Width - f.Width) / 2, menu.Height + strip.Height));
            var shown = new List<ToolStripMenuItem>();
            var ci = CultureInfo.CurrentCulture.CompareInfo;
            Action filter = () =>
            {
                var words = box.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                list.BeginUpdate();
                list.Items.Clear();
                shown.Clear();
                foreach (var c in cmds)
                    if (words.All(w => ci.IndexOf(c.Key, w, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)) { list.Items.Add(c.Key); shown.Add(c.Value); }
                if (list.Items.Count > 0) list.SelectedIndex = 0;
                list.EndUpdate();
            };
            ToolStripMenuItem run = null;
            Action pick = () => { if (list.SelectedIndex >= 0) { run = shown[list.SelectedIndex]; f.Close(); } };
            box.TextChanged += (s, e) => filter();
            list.DoubleClick += (s, e) => pick();
            f.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) f.Close();
                else if (e.KeyCode == Keys.Enter) pick();
                else if ((e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) && list.Items.Count > 0)
                    list.SelectedIndex = Math.Max(0, Math.Min(list.Items.Count - 1, list.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1)));
                else return;
                e.Handled = e.SuppressKeyPress = true;
            };
            f.Deactivate += (s, e) => f.Close();
            filter();
            ThemeForm(f, T);
            list.BackColor = T.Input;
            list.ForeColor = T.Fore;
            f.ActiveControl = box;
            f.ShowDialog(this);
            if (run != null) run.PerformClick();
        }
    }

    // ---------- find in tabs and folders ----------

    const int MaxHits = 5000;
    static readonly string[] SkipDirs = { "node_modules", "bin", "obj", "packages", "__pycache__" };
    Panel results;
    Splitter resultSplit;
    ListBox resultList;
    Label resultHead;
    Button resultClose;

    class Hit
    {
        public string Path, Text;
        public Doc Doc;
        public int Line, Col, Len;
        public override string ToString() { return System.IO.Path.GetFileName(Path) + " (" + Line + "):  " + Text; }
    }

    void BuildResults()
    {
        results = new Panel { Dock = DockStyle.Bottom, Height = Font.Height * 12, Visible = false };
        resultSplit = new Splitter { Dock = DockStyle.Bottom, Height = 4, Visible = false };
        resultList = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, HorizontalScrollbar = true };
        resultHead = new Label { Dock = DockStyle.Top, AutoSize = false, Height = Font.Height + 10, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
        resultClose = new Button { Text = "×", Dock = DockStyle.Right, Width = Font.Height * 2, FlatStyle = FlatStyle.Flat };
        resultClose.FlatAppearance.BorderSize = 0;
        resultClose.Click += (s, e) => results.Visible = resultSplit.Visible = false;
        resultHead.Controls.Add(resultClose);
        results.Controls.Add(resultList);
        results.Controls.Add(resultHead);
        resultList.DoubleClick += (s, e) => GoToHit(resultList.SelectedItem as Hit);
        resultList.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; GoToHit(resultList.SelectedItem as Hit); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; results.Visible = resultSplit.Visible = false; Cur.Box.Focus(); }
        };
        Controls.Add(resultSplit);
        Controls.Add(results);
        find.More.Items.AddRange(new ToolStripItem[] {
            Item(Tr.S("Buscar en todas las pestañas"), Keys.None, FindInTabs),
            Item(Tr.S("Buscar en una carpeta..."), Keys.None, FindInFolder),
            new ToolStripSeparator(),
            Item(Tr.S("Reemplazar en todas las pestañas..."), Keys.None, ReplaceInTabs),
            Item(Tr.S("Reemplazar en una carpeta..."), Keys.None, ReplaceInFolder) });
    }

    void ThemeResults(Theme t)
    {
        results.BackColor = resultHead.BackColor = resultClose.BackColor = t.Chrome;
        resultHead.ForeColor = resultClose.ForeColor = t.ChromeFore;
        resultList.BackColor = t.Back;
        resultList.ForeColor = t.Fore;
        resultSplit.BackColor = t.Border;
        resultList.Font = font;
    }

    void ShowResults(List<Hit> hits, int files, string where)
    {
        resultList.BeginUpdate();
        resultList.Items.Clear();
        resultList.Items.AddRange(hits.ToArray());
        resultList.EndUpdate();
        int inFiles = hits.Select(h => h.Path).Distinct().Count();
        resultHead.Text = Tr.S("«{0}» en {1}: {2} resultados en {3} de {4} archivos", find.What.Text, where, hits.Count, inFiles, files) +
            (hits.Count >= MaxHits ? "  " + Tr.S("(solo los primeros {0})", MaxHits) : "");
        results.Visible = resultSplit.Visible = true;
        if (hits.Count > 0) { resultList.SelectedIndex = 0; resultList.Focus(); }
    }

    Regex SearchPattern()
    {
        var re = find.What.Text.Length > 0 ? find.Build() : null;
        if (re == null) { ShowFind(false); find.What.Focus(); }
        return re;
    }

    static void Collect(List<Hit> hits, string t, Regex re, string path, Doc d)
    {
        int line = 1, ls = 0;
        for (var m = re.Match(t); m.Success && hits.Count < MaxHits; m = m.NextMatch())
        {
            if (m.Length == 0) continue;
            for (int nl; (nl = t.IndexOf('\n', ls)) >= 0 && nl < m.Index; ls = nl + 1) line++;
            int le = t.IndexOf('\n', ls);
            if (le < 0) le = t.Length;
            hits.Add(new Hit { Path = path, Doc = d, Line = line, Col = m.Index - ls, Len = m.Length, Text = t.Substring(ls, Math.Min(le - ls, 300)).Trim() });
        }
    }

    void GoToHit(Hit h)
    {
        if (h == null) return;
        var d = h.Doc != null && docs.Contains(h.Doc) ? h.Doc : null;
        if (d == null && File.Exists(h.Path)) { OpenPath(h.Path); d = docs.FirstOrDefault(x => string.Equals(x.FilePath, h.Path, StringComparison.OrdinalIgnoreCase)); }
        if (d == null) return;
        SwitchTo(d);
        GoTo(d, h.Line, h.Col + 1);
        d.Box.Select(d.Box.SelectionStart, Math.Min(h.Len, d.Text.Length - d.Box.SelectionStart));
        d.Box.Focus();
    }

    void FindInTabs()
    {
        var re = SearchPattern();
        if (re == null) return;
        var hits = new List<Hit>();
        if (Searching(() => { foreach (var d in docs) Collect(hits, d.Text, re, d.FilePath ?? d.Url ?? d.Title, d); }))
            ShowResults(hits, docs.Count, Tr.S("las pestañas abiertas"));
    }

    // Every file under a folder, skipping hidden folders, links and build/dependency folders.
    static IEnumerable<string> Files(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            string[] files, dirs;
            try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
            catch (Exception) { continue; } // no access
            foreach (var f in files) yield return f;
            foreach (var x in dirs.Reverse())
            {
                var info = new DirectoryInfo(x);
                if (info.Name.StartsWith(".") || SkipDirs.Contains(info.Name.ToLowerInvariant()) ||
                    (info.Attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0) continue;
                stack.Push(x);
            }
        }
    }

    // A text file's contents with "\n" line breaks, or null for binary, huge or unreadable files.
    static string ReadText(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 10 << 20) return null;
            var bytes = File.ReadAllBytes(path);
            for (int i = 0; i < Math.Min(bytes.Length, 8000); i++)
                if (bytes[i] == 0 && !(bytes.Length > 1 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))) return null;
            int enc;
            return Normalize(Enc.Decode(bytes, out enc));
        }
        catch (Exception) { return null; }
    }

    string PickFolder()
    {
        using (var dlg = new FolderBrowserDialog { Description = Tr.S("Carpeta donde buscar (con sus subcarpetas):"), ShowNewFolderButton = false })
        {
            if (Cur.FilePath != null) dlg.SelectedPath = Path.GetDirectoryName(Cur.FilePath);
            return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
        }
    }

    // Searches the folder in the background, then calls done(hits, files searched) on the UI thread.
    void ScanFolder(string root, Regex re, Action<List<Hit>, int> done)
    {
        resultList.Items.Clear();
        resultHead.Text = Tr.S("Buscando en {0}...", root);
        results.Visible = resultSplit.Visible = true;
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            var hits = new List<Hit>();
            int files = 0;
            Exception error = null;
            try
            {
                foreach (var f in Files(root))
                {
                    if (hits.Count >= MaxHits || files >= 50000) break;
                    string t = ReadText(f);
                    if (t == null) continue;
                    files++;
                    Collect(hits, t, re, f, null);
                }
            }
            catch (Exception ex) { error = ex; }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (error is RegexMatchTimeoutException) find.Info.Text = Tr.S("La búsqueda tarda demasiado. Prueba con una expresión más sencilla.");
                    else if (error != null) Err(error);
                    done(hits, files);
                }));
            }
            catch (InvalidOperationException) { } // window closed
        });
    }

    void FindInFolder()
    {
        var re = SearchPattern();
        string root = re == null ? null : PickFolder();
        if (root != null) ScanFolder(root, re, (hits, files) => ShowResults(hits, files, root));
    }

    // Replaces in every tab; each tab can undo it with Ctrl+Z.
    void ReplaceInTabs()
    {
        var re = SearchPattern();
        if (re == null) return;
        int total = 0, tabs = 0;
        if (!Searching(() => { foreach (var d in docs) { int n = re.Matches(d.Text).Count; total += n; if (n > 0) tabs++; } })) return;
        if (total == 0) { find.Info.Text = Tr.S("No se encontró."); return; }
        if (!Confirm(Tr.S("Se harán {0} reemplazos en {1} pestañas. Cada pestaña puede deshacerlos con Ctrl+Z.\n¿Continuar?", total, tabs))) return;
        var cur = Cur;
        foreach (var d in docs.ToList()) Searching(() => ReplaceIn(d, re));
        SwitchTo(cur);
        find.Info.Text = Tr.S("{0} reemplazos.", total);
    }

    // Files on disk are never changed directly: the ones with matches open in tabs with the
    // replacements made, unsaved, to review and keep with Save all.
    void ReplaceInFolder()
    {
        var re = SearchPattern();
        string root = re == null ? null : PickFolder();
        if (root == null) return;
        ScanFolder(root, re, (hits, files) =>
        {
            ShowResults(hits, files, root);
            var paths = hits.Select(h => h.Path).Distinct().ToList();
            if (paths.Count == 0) return;
            if (paths.Count > 200) { MessageBox.Show(this, Tr.S("Hay coincidencias en {0} archivos; son demasiados para abrirlos a la vez.", paths.Count), App); return; }
            if (!Confirm(Tr.S("Se abrirán {0} archivos con {1} coincidencias y se harán los reemplazos en sus pestañas, sin guardarlos. Revísalos y usa Guardar todo para conservarlos.\n¿Continuar?", paths.Count, hits.Count))) return;
            int n = 0;
            foreach (var p in paths)
            {
                OpenPath(p);
                var d = docs.FirstOrDefault(x => string.Equals(x.FilePath, p, StringComparison.OrdinalIgnoreCase));
                if (d != null) Searching(() => n += ReplaceIn(d, re));
            }
            find.Info.Text = Tr.S("{0} reemplazos.", n);
        });
    }
}
