// Part of BlocPlus; see BlocPlus.cs. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

// Text transformations with no UI, so the tests can call them: JSON/XML formatting and line diffs.
static class TextTools
{
    // Re-indents JSON (or JSONC) token by token without parsing values, so numbers, key order and
    // comments stay exactly as written. A null indent compacts it to one line, dropping comments.
    public static string Json(string s, string indent)
    {
        var tokens = new List<string>();
        var sameLine = new List<bool>(); // a comment that follows the previous token on its line
        bool newline = true;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\n') { newline = true; continue; }
            if (char.IsWhiteSpace(c)) continue;
            int j = i + 1;
            if (c == '"')
            {
                while (j < s.Length && s[j] != '"' && s[j] != '\n') j += s[j] == '\\' ? 2 : 1;
                if (j >= s.Length || s[j] != '"') throw new FormatException(Tr.S("Falta cerrar una cadena en la línea {0}.", LineOf(s, i)));
                j++;
            }
            else if (c == '/' && j < s.Length && s[j] == '/') { j = s.IndexOf('\n', i); if (j < 0) j = s.Length; }
            else if (c == '/' && j < s.Length && s[j] == '*') { j = s.IndexOf("*/", i + 2); j = j < 0 ? s.Length : j + 2; }
            else if ("{}[],:".IndexOf(c) < 0)
                while (j < s.Length && !char.IsWhiteSpace(s[j]) && "{}[],:\"/".IndexOf(s[j]) < 0) j++;
            tokens.Add(s.Substring(i, j - i));
            sameLine.Add(!newline);
            newline = false;
            i = j - 1;
        }

