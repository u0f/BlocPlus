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

partial class MainForm
{
    // ---------- syntax highlighting ----------

    // Runs formatting-only changes without touching undo history or the "modified" flag.
    static void Silently(Doc d, Action a)
    {
        bool mod = d.Box.Modified;
        d.Busy = true;
        var tom = d.Tom;
        if (tom != null) { tom.Undo(-9999995 /*tomSuspend*/); tom.Freeze(); }
        try { a(); }
        finally
        {
            if (tom != null) { tom.Unfreeze(); tom.Undo(-9999994 /*tomResume*/); }
            d.Box.Modified = mod;
            d.Busy = false;
        }
    }

    static void Colorize(Doc d, int s, int e, Color c) { d.Tom.Range(s, e).Font.ForeColor = c.R | c.G << 8 | c.B << 16; }

    void QueueHighlight(Doc d)
    {
        if (d.HighlightQueued || !IsHandleCreated) return;
        d.HighlightQueued = true;
        BeginInvoke((Action)(() => { d.HighlightQueued = false; if (!d.Page.IsDisposed && d == Cur) Highlight(d); }));
    }

    // Recolors only the visible lines, from matches the document's SyntaxCache keeps between calls.
    void Highlight(Doc d)
    {
        if (d.Tom == null) return;
        var t = T;
        var b = d.Box;
        if (!syntax || d.Lang == null || b.TextLength > 3000000)
        {
            if (d.Colored) Silently(d, () => Colorize(d, 0, b.TextLength, t.Fore));
            d.Colored = false;
            return;
        }
        string text = d.Text;
        int vs = Math.Max(0, b.GetFirstCharIndexFromLine(b.GetLineFromCharIndex(b.GetCharIndexFromPosition(new Point(1, 1)))));
        int ve = Math.Min(text.Length, b.GetCharIndexFromPosition(new Point(b.ClientSize.Width - 1, b.ClientSize.Height - 1)));
        int nl = text.IndexOf('\n', ve);
        ve = nl < 0 ? text.Length : nl;
        d.Syntax.Update(text, d.Lang, ve);
        Silently(d, () =>
        {
            Colorize(d, vs, ve, t.Fore);
            d.Syntax.Each(vs, ve, (s, e, k) => Colorize(d, Math.Max(vs, s), Math.Min(ve, e), t.For(k)));
        });
        d.Colored = true;
    }

    void SetLang(Doc d, Lang l)
    {
        d.Lang = l;
        Highlight(d);
        UpdateStatus();
        if (d == Cur) RenderPreview();
    }

    // ---------- editing ----------

    void PastePlain()
    {
        string t;
        try { t = Clipboard.GetText(); } catch (ExternalException) { return; }
        if (t.Length == 0) return;
        t = Normalize(t);
        var d = Cur;
        if (d.Box.Extra.Count == 0) { d.Box.SelectedText = t; return; }
        // One line per cursor when the counts match (as copied from several cursors), else all of it at each.
        var lines = t.TrimEnd('\n').Split('\n');
        bool spread = lines.Length == d.Box.Extra.Count + 1;
        int n = 0;
        MultiEdit(d, (x, s, en) => Tuple.Create(s, en, spread ? lines[n++] : t));
    }

    // Full logical lines covered by the selection: [a, e), e excludes the trailing '\n'.
    static void LineRange(Doc d, out int a, out int e)
    {
        string t = d.Text;
        int s = d.Box.SelectionStart, en = s + d.Box.SelectionLength;
        if (en > s && t[en - 1] == '\n') en--;
        a = s == 0 ? 0 : t.LastIndexOf('\n', s - 1) + 1;
        e = t.IndexOf('\n', en);
        if (e < 0) e = t.Length;
    }

    static void Replace(Doc d, int a, int e, string s)
    {
        d.Box.Select(a, e - a);
        d.Box.SelectedText = s;
    }

    void Duplicate()
    {
        var d = Cur; var b = d.Box;
        int a, e, s = b.SelectionStart, l = b.SelectionLength;
        LineRange(d, out a, out e);
        string blk = d.Text.Substring(a, e - a);
        Replace(d, e, e, "\n" + blk);
        b.Select(s + blk.Length + 1, l);
    }

    void DeleteLine()
    {
        var d = Cur;
        int a, e;
        LineRange(d, out a, out e);
        if (e < d.Text.Length) e++; else if (a > 0) a--;
        Replace(d, a, e, "");
    }

    void MoveLine(int dir)
    {
        var d = Cur; var b = d.Box; var t = d.Text;
        int a, e, s = b.SelectionStart, l = b.SelectionLength;
        LineRange(d, out a, out e);
        string blk = t.Substring(a, e - a);
        if (dir < 0)
        {
            if (a == 0) return;
            int pa = a >= 2 ? t.LastIndexOf('\n', a - 2) + 1 : 0;
            string prev = t.Substring(pa, a - 1 - pa);
            Replace(d, pa, e, blk + "\n" + prev);
            b.Select(s - prev.Length - 1, l);
        }
        else
        {
            if (e >= t.Length) return;
            int ne = t.IndexOf('\n', e + 1);
            if (ne < 0) ne = t.Length;
            string next = t.Substring(e + 1, ne - e - 1);
            Replace(d, a, ne, next + "\n" + blk);
            b.Select(s + next.Length + 1, l);
        }
    }

    void ChangeCase(bool upper)
    {
        var d = Cur; var b = d.Box;
        int s = b.SelectionStart, l = b.SelectionLength;
        if (l == 0) return;
        string t = d.Text.Substring(s, l);
        b.SelectedText = upper ? t.ToUpper() : t.ToLower();
        b.Select(s, l);
    }

