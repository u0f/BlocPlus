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

// Small converters for the live preview: Markdown, reStructuredText and CSV to HTML.
// They cover the common syntax, not every corner of each spec.
static class Markup
{
    public static bool Strict; // text from the web: raw HTML is shown as text, never run

    static string Esc(string s) { return WebUtility.HtmlEncode(s); }

    // Link/image targets: web, mail, local files and relative paths only, so "javascript:" and friends never reach the browser.
    static string Link(string u)
    {
        u = Regex.Replace(u, @"[\s\x00-\x1f]", ""); // IE drops these, so "java\tscript:" would still run
        var m = Regex.Match(u, @"^([^/?#]*?):");
        return m.Success && !Regex.IsMatch(m.Groups[1].Value, @"^(?:https?|mailto|file|[a-zA-Z])$", RegexOptions.IgnoreCase) ? "#" : Esc(u);
    }

    static int Indent(string l) { int n = 0; while (n < l.Length && (l[n] == ' ' || l[n] == '\t')) n++; return n; }

    // Generated tags are parked in placeholders so later emphasis rules can't touch their URLs.
    static string Stash(List<string> keep, string html) { keep.Add(html); return "\u0001" + (keep.Count - 1) + "\u0001"; }
    static string Unstash(List<string> keep, string s) { return Regex.Replace(s, "\u0001(\\d+)\u0001", m => keep[int.Parse(m.Groups[1].Value)]); }

    static string Emphasis(string s)
    {
        s = Regex.Replace(s, @"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", "<strong>$2</strong>");
        s = Regex.Replace(s, @"(?<![\w*])\*(?=\S)(.+?)(?<=\S)\*(?!\*)|(?<!\w)_(?=\S)(.+?)(?<=\S)_(?!\w)",
            m => "<em>" + (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) + "</em>");
        return Regex.Replace(s, @"~~(?=\S)(.+?)(?<=\S)~~", "<del>$1</del>");
    }