        var sb = new StringBuilder();
        var open = new Stack<char>();
        bool fresh = true, pending = false; // at a line start whose indentation is not written yet; a line break is due
        Action<string> put = x =>
        {
            if (fresh && indent != null) for (int k = 0; k < open.Count; k++) sb.Append(indent);
            fresh = false;
            sb.Append(x);
        };
        Action br = () => { if (indent != null) { sb.Append('\n'); fresh = true; } };
        for (int i = 0; i < tokens.Count; i++)
        {
            string t = tokens[i];
            bool comment = t.StartsWith("//") || t.StartsWith("/*");
            if (comment && indent == null) continue;
            if (comment && sameLine[i] && !fresh) { sb.Append(' ').Append(t); if (t[1] == '/') pending = true; continue; }
            if (pending) { br(); pending = false; }
            char c = t[0];
            if (comment) { put(t); pending = t[1] == '/' || i + 1 < tokens.Count && !sameLine[i + 1]; }
            else if (c == '{' || c == '[')
            {
                char close = c == '{' ? '}' : ']';
                if (i + 1 < tokens.Count && tokens[i + 1] == close.ToString()) { put(t + close); i++; continue; }
                put(t);
                open.Push(close);
                pending = true;
            }
            else if (c == '}' || c == ']')
            {
                if (open.Count == 0 || open.Peek() != c) throw new FormatException(Tr.S("Sobra un «{0}» en la línea {1}.", c, LineAt(s, tokens, i)));
                open.Pop();
                if (!fresh) br();
                put(t);
            }
            else if (c == ',') { put(t); pending = true; }
            else if (c == ':') put(indent == null ? ":" : ": ");
            else put(t);
        }
        if (open.Count > 0) throw new FormatException(Tr.S("Falta cerrar un «{0}».", open.Peek() == '}' ? '{' : '['));
        if (pending && indent != null && tokens.Count > 0 && tokens[tokens.Count - 1].StartsWith("//")) br();
        return sb.ToString();
    }

    static int LineOf(string s, int i) { int n = 1; for (int k = 0; k < i; k++) if (s[k] == '\n') n++; return n; }

    // Line of the i-th token, found again by scanning (only for error messages).
    static int LineAt(string s, List<string> tokens, int i)
    {
        int p = 0;
        for (int k = 0; k <= i; k++) { p = s.IndexOf(tokens[k], p, StringComparison.Ordinal); if (k < i) p += tokens[k].Length; }
        return LineOf(s, Math.Max(0, p));
    }

    // Re-indents XML, or compacts it with a null indent. Entities stay unexpanded beyond a small limit.
    public static string Xml(string s, string indent)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null, MaxCharactersFromEntities = 1000000, IgnoreWhitespace = true };
        XDocument doc;
        using (var r = XmlReader.Create(new StringReader(s), settings)) doc = XDocument.Load(r);
        string decl = doc.Declaration == null ? "" : doc.Declaration + (indent == null ? "" : "\n");
        if (indent == null) return decl + doc.ToString(SaveOptions.DisableFormatting);
        var sw = new StringWriter();
        using (var w = XmlWriter.Create(sw, new XmlWriterSettings { Indent = true, IndentChars = indent, NewLineChars = "\n", OmitXmlDeclaration = true }))
            doc.Save(w);
        return decl + sw;
    }

    // Unified diff of two texts by lines, with three lines of context; "" when they are equal.
    // Lines in common at both ends are skipped first, and the rest is compared by longest common
    // subsequence when that fits in memory, else shown as removed and added whole.
    public static string Diff(string a, string b, string nameA, string nameB)
    {
        if (a == b) return "";
        string[] x = a.Split('\n'), y = b.Split('\n');
        int n = x.Length, m = y.Length, pre = 0, suf = 0;
        while (pre < n && pre < m && x[pre] == y[pre]) pre++;
        while (suf < n - pre && suf < m - pre && x[n - 1 - suf] == y[m - 1 - suf]) suf++;

        var ops = new List<char>();
        for (int k = 0; k < pre; k++) ops.Add(' ');
        int N = n - pre - suf, M = m - pre - suf;
        if ((long)(N + 1) * (M + 1) <= 4000000)
        {
            var len = new int[N + 1, M + 1];
            for (int i = N - 1; i >= 0; i--)
                for (int j = M - 1; j >= 0; j--)
                    len[i, j] = x[pre + i] == y[pre + j] ? len[i + 1, j + 1] + 1 : Math.Max(len[i + 1, j], len[i, j + 1]);
            int p = 0, q = 0;
            while (p < N || q < M)
            {
                if (p < N && q < M && x[pre + p] == y[pre + q]) { ops.Add(' '); p++; q++; }
                else if (p < N && (q == M || len[p + 1, q] >= len[p, q + 1])) { ops.Add('-'); p++; }
                else { ops.Add('+'); q++; }
            }
        }
        else
        {
            for (int k = 0; k < N; k++) ops.Add('-');
            for (int k = 0; k < M; k++) ops.Add('+');
        }
        for (int k = 0; k < suf; k++) ops.Add(' ');

        // Where each op stands in either text, to number the hunks.
        var xi = new int[ops.Count + 1];
        var yi = new int[ops.Count + 1];
        for (int k = 0; k < ops.Count; k++)
        {
            xi[k + 1] = xi[k] + (ops[k] != '+' ? 1 : 0);
            yi[k + 1] = yi[k] + (ops[k] != '-' ? 1 : 0);
        }
        var sb = new StringBuilder("--- " + nameA + "\n+++ " + nameB + "\n");
        const int ctx = 3;
        for (int k = 0; k < ops.Count; )
        {
            if (ops[k] == ' ') { k++; continue; }
            int start = Math.Max(0, k - ctx), last = k;
            for (int j = k; j < ops.Count && j - last <= 2 * ctx; j++) if (ops[j] != ' ') last = j;
            int end = Math.Min(ops.Count, last + ctx + 1);
            int oc = xi[end] - xi[start], nc = yi[end] - yi[start];
            sb.Append("@@ -").Append(oc == 0 ? xi[start] : xi[start] + 1).Append(',').Append(oc)
              .Append(" +").Append(nc == 0 ? yi[start] : yi[start] + 1).Append(',').Append(nc).Append(" @@\n");
            for (int j = start; j < end; j++) sb.Append(ops[j]).Append(ops[j] == '+' ? y[yi[j]] : x[xi[j]]).Append('\n');
            k = end;
        }
        return sb.ToString();
    }
}