    void SortLines()
    {
        var d = Cur;
        int a = 0, e = d.Text.Length;
        if (d.Box.SelectionLength > 0) LineRange(d, out a, out e);
        var lines = d.Text.Substring(a, e - a).Split('\n');
        Array.Sort(lines, StringComparer.CurrentCulture);
        Replace(d, a, e, string.Join("\n", lines));
        d.Box.Select(a, e - a);
    }

    void TrimTrailing()
    {
        var d = Cur;
        string t = d.Text, r = Regex.Replace(t, @"[ \t]+(?=\n|$)", "");
        if (r == t) return;
        int p = d.Box.SelectionStart;
        Replace(d, 0, t.Length, r);
        d.Box.Select(Math.Min(p, r.Length), 0);
    }

    void GoToLine()
    {
        var d = Cur;
        int max = d.Starts.Length, n;
        string s = Prompt(Tr.S("Ir a línea"), Tr.S("Número de línea (1-{0}):", max), (d.LineOf(d.Box.SelectionStart) + 1).ToString());
        if (s == null || !int.TryParse(s, out n)) return;
        GoTo(d, n, 0);
    }

    // Puts the caret on a 1-based line and column, unfolding the text first so it can be seen.
    void GoTo(Doc d, int line, int col)
    {
        UnfoldAll(d);
        string t = d.Text;
        int a = d.Starts[Math.Max(1, Math.Min(line, d.Starts.Length)) - 1], le = t.IndexOf('\n', a);
        d.Box.Select(Math.Min(a + Math.Max(0, col - 1), le < 0 ? t.Length : le), 0);
        d.Box.ScrollToCaret();
    }

    // ---------- find / replace ----------

    void ShowFind(bool replace)
    {
        var b = Cur.Box;
        if (b.SelectionLength > 0 && b.SelectionLength < 500)
        {
            string s = Cur.Text.Substring(b.SelectionStart, b.SelectionLength);
            if (s.IndexOf('\n') < 0) find.What.Text = s;
        }
        find.ReplaceMode = replace;
        find.Visible = true;
        var box = replace && find.What.Text.Length > 0 ? find.With : find.What;
        box.Focus();
        box.SelectAll();
        UpdateMarks(Cur);
    }

    public void CloseFind()
    {
        find.Visible = false;
        find.Info.Text = "";
        Cur.Box.Focus();
        UpdateMarks(Cur);
    }

    // Typing in the find box jumps to the first match from the caret on, as in a browser.
    void OnFindChanged()
    {
        if (!find.Visible) return;
        find.Info.Text = "";
        var b = Cur.Box;
        if (find.What.Text.Length > 0 && find.Build() != null)
        {
            b.Select(b.SelectionStart, 0);
            FindNext(false, true);
        }
        UpdateMarks(Cur);
    }

    // A pattern that takes too long says so instead of hanging the editor.
    bool Searching(Action a)
    {
        try { a(); return true; }
        catch (RegexMatchTimeoutException) { find.Info.Text = Tr.S("La búsqueda tarda demasiado. Prueba con una expresión más sencilla."); return false; }
    }

    public void FindNext(bool back) { FindNext(back, false); }

    void FindNext(bool back, bool quiet)
    {
        if (find.What.Text.Length == 0) { ShowFind(false); return; }
        var re = find.Build();
        if (re == null) return;
        var d = Cur; var b = d.Box; string t = d.Text;
        Match m = null;
        if (!Searching(() =>
        {
            if (!back)
            {
                int from = b.SelectionStart + b.SelectionLength;
                m = re.Match(t, from);
                if (m.Success && m.Length == 0 && m.Index == b.SelectionStart && from < t.Length) m = re.Match(t, from + 1);
                if (!m.Success) m = re.Match(t, 0);
            }
            else
            {
                var rr = new Regex(re.ToString(), re.Options | RegexOptions.RightToLeft, re.MatchTimeout);
                m = rr.Match(t, b.SelectionStart);
                if (!m.Success) m = rr.Match(t, t.Length);
            }
        })) return;
        if (!m.Success)
        {
            find.Info.Text = Tr.S("No se encontró.");
            if (!quiet) System.Media.SystemSounds.Beep.Play();
            return;
        }
        if (d.Folded) UnfoldAll(d);
        b.Select(m.Index, m.Length);
        b.ScrollToCaret();
        // "3 de 12", unless counting would be slow.
        find.Info.Text = "";
        if (t.Length <= 2000000)
            Searching(() =>
            {
                int total = 0, at = 0;
                for (var x = re.Match(t); x.Success; x = x.NextMatch()) { total++; if (x.Index <= m.Index) at = total; }
                find.Info.Text = Tr.S("{0} de {1}", at, total);
            });
    }

    public void ReplaceOne()
    {
        var re = find.Build();
        if (re == null) return;
        var d = Cur; var b = d.Box;
        Searching(() =>
        {
            var m = re.Match(d.Text, b.SelectionStart);
            if (m.Success && m.Index == b.SelectionStart && m.Length == b.SelectionLength) b.SelectedText = find.Replacement(m);
            FindNext(false);
        });
    }

    public void ReplaceAll()
    {
        var re = find.Build();
        if (re == null) return;
        int n = 0;
        if (Searching(() => n = ReplaceIn(Cur, re))) find.Info.Text = Tr.S("{0} reemplazos.", n);
    }

    // Replaces every match in d as one undo step; returns how many.
    int ReplaceIn(Doc d, Regex re)
    {
        int n = 0;
        string r = re.Replace(d.Text, m => { n++; return find.Replacement(m); });
        if (n == 0) return 0;
        int p = d.Box.SelectionStart;
        if (d.Folded) UnfoldAll(d);
        Replace(d, 0, d.Text.Length, r);
        d.Box.Select(Math.Min(p, r.Length), 0);
        return n;
    }

}
