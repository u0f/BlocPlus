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

static class Enc
{
    public static readonly string[] Names = { "UTF-8", Tr.S("UTF-8 con BOM"), "UTF-16 LE", "UTF-16 BE", "ANSI" };

    public static Encoding Get(int i)
    {
        switch (i)
        {
            case 1: return new UTF8Encoding(true);
            case 2: return new UnicodeEncoding(false, true);
            case 3: return new UnicodeEncoding(true, true);
            case 4: return Encoding.Default;
            default: return new UTF8Encoding(false);
        }
    }

    public static string Decode(byte[] b, out int enc)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { enc = 1; return Encoding.UTF8.GetString(b, 3, b.Length - 3); }
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { enc = 2; return Encoding.Unicode.GetString(b, 2, b.Length - 2); }
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { enc = 3; return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2); }
        try { enc = 0; return new UTF8Encoding(false, true).GetString(b); }
        catch (DecoderFallbackException) { enc = 4; return Encoding.Default.GetString(b); }
    }
}

// Syntax highlighting: one regex per language, each named group's first letter is its
// color kind (c comment, s string, k keyword, n number, t type/tag, a attribute/variable).
class Lang
{
    public string Name;
    public string[] Keys;
    string pattern;
    bool ignoreCase;
    Regex rx;

    public Regex Rx
    {
        get
        {
            if (rx == null)
                rx = new Regex(pattern, RegexOptions.Multiline | RegexOptions.ExplicitCapture | RegexOptions.Compiled | (ignoreCase ? RegexOptions.IgnoreCase : 0));
            return rx;
        }
    }

    public char KindOf(Match m)
    {
        for (int g = 1; g < m.Groups.Count; g++)
            if (m.Groups[g].Success) return rx.GroupNameFromNumber(g)[0];
        return 'a';
    }

