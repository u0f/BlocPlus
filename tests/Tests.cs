// BlocPlus tests: plain asserts, no framework, so they build with the csc.exe that ships with Windows.
// Run test.cmd. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

static class Tests
{
    static int failed, passed;

    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) passed++;
        else { failed++; Console.WriteLine("FAIL " + name + (detail.Length > 0 ? ": " + detail : "")); }
    }

    static void Equal(string name, object expected, object actual)
    {
        Check(name, Equals(expected, actual), "expected [" + expected + "] got [" + actual + "]");
    }

    static void Throws<T>(string name, Action a) where T : Exception
    {
        try { a(); Check(name, false, "no exception"); }
        catch (T) { Check(name, true); }
    }

    static int Main()
    {
        Encodings();
        Json();
        Xml();
        Diff();
        Brackets();
        Highlighting();
        Markdown();
        Console.WriteLine(passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    static void Encodings()
    {
        int enc;
        Equal("utf8 bom text", "hé", Enc.Decode(new byte[] { 0xEF, 0xBB, 0xBF, 0x68, 0xC3, 0xA9 }, out enc));
        Equal("utf8 bom kind", 1, enc);
        Equal("utf16le", "hi", Enc.Decode(new byte[] { 0xFF, 0xFE, 0x68, 0, 0x69, 0 }, out enc));
        Equal("utf16le kind", 2, enc);
        Equal("utf16be", "hi", Enc.Decode(new byte[] { 0xFE, 0xFF, 0, 0x68, 0, 0x69 }, out enc));
        Equal("utf8 plain", "ñ", Enc.Decode(Encoding.UTF8.GetBytes("ñ"), out enc));
        Equal("utf8 plain kind", 0, enc);
        Enc.Decode(new byte[] { 0x68, 0xE9 }, out enc); // invalid UTF-8
        Equal("ansi fallback", 4, enc);
    }

    static void Json()
    {
        string src = "{\"b\":1.50,\"a\":[1,2,{}],\"s\":\"x,{y}\\\"\",\"e\":[]}";
        string pretty = TextTools.Json(src, "  ");
        Equal("json pretty", "{\n  \"b\": 1.50,\n  \"a\": [\n    1,\n    2,\n    {}\n  ],\n  \"s\": \"x,{y}\\\"\",\n  \"e\": []\n}", pretty);
        Equal("json compact roundtrip", src, TextTools.Json(pretty, null));
        string jsonc = "{\n  // note\n  \"a\": 1, // trailing\n  \"b\": 2 /* block */\n}";
        Equal("jsonc keeps comments", jsonc, TextTools.Json(jsonc, "  "));
        Equal("jsonc compact drops comments", "{\"a\":1,\"b\":2}", TextTools.Json(jsonc, null));
        Throws<FormatException>("json unbalanced", () => TextTools.Json("{\"a\":[1}", "  "));
        Throws<FormatException>("json unclosed", () => TextTools.Json("[1,2", "  "));
        Throws<FormatException>("json bad string", () => TextTools.Json("{\"a\n\":1}", "  "));
    }

    static void Xml()
    {
        Equal("xml pretty", "<?xml version=\"1.0\"?>\n<a>\n  <b x=\"1\">t</b>\n  <c />\n</a>", TextTools.Xml("<?xml version=\"1.0\"?><a><b x=\"1\">t</b><c/></a>", "  "));
        Equal("xml compact", "<a><b>t</b></a>", TextTools.Xml("<a>\n  <b>t</b>\n</a>", null));
        Throws<System.Xml.XmlException>("xml invalid", () => TextTools.Xml("<a><b></a>", "  "));
    }

    static void Diff()
    {
        Equal("diff equal", "", TextTools.Diff("a\nb", "a\nb", "x", "y"));
        string d = TextTools.Diff("1\n2\n3\n4\n5\n6\n7\n8\n9\n10", "1\n2\n3\n4\nfive\n6\n7\n8\n9\n10\n11", "old", "new");
        Equal("diff one hunk", "--- old\n+++ new\n@@ -2,9 +2,10 @@\n 2\n 3\n 4\n-5\n+five\n 6\n 7\n 8\n 9\n 10\n+11\n", d);
        string far = TextTools.Diff("a\n1\n2\n3\n4\n5\n6\n7\nz", "A\n1\n2\n3\n4\n5\n6\n7\nZ", "o", "n");
        Equal("diff two hunks", "--- o\n+++ n\n@@ -1,4 +1,4 @@\n-a\n+A\n 1\n 2\n 3\n@@ -6,4 +6,4 @@\n 5\n 6\n 7\n-z\n+Z\n", far);
        Check("diff renders", Markup.Diff(d).Contains("five"));
        Equal("diff from empty", "--- a\n+++ b\n@@ -1,1 +1,1 @@\n-\n+x\n", TextTools.Diff("", "x", "a", "b"));
    }

    static void Brackets()
    {
        string t = "f(a[1], {b}) )";
        Equal("bracket forward", 11, MainForm.MatchBracket(t, 1));
        Equal("bracket backward", 1, MainForm.MatchBracket(t, 11));
        Equal("bracket nested", 5, MainForm.MatchBracket(t, 3));
        Equal("bracket unmatched", -1, MainForm.MatchBracket(t, 13));
        Equal("not a bracket", -1, MainForm.MatchBracket(t, 0));
    }

    // The incremental cache must color exactly like a fresh scan, whatever the edits.
    static void Highlighting()
    {
        var rnd = new Random(7);
        string[] pieces = { "/* c */", "/*", "*/", "\"s\"", "\"", "'q'", "// x\n", "\n", " ", "int ", "x = 1;", "{", "}", "#a\n", "\"\"\"", "`t`", "key: v\n", "<a b=\"1\">", "-->" };
        foreach (var lang in Lang.All)
        {
            var cache = new SyntaxCache();
            string text = "";
            for (int step = 0; step < 300; step++)
            {
                int at = rnd.Next(text.Length + 1);
                if (text.Length > 0 && rnd.Next(3) == 0) text = text.Remove(at == text.Length ? at - 1 : at, 1);
                else text = text.Insert(at, pieces[rnd.Next(pieces.Length)]);
                int upTo = rnd.Next(text.Length + 1);
                cache.Update(text, lang, upTo);
                var fresh = new SyntaxCache();
                fresh.Update(text, lang, text.Length);
                if (Spans(cache, upTo) != Spans(fresh, upTo))
                {
                    Check("highlight cache " + lang.Name, false, "step " + step + " text [" + text.Replace("\n", "\\n") + "]");
                    break;
                }
            }
            Check("highlight cache " + lang.Name, true);
        }
    }

    static string Spans(SyntaxCache c, int upTo)
    {
        var sb = new StringBuilder();
        c.Each(0, upTo, (s, e, k) => sb.Append(s).Append('-').Append(Math.Min(e, upTo)).Append(k).Append(' '));
        return sb.ToString();
    }

    static void Markdown()
    {
        Check("markdown heading", Markup.Markdown("# Title").Contains("<h1>Title</h1>"), Markup.Markdown("# Title"));
        Check("markdown code", Markup.Markdown("`x<y`").Contains("x&lt;y"));
    }
}