    public static string Inline(string s)
    {
        var keep = new List<string>();
        s = Regex.Replace(s, @"(`+)(.+?)\1", m => Stash(keep, "<code>" + Esc(m.Groups[2].Value.Trim()) + "</code>"));
        s = Regex.Replace(s, @"!\[([^\]]*)\]\(([^)\s]+)[^)]*\)", m => Stash(keep, "<img src=\"" + Link(m.Groups[2].Value) + "\" alt=\"" + Esc(m.Groups[1].Value) + "\">"));
        s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)\s]+)[^)]*\)", m => Stash(keep, "<a href=\"" + Link(m.Groups[2].Value) + "\">" + Emphasis(Esc(m.Groups[1].Value)) + "</a>"));
        s = Regex.Replace(s, @"<((?:https?|mailto):[^>\s]+)>", m => Stash(keep, "<a href=\"" + Esc(m.Groups[1].Value) + "\">" + Esc(m.Groups[1].Value) + "</a>"));
        return Unstash(keep, Emphasis(Esc(s)));
    }

    // ---------- Markdown ----------

    static readonly Regex ListItem = new Regex(@"^(\s*)([-*+]|\d+[.)])\s+(.*)$");
    static readonly Regex Fence = new Regex(@"^\s{0,3}(```+|~~~+)");
    static readonly Regex Heading = new Regex(@"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$");
    static readonly Regex Rule = new Regex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$");
    static readonly Regex TableSep = new Regex(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$");

    public static string Markdown(string text) { return Md(text.Split('\n').ToList()); }

    static bool StartsBlock(string l)
    {
        return Fence.IsMatch(l) || Heading.IsMatch(l) || Rule.IsMatch(l) || Regex.IsMatch(l, @"^\s{0,3}>") || ListItem.IsMatch(l);
    }

    static string Md(List<string> lines)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < lines.Count)
        {
            string l = lines[i];
            Match m;
            if (l.Trim().Length == 0) { i++; continue; }
            if ((m = Fence.Match(l)).Success)
            {
                string fence = m.Groups[1].Value;
                var code = new StringBuilder();
                for (i++; i < lines.Count && !lines[i].TrimStart().StartsWith(fence); i++) code.Append(lines[i]).Append('\n');
                i++;
                sb.Append("<pre><code>").Append(Esc(code.ToString())).Append("</code></pre>\n");
            }
            else if ((m = Heading.Match(l)).Success)
            {
                sb.AppendFormat("<h{0}>{1}</h{0}>\n", m.Groups[1].Length, Inline(m.Groups[2].Value));
                i++;
            }
            else if (Rule.IsMatch(l)) { sb.Append("<hr>\n"); i++; }
            else if (Regex.IsMatch(l, @"^\s{0,3}>"))
            {
                var quote = new List<string>();
                while (i < lines.Count && lines[i].Trim().Length > 0) quote.Add(Regex.Replace(lines[i++], @"^\s{0,3}>\s?", ""));
                sb.Append("<blockquote>").Append(Md(quote)).Append("</blockquote>\n");
            }
            else if (Regex.IsMatch(l, @"^\s{0,3}<[A-Za-z!/]")) // raw HTML block
            {
                var raw = new StringBuilder();
                while (i < lines.Count && lines[i].Trim().Length > 0) raw.Append(lines[i++]).Append('\n');
                sb.Append(Strict ? "<pre><code>" + Esc(raw.ToString()) + "</code></pre>\n" : raw.ToString());
            }
            else if (ListItem.IsMatch(l)) i = MdList(lines, i, sb);
            else if (l.Contains("|") && i + 1 < lines.Count && lines[i + 1].Contains("-") && TableSep.IsMatch(lines[i + 1])) i = MdTable(lines, i, sb);
            else if (Indent(l) >= 4)
            {
                var code = new StringBuilder();
                for (; i < lines.Count && (lines[i].Trim().Length == 0 || Indent(lines[i]) >= 4); i++)
                    code.Append(lines[i].Length >= 4 ? lines[i].Substring(lines[i][0] == '\t' ? 1 : 4) : "").Append('\n');
                sb.Append("<pre><code>").Append(Esc(code.ToString().TrimEnd('\n'))).Append("</code></pre>\n");
            }
            else
            {
                var para = new List<string>();
                int level = 0;
                while (i < lines.Count && lines[i].Trim().Length > 0)
                {
                    if (para.Count > 0 && Regex.IsMatch(lines[i], @"^\s{0,3}(=+|-+)\s*$")) { level = lines[i].Trim()[0] == '=' ? 1 : 2; i++; break; }
                    if (para.Count > 0 && StartsBlock(lines[i])) break;
                    para.Add(lines[i].EndsWith("  ") ? lines[i].Trim() + "\u0002" : lines[i].Trim());
                    i++;
                }
                string body = Inline(string.Join("\n", para)).Replace("\u0002", "<br>");
                if (level > 0) sb.AppendFormat("<h{0}>{1}</h{0}>\n", level, body);
                else sb.Append("<p>").Append(body).Append("</p>\n");
            }
        }
        return sb.ToString();
    }

    static int MdList(List<string> lines, int i, StringBuilder sb)
    {
        var first = ListItem.Match(lines[i]);
        int indent = first.Groups[1].Length;
        bool ordered = char.IsDigit(first.Groups[2].Value[0]);
        int start = ordered ? int.Parse(first.Groups[2].Value.TrimEnd('.', ')')) : 1;
        sb.Append(ordered ? (start != 1 ? "<ol start=\"" + start + "\">" : "<ol>") : "<ul>");
        while (i < lines.Count)
        {
            var m = ListItem.Match(lines[i]);
            if (!m.Success || m.Groups[1].Length != indent) break;
            int content = indent + m.Groups[2].Length + 1;
            var item = new List<string> { m.Groups[3].Value };
            bool loose = false;
            for (i++; i < lines.Count; i++)
            {
                string l = lines[i];
                if (l.Trim().Length == 0)
                {
                    if (i + 1 < lines.Count && lines[i + 1].Trim().Length > 0 && Indent(lines[i + 1]) >= content) { item.Add(""); loose = true; continue; }
                    break;
                }
                var sub = ListItem.Match(l);
                if (sub.Success && sub.Groups[1].Length <= indent) break;
                if (!sub.Success && Indent(l) < content && item[item.Count - 1].Length == 0) break;
                item.Add(l.Substring(Math.Min(Indent(l), content)));
            }
            string inner = Md(item);
            if (!loose) inner = Regex.Replace(inner, @"^<p>(.*?)</p>", "$1", RegexOptions.Singleline);
            inner = Regex.Replace(inner, @"^\[( |x|X)\] ", c => "<input type=\"checkbox\" disabled" + (c.Groups[1].Value == " " ? "" : " checked") + "> ");
            sb.Append("<li>").Append(inner.Trim()).Append("</li>\n");
        }
        sb.Append(ordered ? "</ol>\n" : "</ul>\n");
        return i;
    }

    static string[] Cells(string l)
    {
        l = l.Trim();
        if (l.StartsWith("|")) l = l.Substring(1);
        if (l.EndsWith("|")) l = l.Substring(0, l.Length - 1);
        return l.Split('|').Select(c => c.Trim()).ToArray();
    }

    static int MdTable(List<string> lines, int i, StringBuilder sb)
    {
        var head = Cells(lines[i]);
        var align = Cells(lines[i + 1]).Select(a => a.StartsWith(":") && a.EndsWith(":") ? " align=\"center\"" : a.EndsWith(":") ? " align=\"right\"" : "").ToArray();
        Func<int, string> al = k => k < align.Length ? align[k] : "";
        sb.Append("<table><thead><tr>");
        for (int k = 0; k < head.Length; k++) sb.Append("<th" + al(k) + ">" + Inline(head[k]) + "</th>");
        sb.Append("</tr></thead><tbody>");
        for (i += 2; i < lines.Count && lines[i].Trim().Length > 0 && lines[i].Contains("|"); i++)
        {
            var row = Cells(lines[i]);
            sb.Append("<tr>");
            for (int k = 0; k < head.Length; k++) sb.Append("<td" + al(k) + ">" + (k < row.Length ? Inline(row[k]) : "") + "</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>\n");
        return i;
    }

    // ---------- reStructuredText ----------

    static readonly Regex Adornment = new Regex(@"^([=\-~^""'`#*+:._])\1{2,}\s*$");

    static string RstInline(string s)
    {
        var keep = new List<string>();
        s = Regex.Replace(s, @"``(.+?)``", m => Stash(keep, "<code>" + Esc(m.Groups[1].Value) + "</code>"));
        s = Regex.Replace(s, @"`([^`<]+?)\s*<([^>]+)>`__?", m => Stash(keep, "<a href=\"" + Link(m.Groups[2].Value) + "\">" + Esc(m.Groups[1].Value) + "</a>"));
        s = Regex.Replace(s, @":[\w-]+:`([^`]+)`", m => Stash(keep, "<code>" + Esc(m.Groups[1].Value) + "</code>"));
        s = Regex.Replace(s, @"`([^`]+)`_?", m => Stash(keep, "<em>" + Esc(m.Groups[1].Value) + "</em>"));
        s = Regex.Replace(s, @"\bhttps?://[^\s<>""]+[^\s<>"".,;:)]", m => Stash(keep, "<a href=\"" + Esc(m.Value) + "\">" + Esc(m.Value) + "</a>"));
        s = Esc(s);
        s = Regex.Replace(s, @"\*\*(?=\S)(.+?)(?<=\S)\*\*", "<strong>$1</strong>");
        s = Regex.Replace(s, @"(?<![\w*])\*(?=\S)(.+?)(?<=\S)\*(?!\*)", "<em>$1</em>");
        return Unstash(keep, s);
    }

    public static string Rst(string text) { return RstBlocks(text.Split('\n'), new List<string>()); }

    // Lines indented deeper than `indent` that follow position i, dedented.
    static List<string> IndentedBlock(string[] lines, ref int i, int indent)
    {
        var block = new List<string>();
        while (i < lines.Length && (lines[i].Trim().Length == 0 || Indent(lines[i]) > indent)) block.Add(lines[i++]);
        while (block.Count > 0 && block[block.Count - 1].Trim().Length == 0) block.RemoveAt(block.Count - 1);
        int cut = block.Where(b => b.Trim().Length > 0).Select(Indent).DefaultIfEmpty(0).Min();
        return block.Select(b => b.Length >= cut ? b.Substring(cut) : "").ToList();
    }

    static string RstBlocks(string[] lines, List<string> levels)
    {
        var sb = new StringBuilder();
        Func<string, string, string> heading = (key, title) =>
        {
            if (!levels.Contains(key)) levels.Add(key);
            int n = Math.Min(levels.IndexOf(key) + 1, 6);
            return "<h" + n + ">" + RstInline(title) + "</h" + n + ">\n";
        };
        int i = 0;
        while (i < lines.Length)
        {
            string l = lines[i];
            Match m;
            if (l.Trim().Length == 0) { i++; continue; }
            if (Adornment.IsMatch(l) && i + 2 < lines.Length && lines[i + 1].Trim().Length > 0 && Adornment.IsMatch(lines[i + 2]))
            {
                sb.Append(heading("o" + l[0], lines[i + 1].Trim()));
                i += 3;
            }
            else if (i + 1 < lines.Length && Indent(l) == 0 && !Adornment.IsMatch(l) && Adornment.IsMatch(lines[i + 1]) && lines[i + 1].TrimEnd().Length >= l.TrimEnd().Length)
            {
                sb.Append(heading("u" + lines[i + 1][0], l.Trim()));
                i += 2;
            }
            else if (Adornment.IsMatch(l)) { sb.Append("<hr>\n"); i++; }
            else if (Regex.IsMatch(l, @"^\.\.\s+(code-block|code|sourcecode)::"))
            {
                i++;
                var block = IndentedBlock(lines, ref i, 0).SkipWhile(b => b.StartsWith(":") || b.Trim().Length == 0);
                sb.Append("<pre><code>").Append(Esc(string.Join("\n", block))).Append("</code></pre>\n");
            }
            else if ((m = Regex.Match(l, @"^\.\.\s+(note|warning|tip|important|caution|attention|hint|danger|error|seealso|admonition)::\s*(.*)$", RegexOptions.IgnoreCase)).Success)
            {
                i++;
                var body = IndentedBlock(lines, ref i, 0);
                if (m.Groups[2].Value.Length > 0) body.Insert(0, m.Groups[2].Value);
                string title = char.ToUpper(m.Groups[1].Value[0]) + m.Groups[1].Value.Substring(1).ToLower();
                sb.Append("<div class=\"admonition\"><p class=\"admonition-title\">").Append(title).Append("</p>").Append(RstBlocks(body.ToArray(), levels)).Append("</div>\n");
            }
            else if ((m = Regex.Match(l, @"^\.\.\s+(image|figure)::\s*(\S+)")).Success)
            {
                i++;
                IndentedBlock(lines, ref i, 0);
                sb.Append("<p><img src=\"").Append(Link(m.Groups[2].Value)).Append("\"></p>\n");
            }
            else if (l.StartsWith("..")) { i++; IndentedBlock(lines, ref i, 0); } // comments, targets, other directives
            else if ((m = Regex.Match(l, @"^(\s*)([-*+•]|\d+[.)]|#[.)])\s+")).Success)
            {
                int indent = m.Groups[1].Length;
                bool ordered = !"-*+•".Contains(m.Groups[2].Value);
                sb.Append(ordered ? "<ol>" : "<ul>");
                while (i < lines.Length && (m = Regex.Match(lines[i], @"^(\s*)([-*+•]|\d+[.)]|#[.)])\s+(.*)$")).Success && m.Groups[1].Length == indent)
                {
                    int content = lines[i].Length - m.Groups[3].Length;
                    var item = new List<string> { m.Groups[3].Value };
                    i++;
                    item.AddRange(IndentedBlock(lines, ref i, content - 1));
                    string inner = RstBlocks(item.ToArray(), levels);
                    sb.Append("<li>").Append(Regex.Replace(inner, @"^<p>(.*?)</p>", "$1", RegexOptions.Singleline).Trim()).Append("</li>\n");
                    while (i < lines.Length && lines[i].Trim().Length == 0 && i + 1 < lines.Length && Regex.IsMatch(lines[i + 1], @"^\s*([-*+•]|\d+[.)]|#[.)])\s+")) i++;
                }
                sb.Append(ordered ? "</ol>\n" : "</ul>\n");
            }
            else if (Indent(l) > 0)
            {
                var block = IndentedBlock(lines, ref i, -1);
                sb.Append("<blockquote>").Append(RstBlocks(block.ToArray(), levels)).Append("</blockquote>\n");
            }
            else
            {
                var para = new List<string>();
                while (i < lines.Length && lines[i].Trim().Length > 0 && Indent(lines[i]) == 0)
                {
                    if (para.Count > 0 && i + 1 < lines.Length && Adornment.IsMatch(lines[i + 1])) break;
                    para.Add(lines[i++].Trim());
                }
                string p = string.Join(" ", para);
                bool literal = p.EndsWith("::");
                if (literal) p = p == "::" ? "" : p.EndsWith(" ::") ? p.Substring(0, p.Length - 3) : p.Substring(0, p.Length - 1);
                if (p.Length > 0) sb.Append("<p>").Append(RstInline(p)).Append("</p>\n");
                if (literal)
                {
                    while (i < lines.Length && lines[i].Trim().Length == 0) i++;
                    sb.Append("<pre><code>").Append(Esc(string.Join("\n", IndentedBlock(lines, ref i, 0)))).Append("</code></pre>\n");
                }
            }
        }
        return sb.ToString();
    }

    // ---------- CSV / TSV ----------

    // LRC lyrics: one <p data-t="seconds"> per timed line, sorted, after a title/artist heading from the header tags.
    public static string Lrc(string text)
    {
        var lines = new List<KeyValuePair<double, string>>();
        string ti = null, ar = null;
        double offset = 0;
        foreach (var raw in text.Split('\n'))
        {
            var m = Regex.Match(raw, @"^\s*((?:\[\d+:\d+(?:[.:]\d+)?\]\s*)+)(.*)");
            if (m.Success)
            {
                string words = Regex.Replace(m.Groups[2].Value, @"<\d+:\d+(?:[.:]\d+)?>", "").Trim(); // enhanced LRC word times
                foreach (Match t in Regex.Matches(m.Groups[1].Value, @"\[(\d+):(\d+(?:[.:]\d+)?)\]"))
                    lines.Add(new KeyValuePair<double, string>(int.Parse(t.Groups[1].Value) * 60 + double.Parse(t.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture), words));
                continue;
            }
            m = Regex.Match(raw, @"^\s*\[(ti|ar|offset):(.*)\]", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            string k = m.Groups[1].Value.ToLowerInvariant(), v = m.Groups[2].Value.Trim();
            if (k == "ti") ti = v;
            else if (k == "ar") ar = v;
            else double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
        }
        var sb = new StringBuilder();
        if (ti != null || ar != null) sb.Append("<h2>").Append(Esc(string.Join(" · ", new[] { ti, ar }.Where(x => !string.IsNullOrEmpty(x))))).Append("</h2>");
        foreach (var l in lines.OrderBy(x => x.Key))
            sb.Append("<p data-t=\"").Append(Math.Max(0, l.Key - offset / 1000).ToString(CultureInfo.InvariantCulture)).Append("\">")
              .Append(l.Value.Length > 0 ? Esc(l.Value) : "♪").Append("</p>");
        return sb.ToString();
    }

    public static string Csv(string text, char sep)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length && rows.Count < 5000; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        if (rows.Count == 0) return "";
        var sb = new StringBuilder("<table><thead><tr>");
        foreach (var h in rows[0]) sb.Append("<th>").Append(Esc(h)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var r in rows.Skip(1))
        {
            sb.Append("<tr>");
            foreach (var v in r) sb.Append("<td>").Append(Esc(v)).Append("</td>");
            sb.Append("</tr>");
        }
        return sb.Append("</tbody></table>").ToString();
    }

    // ---------- diff / patch ----------

    static readonly Regex Hunk = new Regex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");

    // One box per file with old/new line numbers; text around the files (a patch's mail header) stays as is.
    public static string Diff(string text)
    {
        var sb = new StringBuilder();
        var plain = new StringBuilder();
        var meta = new List<string>();
        string name = null; // file whose box is pending or open
        bool table = false;
        int o = 0, n = 0, oLeft = 0, nLeft = 0;
        Action<string, object, object, string> row = (cls, a, b, s) => sb.Append("<tr class=\"").Append(cls).Append("\"><td class=\"ln\">").Append(a)
            .Append("</td><td class=\"ln\">").Append(b).Append("</td><td>").Append(Esc(s)).Append("</td></tr>");
        Action flush = () =>
        {
            if (plain.ToString().Trim().Length > 0) sb.Append("<pre>").Append(Esc(plain.ToString().Trim('\n'))).Append("</pre>");
            plain.Clear();
        };
        Action open = () =>
        {
            if (name == null || table) return;
            flush();
            sb.Append("<div class=\"df\"><div class=\"dh\">").Append(Esc(name));
            foreach (var x in meta) sb.Append("<span>").Append(Esc(x)).Append("</span>");
            sb.Append("</div><table class=\"dt\">");
            table = true;
        };
        Action close = () => { open(); if (table) sb.Append("</table></div>"); table = false; name = null; meta.Clear(); };
        foreach (var l in text.Split('\n'))
        {
            if (oLeft > 0 || nLeft > 0)
            {
                char c = l.Length > 0 ? l[0] : ' ';
                if (c == '+') { row("da", "", n++, l); nLeft--; }
                else if (c == '-') { row("dd", o++, "", l); oLeft--; }
                else if (c == '\\') row("dn", "", "", l);
                else { row("", o++, n++, l); oLeft--; nLeft--; }
                continue;
            }
            var m = Hunk.Match(l);
            if (m.Success)
            {
                if (name == null) name = "";
                open();
                o = int.Parse(m.Groups[1].Value); oLeft = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
                n = int.Parse(m.Groups[3].Value); nLeft = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 1;
                row("dk", "", "", l);
            }
            else if (l.StartsWith("diff ")) { close(); int b = l.LastIndexOf(" b/"); name = b > 0 ? l.Substring(b + 3) : l; }
            else if (l.StartsWith("--- ") || l.StartsWith("+++ "))
            {
                if (table || name == null) { close(); name = ""; }
                string f = l.Substring(4).Split('\t')[0];
                if (f != "/dev/null") name = Regex.Replace(f, "^[ab]/", "");
            }
            else if (l.StartsWith("\\") && table) row("dn", "", "", l);
            else if (name != null && !table) { if (l.Length > 0 && !l.StartsWith("index ")) meta.Add(l); }
            else { close(); plain.Append(l).Append('\n'); }
        }
        close();
        flush();
        return sb.ToString();
    }
}