    const string DQ = @"""(?:\\.|[^""\\\n])*""?", SQ = @"'(?:\\.|[^'\\\n])*'?", BT = @"`(?:\\.|[^`\\])*`?",
        CL = @"//[^\n]*|/\*[\s\S]*?(?:\*/|\z)", HASH = @"#[^\n]*",
        NUM = @"\b(?:0[xX][0-9a-fA-F_]+|\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?)[a-zA-Z]*\b";

    static string W(string words) { return @"\b(?:" + words.Replace(" ", "|") + @")\b"; }

    static Lang L(string name, string keys, bool ignoreCase, params string[] parts)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i += 2)
            sb.Append(i == 0 ? "" : "|").Append("(?<").Append(parts[i]).Append(i).Append('>').Append(parts[i + 1]).Append(')');
        return new Lang { Name = name, Keys = keys.Split(' '), pattern = sb.ToString(), ignoreCase = ignoreCase };
    }

    public static readonly Lang[] All =
    {
        L("C#", "cs csx", false, "c", CL, "s", @"@""(?:""""|[^""])*""?|\$?" + DQ + "|" + SQ, "t", @"^[ \t]*#[ \t]*\w+", "n", NUM,
          "k", W("abstract as async await base bool break byte case catch char checked class const continue decimal default delegate do double dynamic else enum event explicit extern false finally fixed float for foreach get goto if implicit in init int interface internal is lock long nameof namespace new null object operator out override params partial private protected public readonly record ref return sbyte sealed set short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile when where while yield")),
        L("C / C++", "c h cpp hpp cc cxx hh hxx ino", false, "c", CL, "s", DQ + "|" + SQ, "t", @"^[ \t]*#[ \t]*\w+", "n", NUM,
          "k", W("auto bool break case catch char class const constexpr continue default delete do double else enum explicit extern false float for friend goto if inline int long mutable namespace new noexcept nullptr operator private protected public register return short signed sizeof static static_cast struct switch template this throw true try typedef typename union unsigned using virtual void volatile while")),
        L("Java / Kotlin / Swift", "java kt kts scala groovy gradle dart swift", false, "c", CL, "s", DQ + "|" + SQ, "t", @"@\w+", "n", NUM,
          "k", W("abstract assert boolean break byte case catch char class const continue def default do double else enum extends extension false final finally float for fun func guard if implements import in instanceof int interface is let long native new null object override package private protected protocol public return self short static struct super switch synchronized this throw throws true try val var void volatile when while")),
        L("JavaScript / TypeScript", "js jsx mjs cjs ts tsx", false, "c", CL, "s", DQ + "|" + SQ + "|" + BT, "n", NUM,
          "k", W("any as async await boolean break case catch class const continue debugger default delete do else enum export extends false finally for from function get if implements import in instanceof interface let never new null number of private protected public readonly return set static string super switch this throw true try type typeof undefined unknown var void while with yield")),
        L("Go", "go", false, "c", CL, "s", DQ + "|" + BT + "|" + SQ, "n", NUM,
          "k", W("break case chan const continue default defer else fallthrough false for func go goto if import interface iota map nil package range return select struct switch true type var")),
        L("Rust", "rs", false, "c", CL, "s", DQ + @"|'(?:\\.|[^'\\\n])'", "t", @"\b\w+!|#!?\[[^\]\n]*\]", "n", NUM,
          "k", W("as async await break const continue crate dyn else enum extern false fn for if impl in let loop match mod move mut pub ref return self Self static struct super trait true type unsafe use where while")),
        L("PHP", "php", false, "c", CL + "|" + HASH, "s", DQ + "|" + SQ, "a", @"\$\w+", "n", NUM,
          "k", W("abstract and array as break case catch class clone const continue declare default do echo else elseif empty extends false final finally fn for foreach function global if implements include include_once instanceof interface isset list match namespace new null or print private protected public require require_once return static switch throw trait true try unset use var while yield")),
        L("Python", "py pyw pyi", false, "c", HASH, "s", @"[rRbBfFuU]{0,2}(?:""""""[\s\S]*?(?:""""""|\z)|'''[\s\S]*?(?:'''|\z))|" + DQ + "|" + SQ, "t", @"^[ \t]*@[\w.]+", "n", NUM,
          "k", W("False None True and as assert async await break case class continue def del elif else except finally for from global if import in is lambda match nonlocal not or pass raise return self try while with yield")),
        L("Ruby", "rb rake gemspec", false, "c", HASH, "s", DQ + "|" + SQ, "a", @"[@$]\w+|:\w+", "n", NUM,
          "k", W("alias and begin break case class def defined do else elsif end ensure false for if in module next nil not or redo require rescue retry return self super then true undef unless until when while yield")),
        L("Shell", "sh bash zsh", false, "c", @"(?<![\w$])#[^\n]*", "s", DQ + "|" + SQ, "a", @"\$\{?[\w@#?*!-]+\}?", "n", NUM,
          "k", W("alias case declare do done echo elif else esac exit export fi for function if in local readonly return select shift source then unset until while")),
        L("PowerShell", "ps1 psm1 psd1", true, "c", @"<#[\s\S]*?(?:#>|\z)|#[^\n]*", "s", DQ + "|" + SQ, "a", @"\$[\w:]+", "t", @"\b[A-Za-z]+-[A-Za-z]+\b", "n", NUM,
          "k", W("begin break catch class continue data do dynamicparam else elseif end enum exit filter finally for foreach from function if in param process return switch throw trap try until using while")),
        L("Batch", "bat cmd", true, "c", @"^[ \t]*@?(?:rem\b|::)[^\n]*", "s", DQ, "a", @"%[\w~:]+%?|![\w]+!", "t", @"^[ \t]*:\w+",
          "k", W("call cd defined do echo else endlocal enabledelayedexpansion equ errorlevel exist exit for geq goto gtr if in leq lss neq not nul pause popd pushd set setlocal shift start")),
        L("SQL", "sql", true, "c", @"--[^\n]*|/\*[\s\S]*?(?:\*/|\z)", "s", SQ + "|" + DQ, "n", NUM,
          "k", W("add all alter and as asc begin between boolean by case char check column commit constraint create cross database date datetime decimal default delete desc distinct drop else end exists float foreign from full group having in index inner insert int integer into is join key left like limit not null offset on or order outer primary references right rollback select set table text then top transaction truncate union unique update values varchar view when where with")),
        L("Lua", "lua", false, "c", @"--\[\[[\s\S]*?(?:\]\]|\z)|--[^\n]*", "s", DQ + "|" + SQ + @"|\[\[[\s\S]*?(?:\]\]|\z)", "n", NUM,
          "k", W("and break do else elseif end false for function goto if in local nil not or repeat return then true until while")),
        L("HTML / XML", "html htm xhtml xml xaml svg csproj vbproj fsproj props targets config xsd xsl xslt resx plist vue", false,
          "c", @"<!--[\s\S]*?(?:-->|\z)", "t", @"</?[\w:.-]+|/?>|<\?[\w-]*|\?>", "a", @"\b[\w:.-]+(?=\s*=\s*[""'])",
          "s", @"(?<==\s*)(?:""[^""]*""?|'[^']*'?)", "k", @"&[#\w]+;"),
        L("CSS", "css scss sass less", false, "c", @"/\*[\s\S]*?(?:\*/|\z)", "s", DQ + "|" + SQ,
          "n", @"#[0-9a-fA-F]{3,8}\b|(?<![\w-])-?\d*\.?\d+(?:[a-z]+|%)?", "a", @"[\w-]+(?=\s*:[^;{}\n]*(?:;|\}|$))",
          "k", @"@[\w-]+|!important", "t", @"[.#][\w-]+"),
        L("JSON", "json jsonc json5 geojson webmanifest", false, "c", CL, "a", @"""(?:\\.|[^""\\\n])*""(?=\s*:)", "s", DQ,
          "n", @"-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b", "k", W("true false null")),
        L("YAML", "yml yaml", false, "c", @"(?:^|(?<=\s))#[^\n]*", "a", @"^[ \t]*(?:-[ \t]+)?[\w.\-/]+(?=[ \t]*:(?:\s|$))",
          "s", DQ + "|" + SQ, "n", NUM, "k", W("true false null yes no on off")),
        L("INI / TOML", "ini toml cfg conf properties env inf reg editorconfig gitconfig", false, "c", @"^[ \t]*[;#][^\n]*",
          "t", @"^[ \t]*\[[^\]\n]*\]", "a", @"^[ \t]*[\w.\-""]+(?=[ \t]*[=:])", "s", DQ + "|" + SQ, "n", NUM, "k", W("true false")),
        L("Markdown", "md markdown", false, "t", @"^#{1,6}[ \t][^\n]*", "s", @"```[\s\S]*?(?:```|\z)|`[^`\n]+`", "c", @"^>[^\n]*",
          "k", @"\*\*[^*\n]+\*\*|__[^_\n]+__", "a", @"!?\[[^\]\n]*\]\([^)\n]*\)"),
        L("reStructuredText", "rst rest", false, "t", @"^([=\-~^""'`#*+])\1{2,}[ \t]*$", "k", @"^\.\.[ \t]+[\w-]+::", "c", @"^\.\.(?:[ \t][^\n]*)?$",
          "s", @"``[^`\n]+``", "a", @":[\w-]+:`[^`\n]+`|`[^`\n]+`_{0,2}"),
        L("Dockerfile", "dockerfile containerfile", true, "c", HASH, "s", DQ + "|" + SQ, "a", @"\$\{?\w+\}?",
          "k", W("from run cmd label expose env add copy entrypoint volume user workdir arg onbuild stopsignal healthcheck shell as")),
        L("Diff", "diff patch", false, "t", @"^@@[^\n]*", "c", @"^\+[^\n]*", "s", @"^-[^\n]*"),
    };

    public static Lang Detect(string path)
    {
        if (path == null) return null;
        string key = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (key.Length == 0) key = Path.GetFileName(path).ToLowerInvariant();
        return All.FirstOrDefault(l => l.Keys.Contains(key));
    }
}

// Highlighting matches of one document, kept between repaints. An edit drops only the matches from the
// start of the changed line on, and scanning never goes further than what has been on screen.
class SyntaxCache
{
    readonly List<int> start = new List<int>(), end = new List<int>();
    readonly List<char> kind = new List<char>();
    string text;
    Lang lang;
    int scanned; // every match starting before this is known

    // Makes sure every match that starts before upTo is known.
    public void Update(string t, Lang l, int upTo)
    {
        if (l != lang || text == null) { Trim(0); lang = l; }
        else if (!ReferenceEquals(t, text))
        {
            int n = Math.Min(t.Length, text.Length), d = 0;
            while (d < n && t[d] == text[d]) d++;
            if (d < n || t.Length != text.Length) Trim(d == 0 ? 0 : t.LastIndexOf('\n', d - 1) + 1);
        }
        text = t;
        if (scanned >= upTo || scanned > t.Length) return;
        for (var m = l.Rx.Match(t, scanned); ; m = m.NextMatch())
        {
            if (!m.Success) { scanned = t.Length + 1; return; }
            if (m.Length > 0) { start.Add(m.Index); end.Add(m.Index + m.Length); kind.Add(l.KindOf(m)); }
            scanned = m.Index + Math.Max(1, m.Length); // where NextMatch goes on from
            if (m.Index >= upTo) return;
        }
    }

    // Lookaheads can peek past a match's end, so a match is kept only if it ends before the changed line.
    void Trim(int line)
    {
        int i = end.Count;
        while (i > 0 && end[i - 1] >= line) i--;
        start.RemoveRange(i, start.Count - i);
        end.RemoveRange(i, end.Count - i);
        kind.RemoveRange(i, kind.Count - i);
        scanned = i > 0 ? end[i - 1] : 0;
    }

    // Calls f(start, end, kind) for each match that overlaps [from, to).
    public void Each(int from, int to, Action<int, int, char> f)
    {
        int i = start.BinarySearch(from);
        if (i < 0) i = ~i;
        while (i > 0 && end[i - 1] > from) i--;
        for (; i < start.Count && start[i] < to; i++) if (end[i] > from) f(start[i], end[i], kind[i]);
    }
}
