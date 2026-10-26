// BlocPlus: a Notepad replacement. Single file, WinForms, builds with the csc.exe
// that ships with Windows (.NET Framework 4.x). Run build.cmd.
//
// Copyright (C) 2026 u0f
// This program is free software: you can redistribute it and/or modify it under the
// terms of the GNU General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version. It is
// distributed WITHOUT ANY WARRANTY. See the LICENSE file for details.
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

[assembly: AssemblyTitle("BlocPlus")]
[assembly: AssemblyProduct("BlocPlus")]
[assembly: AssemblyVersion("1.3.0")]

// UI strings are written in Spanish and translated with the embedded lang\<culture>.txt
// matching the language chosen in settings, or else the Windows display language (English
// when there is none): one "Spanish<TAB>translation" pair per line, \n for line breaks, {0} for arguments.
static class Tr
{
    public static readonly string Lang = Saved(); // "" follows Windows; read before any window exists
    static readonly Dictionary<string, string> map = Load();

    static string Saved()
    {
        try
        {
            foreach (var l in File.ReadAllLines(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Setup.App, "settings.ini")))
                if (l.StartsWith("lang=")) return l.Substring(5);
        }
        catch { }
        return "";
    }

    // Culture names with a translation, Spanish (the source) included.
    public static string[] Available()
    {
        return new[] { "es" }.Concat(typeof(Tr).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("lang.") && !n.StartsWith("lang._")).Select(n => n.Substring(5, n.Length - 9))).ToArray();
    }

    public static string S(string s, params object[] args)
    {
        string t;
        if (!map.TryGetValue(s, out t)) t = s;
        return args.Length == 0 ? t : string.Format(t, args);
    }

    static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>();
        var asm = typeof(Tr).Assembly;
        var names = asm.GetManifestResourceNames();
        var start = CultureInfo.CurrentUICulture;
        try { if (Lang != "") start = new CultureInfo(Lang); } catch { }
        for (var c = start; ; c = c.Parent)
        {
            if (c.TwoLetterISOLanguageName == "es") return map;
            string res = "lang." + (c.Name == "" ? "en" : c.Name) + ".txt";
            if (names.Contains(res))
                using (var r = new StreamReader(asm.GetManifestResourceStream(res), Encoding.UTF8))
                    for (string l; (l = r.ReadLine()) != null; )
                    {
                        int tab = l.IndexOf('\t');
                        if (tab > 0) map[l.Substring(0, tab).Replace("\\n", "\n")] = l.Substring(tab + 1).Replace("\\n", "\n");
                    }
            if (names.Contains(res) || c.Name == "") return map;
        }
    }
}

static class Native
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, out IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, out Point w, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetUpdateRgn(IntPtr h, IntPtr rgn, bool erase);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int GetShortPathName(string path, StringBuilder buf, int size);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, int flags);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr h, string app, string idList);
    [DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, int flags, IntPtr a, IntPtr b);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int GetCurrentPackageFullName(ref int length, StringBuilder name);
    [DllImport("urlmon.dll")] public static extern int CoInternetSetFeatureEnabled(int feature, int flags, bool enable);
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try { Native.SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (!Setup.Packaged)
        {
            if (args.Contains("--uninstall")) { Setup.UninstallInteractive(); return; }
            // The release ships the same exe twice; the copy named BlocPlus-Setup*.exe acts as the installer.
            if (args.Contains("--install") || Path.GetFileName(Application.ExecutablePath).StartsWith("BlocPlus-Setup", StringComparison.OrdinalIgnoreCase))
            {
                if (Setup.InstallInteractive()) Process.Start(Setup.Exe);
                return;
            }
            Setup.Startup();
        }
        Application.Run(new MainForm(args.Where(a => !a.StartsWith("--")).ToArray()));
    }
}

// Per-user install, no admin rights: copies the exe to %LOCALAPPDATA%\Programs\BlocPlus and
// registers a Start menu shortcut, "Open with" / default-app entries and an uninstall entry.
static class Setup
{
    public const string App = "BlocPlus", Repo = "u0f/BlocPlus";
    public static readonly string Version = typeof(Setup).Assembly.GetName().Version.ToString(3);
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BlocPlus", ProgId = "BlocPlus.Document";
    static readonly string[] Exts = ("txt log md markdown json jsonc xml yml yaml ini cfg conf toml csv tsv cs c h cpp hpp java kt js jsx ts tsx " +
        "py rb go rs php ps1 psm1 sh bat cmd sql lua html htm css scss xaml csproj props targets config env gitignore editorconfig diff patch").Split(' ');

    public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", App); } }
    public static string Exe { get { return Path.Combine(Dir, App + ".exe"); } }
    static string Shortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), App + ".lnk"); } }
    // Microsoft Store (MSIX) copy: Windows installs and updates the package, so self-install and the GitHub updater stay off.
    public static readonly bool Packaged = IsPackaged();
    public static bool RunningInstalled { get { return string.Equals(Path.GetFullPath(Application.ExecutablePath), Exe, StringComparison.OrdinalIgnoreCase); } }

    static bool IsPackaged()
    {
        int len = 0;
        try { return Native.GetCurrentPackageFullName(ref len, null) != 15700; } // APPMODEL_ERROR_NO_PACKAGE
        catch { return false; } // Windows 7: no such API, no packages
    }

    // Windows lets a running exe be renamed, so a new version can be swapped in while the old one is open.
    public static void ReplaceExe(string source, string target)
    {
        if (File.Exists(target)) File.Move(target, target + "." + DateTime.Now.Ticks + ".old");
        File.Copy(source, target);
    }

    public static void Startup()
    {
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(Application.ExecutablePath), App + ".exe.*.old"))
            try { File.Delete(f); } catch { }
        if (!RunningInstalled) return;
        using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
            if (k == null || k.GetValue("DisplayVersion") as string != Version) try { Register(); } catch { } // after an update
    }

    public static bool InstallInteractive()
    {
        if (MessageBox.Show(Tr.S("¿Instalar {0} {1} en este equipo?\n\nSe instalará solo para tu usuario en:\n{2}\n\nNo hace falta ser administrador.", App, Version, Dir),
                Tr.S("{0} - Instalación", App), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return false;
        try
        {
            Directory.CreateDirectory(Dir);
            if (!RunningInstalled) ReplaceExe(Application.ExecutablePath, Exe);
            Register();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Tr.S("No se pudo instalar:\n{0}", ex.Message), App, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        if (MessageBox.Show(Tr.S("{0} se ha instalado y ya está en el menú Inicio.\n\n¿Quieres usarlo como editor de texto predeterminado? Se abrirá la configuración de Windows para que lo elijas.", App),
                App, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) OpenDefaultApps();
        return true;
    }

    // Windows does not let apps make themselves the default; this opens the page where the user picks it.
    public static void OpenDefaultApps()
    {
        try { Process.Start("ms-settings:defaultapps?registeredAppUser=" + App); }
        catch { Process.Start("ms-settings:defaultapps"); }
    }

    static void Register()
    {
        var cu = Registry.CurrentUser;
        string exe = Exe, open = "\"" + exe + "\" \"%1\"", icon = exe + ",0";
        using (var k = cu.CreateSubKey(@"Software\Classes\" + ProgId))
        {
            k.SetValue("", Tr.S("Documento de texto ({0})", App));
            using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue("", icon);
            using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", open);
        }
        using (var k = cu.CreateSubKey(@"Software\Classes\Applications\" + App + ".exe"))
        {
            k.SetValue("FriendlyAppName", App);
            using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", open);
            using (var s = k.CreateSubKey("SupportedTypes")) foreach (var e in Exts) s.SetValue("." + e, "");
        }
        using (var k = cu.CreateSubKey(@"Software\Classes\*\shell\" + App))
        {
            k.SetValue("", Tr.S("Editar con {0}", App));
            k.SetValue("Icon", icon);
            using (var c = k.CreateSubKey("command")) c.SetValue("", open);
        }
        using (var k = cu.CreateSubKey(@"Software\" + App + @"\Capabilities"))
        {
            k.SetValue("ApplicationName", App);
            k.SetValue("ApplicationDescription", Tr.S("Editor de texto ligero para Windows."));
            k.SetValue("ApplicationIcon", icon);
            using (var fa = k.CreateSubKey("FileAssociations")) foreach (var e in Exts) fa.SetValue("." + e, ProgId);
        }
        using (var k = cu.CreateSubKey(@"Software\RegisteredApplications")) k.SetValue(App, @"Software\" + App + @"\Capabilities");
        foreach (var e in Exts) using (var k = cu.CreateSubKey(@"Software\Classes\." + e + @"\OpenWithProgids")) k.SetValue(ProgId, "");
        using (var k = cu.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", App);
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "u0f");
            k.SetValue("DisplayIcon", icon);
            k.SetValue("InstallLocation", Dir);
            k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
            k.SetValue("URLInfoAbout", "https://github.com/" + Repo);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
        }
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        var lnk = shell.CreateShortcut(Shortcut);
        lnk.TargetPath = exe;
        lnk.WorkingDirectory = Dir;
        lnk.IconLocation = icon;
        lnk.Description = Tr.S("Editor de texto ligero para Windows");
        lnk.Save();
        Native.SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0, IntPtr.Zero, IntPtr.Zero);
    }

    public static void UninstallInteractive()
    {
        if (MessageBox.Show(Tr.S("¿Desinstalar {0}?", App), App, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        bool wipe = MessageBox.Show(Tr.S("¿Borrar también tus datos de {0}?\n\nSon tus ajustes, los archivos recientes y la sesión guardada, incluido el texto de las pestañas que nunca guardaste.\n\nSi eliges No, se conservan por si vuelves a instalarlo.", App),
            App, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        if (wipe) try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), App), true); } catch { }
        var cu = Registry.CurrentUser;
        foreach (var key in new[] { @"Software\Classes\" + ProgId, @"Software\Classes\Applications\" + App + ".exe", @"Software\Classes\*\shell\" + App, @"Software\" + App, UninstallKey })
            cu.DeleteSubKeyTree(key, false);
        using (var k = cu.OpenSubKey(@"Software\RegisteredApplications", true)) if (k != null) k.DeleteValue(App, false);
        foreach (var e in Exts)
            using (var k = cu.OpenSubKey(@"Software\Classes\." + e + @"\OpenWithProgids", true)) if (k != null) k.DeleteValue(ProgId, false);
        try { File.Delete(Shortcut); } catch { }
        Native.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        MessageBox.Show(Tr.S("{0} se ha desinstalado.", App), App, MessageBoxButtons.OK, MessageBoxIcon.Information);
        // A running exe can't delete its own folder; a hidden cmd does it a moment after we exit.
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + Dir + "\"") { CreateNoWindow = true, UseShellExecute = false });
    }
}

// Checks the latest GitHub release of the repo.
static class Updater
{
    public class Release { public Version Version; public string Notes, Url, Page; }

    // Null when there is no release or GitHub can't be reached.
    public static Release Latest()
    {
        try
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            using (var wc = new WebClient { Encoding = Encoding.UTF8 })
            {
                wc.Headers[HttpRequestHeader.UserAgent] = Setup.App;
                var o = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(
                    wc.DownloadString("https://api.github.com/repos/" + Setup.Repo + "/releases/latest"));
                Version v;
                if (!Version.TryParse(((string)o["tag_name"]).TrimStart('v', 'V'), out v)) return null;
                var r = new Release { Version = v, Notes = o["body"] as string ?? "", Page = (string)o["html_url"] };
                foreach (Dictionary<string, object> a in (object[])o["assets"])
                    if ((string)a["name"] == Setup.App + ".exe") r.Url = (string)a["browser_download_url"];
                return r;
            }
        }
        catch { return null; }
    }

    public static bool IsNewer(Release r) { return r != null && r.Version > Version.Parse(Setup.Version); }

    public static void Download(Release r, string path)
    {
        using (var wc = new WebClient())
        {
            wc.Headers[HttpRequestHeader.UserAgent] = Setup.App;
            wc.DownloadFile(r.Url, path);
        }
        var head = new byte[2];
        using (var f = File.OpenRead(path)) f.Read(head, 0, 2);
        if (head[0] != 'M' || head[1] != 'Z') throw new InvalidDataException(Tr.S("La descarga no es un ejecutable válido."));
    }
}

class Theme
{
    public bool IsDark;
    public Color Back, Fore, Gutter, GutterFore, Chrome, ChromeFore, Hover, Border, Input, Accent;
    public Color Kw, Str, Com, Num, Typ, Attr;

    static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

    public static readonly Theme Light = new Theme
    {
        Back = Color.White, Fore = C(30, 30, 30), Gutter = Color.White, GutterFore = C(160, 160, 160),
        Chrome = C(243, 243, 243), ChromeFore = C(90, 90, 90), Hover = C(228, 228, 228), Border = C(215, 215, 215),
        Input = Color.White, Accent = C(0, 120, 212),
        Kw = C(0, 0, 255), Str = C(163, 21, 21), Com = C(0, 128, 0), Num = C(9, 134, 88), Typ = C(38, 127, 153), Attr = C(0, 16, 128)
    };

    public static readonly Theme Dark = new Theme
    {
        IsDark = true,
        Back = C(30, 30, 30), Fore = C(212, 212, 212), Gutter = C(30, 30, 30), GutterFore = C(110, 118, 129),
        Chrome = C(37, 37, 38), ChromeFore = C(160, 160, 160), Hover = C(50, 50, 52), Border = C(62, 62, 64),
        Input = C(51, 51, 51), Accent = C(0, 122, 204),
        Kw = C(86, 156, 214), Str = C(206, 145, 120), Com = C(106, 153, 85), Num = C(181, 206, 168), Typ = C(78, 201, 176), Attr = C(156, 220, 254)
    };

    public Color For(char kind)
    {
        switch (kind)
        {
            case 'c': return Com;
            case 's': return Str;
            case 'k': return Kw;
            case 'n': return Num;
            case 't': return Typ;
            default: return Attr;
        }
    }
}

class ThemeTable : ProfessionalColorTable
{
    readonly Theme t;
    public ThemeTable(Theme t) { this.t = t; UseSystemColors = false; }
    public override Color MenuStripGradientBegin { get { return t.Chrome; } }
    public override Color MenuStripGradientEnd { get { return t.Chrome; } }
    public override Color ToolStripDropDownBackground { get { return t.Chrome; } }
    public override Color ImageMarginGradientBegin { get { return t.Chrome; } }
    public override Color ImageMarginGradientMiddle { get { return t.Chrome; } }
    public override Color ImageMarginGradientEnd { get { return t.Chrome; } }
    public override Color MenuItemSelected { get { return t.Hover; } }
    public override Color MenuItemSelectedGradientBegin { get { return t.Hover; } }
    public override Color MenuItemSelectedGradientEnd { get { return t.Hover; } }
    public override Color MenuItemPressedGradientBegin { get { return t.Hover; } }
    public override Color MenuItemPressedGradientMiddle { get { return t.Hover; } }
    public override Color MenuItemPressedGradientEnd { get { return t.Hover; } }
    public override Color MenuItemBorder { get { return t.Hover; } }
    public override Color MenuBorder { get { return t.Border; } }
    public override Color SeparatorDark { get { return t.Border; } }
    public override Color SeparatorLight { get { return t.Chrome; } }
    public override Color StatusStripGradientBegin { get { return t.Chrome; } }
    public override Color StatusStripGradientEnd { get { return t.Chrome; } }
    public override Color CheckBackground { get { return t.Accent; } }
    public override Color CheckSelectedBackground { get { return t.Accent; } }
    public override Color CheckPressedBackground { get { return t.Accent; } }
    public override Color ToolStripBorder { get { return t.Chrome; } }
}

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

    public void ClearExtra() { if (Extra.Count > 0) { Extra.Clear(); Invalidate(); } }

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
        if (m.Msg != 0xF /*WM_PAINT*/ || Extra.Count == 0) { base.WndProc(ref m); return; }
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
        int lh = (int)(Font.Height * ZoomFactor), h = ClientSize.Height;
        int top = GetFirstCharIndexFromLine(GetLineFromCharIndex(GetCharIndexFromPosition(Point.Empty)));
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

class Gutter : Control
{
    public Doc D;

    public Gutter()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var b = D.Box;
        e.Graphics.Clear(BackColor);
        using (var f = new Font(b.Font.FontFamily, b.Font.Size * b.ZoomFactor))
        {
            bool wrap = b.WordWrap;
            int len = b.TextLength;
            int total = wrap ? D.Starts.Length : b.GetLineFromCharIndex(len) + 1;
            int w = TextRenderer.MeasureText(new string('9', Math.Max(3, total.ToString().Length)), f).Width + 14;
            if (Width != w) { Width = w; return; } // relayout repaints us

            int lh = f.Height;
            for (int line = b.GetLineFromCharIndex(b.GetCharIndexFromPosition(new Point(1, 1))); ; line++)
            {
                int ci = b.GetFirstCharIndexFromLine(line);
                if (ci < 0) break;
                // Empty last line has no char to ask the position of.
                int y = ci >= len && len > 0 ? b.GetPositionFromCharIndex(len - 1).Y + lh : b.GetPositionFromCharIndex(ci).Y;
                if (y > Height) break;
                string num;
                if (!wrap) num = (line + 1).ToString();
                else { int k = D.LineOf(ci); num = D.Starts[k] == ci ? (k + 1).ToString() : ""; }
                TextRenderer.DrawText(e.Graphics, num, f, new Rectangle(0, y, w - 8, lh), ForeColor, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            }
        }
    }
}

// Owner-drawn tab bar: themable (TabControl is not), with close buttons, "+" and drag to reorder.
class TabStrip : Control
{
    public List<Doc> Docs;
    public int Selected;
    public Theme T = Theme.Light;
    public Action<int> OnSelect, OnClose;
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
            if (rs[i].Contains(p)) { close = CloseRect(rs[i]).Contains(p); return i; }
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
                string glyph = hot || (sel && !d.Dirty) ? "×" : d.Dirty ? "●" : "";
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
        else if (e.Button == MouseButtons.Middle && i >= 0) OnClose(i);
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

class FindForm : Form
{
    public readonly TextBox What = new TextBox(), With = new TextBox();
    public readonly CheckBox MatchCase = Chk(Tr.S("Mayús/minús")), Word = Chk(Tr.S("Palabra completa")), Rx = Chk(Tr.S("Expresión regular"));
    public readonly Label Info = new Label { AutoSize = true, Anchor = AnchorStyles.Left };

    static CheckBox Chk(string t) { return new CheckBox { Text = t, AutoSize = true }; }
    static Label Lab(string t) { return new Label { Text = t, AutoSize = true, Anchor = AnchorStyles.Left }; }
    static Button Btn(string t, Action a)
    {
        var b = new Button { Text = t, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        b.Click += (s, e) => a();
        return b;
    }

    public FindForm(MainForm main)
    {
        Text = Tr.S("Buscar y reemplazar");
        Font = main.Font;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true;
        What.Width = With.Width = Font.Height * 18;
        What.Anchor = With.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        var next = Btn(Tr.S("Siguiente"), () => main.FindNext(false));
        var opts = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        opts.Controls.AddRange(new Control[] { MatchCase, Word, Rx });
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Padding = new Padding(8) };
        t.Controls.Add(Lab(Tr.S("Buscar:")), 0, 0); t.Controls.Add(What, 1, 0); t.Controls.Add(next, 2, 0);
        t.Controls.Add(Lab(Tr.S("Reemplazar:")), 0, 1); t.Controls.Add(With, 1, 1); t.Controls.Add(Btn(Tr.S("Anterior"), () => main.FindNext(true)), 2, 1);
        t.Controls.Add(opts, 1, 2); t.Controls.Add(Btn(Tr.S("Reemplazar"), main.ReplaceOne), 2, 2);
        t.Controls.Add(Info, 1, 3); t.Controls.Add(Btn(Tr.S("Reemplazar todo"), main.ReplaceAll), 2, 3);
        Controls.Add(t);
        AcceptButton = next;

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape) { Hide(); main.Activate(); }
            else if (e.KeyCode == Keys.F3) { main.FindNext(e.Shift); e.Handled = true; }
        };
        FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    public Regex Build()
    {
        string p = What.Text;
        if (p.Length == 0) return null;
        if (!Rx.Checked) p = Regex.Escape(p);
        if (Word.Checked) p = @"\b(?:" + p + @")\b";
        var o = RegexOptions.Multiline;
        if (!MatchCase.Checked) o |= RegexOptions.IgnoreCase;
        try { return new Regex(p, o); }
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

class MainForm : Form
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
    ToolStripMenuItem mWrap, mLines, mRecent, mEnc, mEol, mLang;
    Font font = new Font("Consolas", 11f);
    int sel = -1, untitled, themeMode, tabSize = 4;
    bool wrap, lineNumbers = true, syntax = true, autoIndent = true, spaces, restoreSession = true, checkUpdates = true, sessionDirty, checking;
    string lang = Tr.Lang; // saved to settings.ini; Tr reads it at startup
    string restartExe; // set when an update or install asks to relaunch after closing
    FindForm find;

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
        strip.OnReorder = (from, to) =>
        {
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
        ctx.Items.AddRange(new ToolStripItem[] {
            Item(Tr.S("Deshacer"), Keys.None, () => Cur.Box.Undo()), new ToolStripSeparator(),
            Item(Tr.S("Cortar"), Keys.None, () => CopySel(true)), Item(Tr.S("Copiar"), Keys.None, () => CopySel(false)),
            Item(Tr.S("Pegar"), Keys.None, PastePlain), Item(Tr.S("Eliminar"), Keys.None, () => Cur.Box.SelectedText = ""),
            new ToolStripSeparator(), Item(Tr.S("Seleccionar todo"), Keys.None, () => { Cur.Box.ClearExtra(); Cur.Box.SelectAll(); }) });
        split.Panel1.Controls.Add(host);
        split.Panel2.Controls.Add(web);
        try { Native.CoInternetSetFeatureEnabled(21 /*FEATURE_DISABLE_NAVIGATION_SOUNDS*/, 2 /*SET_FEATURE_ON_PROCESS*/, true); } catch { }
        web.Navigating += OnPreviewNavigating;
        web.DocumentCompleted += (s, e) => SyncPreviewScroll();
        previewTimer.Tick += (s, e) => { previewTimer.Stop(); RenderPreview(); };
        Controls.Add(split); Controls.Add(strip); Controls.Add(menu); Controls.Add(status);
        MainMenuStrip = menu;

        try { LoadSettings(); } catch { }
        mWrap.Checked = wrap;
        mLines.Checked = lineNumbers;
        mPreview.Checked = previewOn;
        if (restoreSession) RestoreSession();
        foreach (var a in args) if (File.Exists(a)) OpenPath(a); else if (IsUrl(a)) OpenUrl(a);
        if (docs.Count == 0) NewDoc();
        SwitchTo(Cur ?? docs[0]);
        ApplyThemeAll();

        // Crash protection: unsaved work also lands in the session every 30 s.
        var timer = new Timer { Interval = 30000 };
        timer.Tick += (s, e) => { if (sessionDirty && restoreSession) try { SaveSession(); } catch { } };
        timer.Start();
        UserPreferenceChangedEventHandler pref = (s, e) => { if (themeMode == 0 && IsHandleCreated) BeginInvoke((Action)ApplyThemeAll); };
        SystemEvents.UserPreferenceChanged += pref;
        FormClosed += (s, e) =>
        {
            SystemEvents.UserPreferenceChanged -= pref;
            if (restartExe != null) Process.Start(restartExe); // after the session is saved
        };
        Shown += (s, e) =>
        {
            Cur.Box.Focus();
            Cur.Box.ScrollToCaret();
            Highlight(Cur);
            previewShell = null; // the page set before the browser had a handle never loaded
            RenderPreview();
            if (checkUpdates && !Setup.Packaged) CheckUpdates(false);
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
        var docMenu = new ToolStripMenuItem(Tr.S("&Documento"));
        docMenu.DropDownItems.AddRange(new ToolStripItem[] { mLang, mEnc, mEol });

        mWrap = Item(Tr.S("A&juste de línea"), Keys.Alt | Keys.Z, () => { wrap = !wrap; ApplyEditorSettings(); });
        mLines = Item(Tr.S("&Números de línea"), Keys.None, () => { lineNumbers = !lineNumbers; ApplyEditorSettings(); });
        mPreview = Item(Tr.S("&Vista previa"), Keys.Control | Keys.Shift | Keys.V, TogglePreview);
        var zin = Item(Tr.S("Acercar"), Keys.Control | Keys.Oemplus, () => Zoom(0.1f));
        var zout = Item(Tr.S("Alejar"), Keys.Control | Keys.OemMinus, () => Zoom(-0.1f));
        zin.ShortcutKeyDisplayString = "Ctrl++";
        zout.ShortcutKeyDisplayString = "Ctrl+-";
        var view = new ToolStripMenuItem(Tr.S("&Ver"));
        view.DropDownItems.AddRange(new ToolStripItem[] {
            mWrap, mLines, mPreview, new ToolStripSeparator(),
            zin, zout, Item(Tr.S("Restablecer zoom"), Keys.Control | Keys.D0, () => Zoom(0)) });

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
        d.Gut = new Gutter { Dock = DockStyle.Left, D = d, Width = 40, Visible = lineNumbers };
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
        d.Box.SelectionChanged += (s, e) => { if (d == Cur && !d.Busy) UpdateStatus(); };
        d.Box.VScroll += (s, e) => { d.Gut.Invalidate(); QueueHighlight(d); if (d == Cur) SyncPreviewScroll(); };
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
        if (sender != d.Box || d.Box.Extra.Count == 0 || e.KeyChar < ' ' || e.KeyChar == '\x7f') return;
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
        if (!b.WordWrap) { line = b.GetLineFromCharIndex(p); col = p - b.GetFirstCharIndexFromLine(line); }
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
        d.Box.Text = s;
        d.Box.ClearUndo();
        d.Box.Modified = false;
        d.Box.Select(0, 0);
        d.Invalidate();
        d.Colored = false;
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
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex) { Err(ex); return; }
        var d = BlankOrNew();
        LoadInto(d, path, bytes);
        AddRecent(path);
        SwitchTo(d);
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

    // Recolors only the visible lines; scanning starts earlier so block comments/strings are right.
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
        int from = vs > 300000 ? vs - 100000 : 0;
        var lang = d.Lang;
        Silently(d, () =>
        {
            Colorize(d, vs, ve, t.Fore);
            for (var m = lang.Rx.Match(text, from); m.Success && m.Index < ve; m = m.NextMatch())
            {
                int e = m.Index + m.Length;
                if (e > vs && m.Length > 0) Colorize(d, Math.Max(vs, m.Index), Math.Min(ve, e), t.For(lang.KindOf(m)));
            }
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
        d.Box.Select(d.Starts[Math.Max(1, Math.Min(n, max)) - 1], 0);
        d.Box.ScrollToCaret();
    }

    // ---------- find / replace ----------

    void ShowFind(bool replace)
    {
        bool first = find == null;
        if (first) find = new FindForm(this);
        var b = Cur.Box;
        if (b.SelectionLength > 0 && b.SelectionLength < 500)
        {
            string s = Cur.Text.Substring(b.SelectionStart, b.SelectionLength);
            if (s.IndexOf('\n') < 0) find.What.Text = s;
        }
        if (first)
        {
            ThemeForm(find, T);
            find.Show(this);
            find.Location = new Point(Right - find.Width - 40, Top + 110);
        }
        else find.Show();
        find.Activate();
        var box = replace ? find.With : find.What;
        box.Focus();
        box.SelectAll();
    }

    public void FindNext(bool back)
    {
        if (find == null || find.What.Text.Length == 0) { ShowFind(false); return; }
        var re = find.Build();
        if (re == null) return;
        var d = Cur; var b = d.Box; string t = d.Text;
        Match m;
        if (!back)
        {
            int from = b.SelectionStart + b.SelectionLength;
            m = re.Match(t, from);
            if (m.Success && m.Length == 0 && m.Index == b.SelectionStart && from < t.Length) m = re.Match(t, from + 1);
            if (!m.Success) m = re.Match(t, 0);
        }
        else
        {
            var rr = new Regex(re.ToString(), re.Options | RegexOptions.RightToLeft);
            m = rr.Match(t, b.SelectionStart);
            if (!m.Success) m = rr.Match(t, t.Length);
        }
        if (!m.Success)
        {
            find.Info.Text = Tr.S("No se encontró.");
            System.Media.SystemSounds.Beep.Play();
            return;
        }
        find.Info.Text = "";
        b.Select(m.Index, m.Length);
        b.ScrollToCaret();
    }

    public void ReplaceOne()
    {
        var re = find.Build();
        if (re == null) return;
        var d = Cur; var b = d.Box;
        var m = re.Match(d.Text, b.SelectionStart);
        if (m.Success && m.Index == b.SelectionStart && m.Length == b.SelectionLength) b.SelectedText = find.Replacement(m);
        FindNext(false);
    }

    public void ReplaceAll()
    {
        var re = find.Build();
        if (re == null) return;
        var d = Cur;
        int n = 0;
        string r = re.Replace(d.Text, m => { n++; return find.Replacement(m); });
        if (n > 0)
        {
            int p = d.Box.SelectionStart;
            Replace(d, 0, d.Text.Length, r); // one undo step
            d.Box.Select(Math.Min(p, r.Length), 0);
        }
        find.Info.Text = n + " reemplazos.";
    }

    // ---------- view & theme ----------

    void Zoom(float delta)
    {
        var b = Cur.Box;
        b.ZoomFactor = delta == 0 ? 1f : Math.Max(0.5f, Math.Min(5f, b.ZoomFactor + delta));
        Cur.Gut.Invalidate();
        UpdateStatus();
        QueueHighlight(Cur);
    }

    void ApplyTabs(Doc d)
    {
        if (d.Tom == null) return;
        using (var g = d.Box.CreateGraphics())
        {
            float px = TextRenderer.MeasureText(g, new string('x', 10), font, Size.Empty, TextFormatFlags.NoPadding).Width / 10f;
            float pt = px * tabSize * 72f / g.DpiX;
            Silently(d, () => d.Tom.DefaultTabStop = pt);
        }
    }

    // Applies wrap / line numbers / font / tabs to every open document.
    void ApplyEditorSettings()
    {
        mWrap.Checked = wrap;
        mLines.Checked = lineNumbers;
        foreach (var d in docs)
        {
            var x = d;
            Silently(x, () => { x.Box.WordWrap = wrap; x.Box.Font = font; });
            x.Gut.Visible = lineNumbers;
            x.Gut.Invalidate();
            x.Colored = true; // font reset may keep old colors; force a full repaint
            ApplyTabs(x);
        }
        TopMost = TopMost;
        UpdateStatus();
        Highlight(Cur);
    }

    void ApplyTheme(Doc d, Theme t)
    {
        Silently(d, () => { d.Box.BackColor = t.Back; d.Box.ForeColor = t.Fore; });
        d.Gut.BackColor = t.Gutter;
        d.Gut.ForeColor = t.GutterFore;
        d.Gut.Invalidate();
        if (d.Box.IsHandleCreated) Native.SetWindowTheme(d.Box.Handle, t.IsDark ? "DarkMode_Explorer" : "Explorer", null);
    }

    void ApplyThemeAll()
    {
        var t = T;
        foreach (var d in docs) { ApplyTheme(d, t); d.Colored = false; }
        menu.Renderer = status.Renderer = ctx.Renderer = new ToolStripProfessionalRenderer(new ThemeTable(t)) { RoundedEdges = false };
        Recolor(menu.Items, t);
        Recolor(ctx.Items, t);
        foreach (ToolStripItem i in status.Items) i.ForeColor = t.ChromeFore;
        lUpdate.ForeColor = t.Accent;
        StylePreviewButton(false);
        strip.T = t;
        strip.Invalidate();
        BackColor = host.BackColor = split.Panel1.BackColor = split.Panel2.BackColor = t.Back;
        split.BackColor = t.Border; // the splitter bar
        previewShell = null;
        RenderPreview();
        TitleBar(this, t.IsDark);
        if (find != null) ThemeForm(find, t);
        if (Cur != null) Highlight(Cur);
    }

    static void Recolor(ToolStripItemCollection items, Theme t)
    {
        foreach (ToolStripItem i in items)
        {
            i.ForeColor = t.Fore;
            var m = i as ToolStripMenuItem;
            if (m != null) Recolor(m.DropDownItems, t);
        }
    }

    static void TitleBar(Form f, bool dark)
    {
        int v = dark ? 1 : 0;
        try
        {
            Native.DwmSetWindowAttribute(f.Handle, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref v, 4);
            Native.SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x37 /*NOSIZE|NOMOVE|NOZORDER|NOACTIVATE|FRAMECHANGED*/);
        }
        catch { }
    }

    static void ThemeForm(Form f, Theme t)
    {
        f.BackColor = t.Chrome;
        f.ForeColor = t.Fore;
        ThemeControls(f.Controls, t);
        TitleBar(f, t.IsDark);
    }

    static void ThemeControls(Control.ControlCollection cs, Theme t)
    {
        foreach (Control c in cs)
        {
            if (c is TextBoxBase || c is ComboBox)
            {
                c.BackColor = t.Input;
                c.ForeColor = t.Fore;
                var cb = c as ComboBox;
                if (cb != null) cb.FlatStyle = t.IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                Native.SetWindowTheme(c.Handle, t.IsDark ? "DarkMode_Explorer" : "Explorer", null);
            }
            else if (c is Button)
            {
                var b = (Button)c;
                b.FlatStyle = t.IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                b.BackColor = t.IsDark ? t.Hover : SystemColors.Control;
                b.ForeColor = t.Fore;
                b.FlatAppearance.BorderColor = t.Border;
                b.UseVisualStyleBackColor = !t.IsDark;
            }
            ThemeControls(c.Controls, t);
        }
    }

    string Prompt(string title, string label, string value)
    {
        using (var f = new Form { Text = title, Font = Font, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ShowIcon = false, StartPosition = FormStartPosition.CenterParent, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink })
        {
            var tb = new TextBox { Text = value, Width = Font.Height * 16 };
            var ok = new Button { Text = Tr.S("Aceptar"), DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = Tr.S("Cancelar"), DialogResult = DialogResult.Cancel, AutoSize = true };
            var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right };
            buttons.Controls.AddRange(new Control[] { ok, cancel });
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(10) };
            p.Controls.AddRange(new Control[] { new Label { Text = label, AutoSize = true }, tb, buttons });
            f.Controls.Add(p);
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            ThemeForm(f, T);
            return f.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
        }
    }

    // ---------- live preview ----------

    // Pages and SVGs can run scripts, so ones that came from the web are never rendered.
    static string PreviewKind(Doc d)
    {
        string k = AnyPreviewKind(d);
        return d.Url != null && (k == "html" || k == "svg") ? null : k;
    }

    static string AnyPreviewKind(Doc d)
    {
        string file = d.FilePath ?? (d.Url != null ? new Uri(d.Url).LocalPath : null);
        string ext = file == null ? "" : Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
        switch (ext)
        {
            case "html": case "htm": case "xhtml": return "html";
            case "md": case "markdown": case "mdown": case "mkd": return "md";
            case "rst": case "rest": return "rst";
            case "svg": return "svg";
            case "csv": return "csv";
            case "tsv": return "tsv";
            case "lrc": return "lrc";
            case "diff": case "patch": return "diff";
        }
        if (d.Lang == null) return null;
        if (d.Lang.Name == "Markdown") return "md";
        if (d.Lang.Name == "reStructuredText") return "rst";
        if (d.Lang.Name == "Diff") return "diff";
        if (d.Lang.Name == "HTML / XML" && Regex.IsMatch(d.Text.Substring(0, Math.Min(1000, d.Text.Length)), "<html|<!doctype html", RegexOptions.IgnoreCase)) return "html";
        return null;
    }

    void TogglePreview()
    {
        if (!previewOn && PreviewKind(Cur) == null)
        {
            MessageBox.Show(this, Tr.S("La vista previa está disponible para HTML, Markdown, reStructuredText, SVG, CSV/TSV y letras LRC."), App, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        previewOn = mPreview.Checked = !previewOn;
        RenderPreview();
        Cur.Box.Focus();
    }

    // A filled accent pill while the preview is available but hidden, so it stands out; accent text once open.
    void StylePreviewButton(bool hover)
    {
        var t = T;
        if (previewOn) { lPreview.BackColor = hover ? t.Hover : t.Chrome; lPreview.ForeColor = t.Accent; }
        else { lPreview.BackColor = hover ? ControlPaint.Light(t.Accent) : t.Accent; lPreview.ForeColor = Color.White; }
    }

    static string Hex(Color c) { return "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2"); }

    string BaseTag(Doc d)
    {
        return d.FilePath == null ? "" : "<base href=\"file:///" + WebUtility.HtmlEncode(Path.GetDirectoryName(d.FilePath).Replace('\\', '/')) + "/\">";
    }

    string Shell(Doc d)
    {
        var t = T;
        string code = Hex(t.IsDark ? Color.FromArgb(45, 45, 48) : Color.FromArgb(246, 248, 250)), muted = Hex(t.IsDark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(100, 100, 100));
        string css =
            "body{margin:0;padding:20px 32px;font:15px/1.6 'Segoe UI',sans-serif;color:" + Hex(t.Fore) + ";background:" + Hex(t.Back) + ";word-wrap:break-word}" +
            "h1,h2,h3,h4,h5,h6{margin:1.2em 0 .6em;font-weight:600;line-height:1.25}h1,h2{padding-bottom:.3em;border-bottom:1px solid " + Hex(t.Border) + "}" +
            "h1{font-size:2em}h2{font-size:1.5em}h3{font-size:1.25em}a{color:" + Hex(t.Accent) + "}img{max-width:100%}" +
            "code{font-family:Consolas,monospace;font-size:90%;background:" + code + ";padding:.15em .35em;border-radius:4px}" +
            "pre{background:" + code + ";padding:12px 16px;border-radius:6px;overflow:auto;line-height:1.45}pre code{padding:0;background:none}" +
            "blockquote{margin:0 0 1em;padding:0 1em;color:" + muted + ";border-left:4px solid " + Hex(t.Border) + "}" +
            "table{border-collapse:collapse;margin:1em 0}th,td{border:1px solid " + Hex(t.Border) + ";padding:6px 13px}th{background:" + code + ";font-weight:600}" +
            "hr{border:0;border-top:1px solid " + Hex(t.Border) + ";margin:1.5em 0}ul,ol{padding-left:2em}" +
            ".admonition{border-left:4px solid " + Hex(t.Accent) + ";background:" + code + ";padding:.4em 1em;margin:1em 0;border-radius:4px}.admonition-title{font-weight:600;margin:.3em 0}" +
            ".df{border:1px solid " + Hex(t.Border) + ";border-radius:6px;margin:0 0 16px;overflow:hidden}.dh{padding:6px 12px;background:" + code + ";font-weight:600;border-bottom:1px solid " + Hex(t.Border) + "}" +
            ".dh span{display:block;font-weight:normal;font-size:90%;color:" + muted + "}.dt{width:100%;margin:0;font:13px/1.45 Consolas,monospace}" +
            ".dt td{border:0;padding:0 8px;white-space:pre-wrap;word-break:break-all}.dt td.ln{width:1%;text-align:right;color:" + muted + ";white-space:nowrap}" +
            ".da td{background:rgba(46,160,67,.2)}.dd td{background:rgba(248,81,73,.2)}.dk td{background:rgba(56,139,253,.12);color:" + muted + "}.dn td{color:" + muted + "}";
        return "<!DOCTYPE html><html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><meta charset=\"utf-8\">" + BaseTag(d) +
            "<style>" + css + "</style></head><body></body></html>";
    }

    // Re-renders the preview of the current tab; shows or hides the pane as needed.
    void RenderPreview()
    {
        var d = Cur;
        if (d == null) return;
        string kind = PreviewKind(d);
        lPreview.Visible = kind != null;
        lPreview.Text = previewOn ? Tr.S("◧ Ocultar vista previa") : Tr.S("◧ Vista previa disponible");
        StylePreviewButton(false);
        bool show = previewOn && kind != null;
        if (split.Panel2Collapsed == show)
        {
            split.Panel2Collapsed = !show;
            if (show && !previewSized) { split.SplitterDistance = split.Width / 2; previewSized = true; }
            if (!show) { previewShell = null; web.DocumentText = ""; } // unload it, so an LRC song stops playing
        }
        if (!show) return;
        if (kind == "lrc") { RenderLrc(d); return; }

        if (kind == "html")
        {
            // A full page: give it our base folder (for relative links/images) and the modern IE mode.
            string head = "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">" + BaseTag(d), page = d.Text;
            var m = Regex.Match(page, @"<head[^>]*>|<!doctype[^>]*>", RegexOptions.IgnoreCase);
            previewShell = null;
            web.DocumentText = m.Success ? page.Insert(m.Index + m.Length, head) : head + page;
            return;
        }
        Markup.Strict = d.Url != null;
        string body = kind == "md" ? Markup.Markdown(d.Text) : kind == "rst" ? Markup.Rst(d.Text)
            : kind == "diff" ? Markup.Diff(d.Text) : kind == "csv" ? Markup.Csv(d.Text, ',') : kind == "tsv" ? Markup.Csv(d.Text, '\t') : d.Text;
        string shell = Shell(d);
        if (previewShell == shell && web.ReadyState == WebBrowserReadyState.Complete && web.Document != null && web.Document.Body != null)
        {
            web.Document.Body.InnerHtml = body; // no reload: no flicker, scroll stays put
            SyncPreviewScroll();
        }
        else
        {
            previewShell = shell;
            web.DocumentText = shell.Replace("<body></body>", "<body>" + body + "</body>");
        }
    }

    static readonly string[] MediaExts = { ".mp3", ".m4a", ".aac", ".mp4", ".m4v" };

    // Lyrics under an audio/video player: the current line follows playback, clicking a line seeks to it.
    // Edits only swap the lyrics, so playback keeps going.
    void RenderLrc(Doc d)
    {
        if (d.Media == null && d.FilePath != null) // song.lrc next to song.mp3
            d.Media = MediaExts.Select(x => Path.ChangeExtension(d.FilePath, x)).FirstOrDefault(File.Exists);
        string player;
        if (d.Media == null) player = "<p>" + WebUtility.HtmlEncode(Tr.S("Elige el audio o vídeo de esta letra para seguirla mientras suena.")) + "</p><a href='blocplus:media'>" + WebUtility.HtmlEncode(Tr.S("Elegir archivo...")) + "</a>";
        else
        {
            string ext = Path.GetExtension(d.Media).ToLowerInvariant(), tag = ext == ".mp4" || ext == ".m4v" || ext == ".mov" || ext == ".wmv" || ext == ".avi" ? "video" : "audio";
            player = "<" + tag + " id='m' src='" + WebUtility.HtmlEncode(MediaUrl(d.Media)) + "'></" + tag + ">";
            if (tag == "video") player = "<div id='v'>" + player + "<div id='sub'></div></div>"; // lyrics as subtitles
            player +=
                "<div id='c'><button id='pp' class='p' title='" + WebUtility.HtmlEncode(Tr.S("Reproducir / pausa")) + "'>&#9654;</button><span id='tc'>0:00</span>" + Slider("bar") +
                "<span id='td'>0:00</span><button id='mu' title='" + WebUtility.HtmlEncode(Tr.S("Silenciar")) + "'>&#128266;</button>" + Slider("vol") + "</div>" +
                "<div>" + WebUtility.HtmlEncode(Path.GetFileName(d.Media)) + " · <a href='blocplus:media'>" + WebUtility.HtmlEncode(Tr.S("Cambiar...")) + "</a> <span id='e'></span></div>";
        }
        string acc = Hex(T.Accent), track = Hex(T.Border);
        string css = "html,body{height:100%;overflow:hidden}body{padding:0}" +
            "#h{padding:12px 20px;border-bottom:1px solid " + track + ";font-size:13px}#h video{display:block;width:100%;max-height:55vh;background:#000;cursor:pointer}" +
            "#v{position:relative;background:#000}#sub{position:absolute;left:4%;right:4%;bottom:7%;text-align:center;line-height:1.45;color:#fff;pointer-events:none}" +
            "#sub span{background:rgba(8,8,8,.75);padding:.1em .45em}" +
            "#c{display:flex;align-items:center;margin:8px 0}" +
            "#c button{flex:none;width:38px;height:38px;padding:0;border:0;border-radius:50%;cursor:pointer;font:15px/38px 'Segoe UI Symbol';text-align:center}" +
            "#pp{background:" + acc + ";color:#fff}#pp.p{padding-left:3px}#mu{background:none;color:" + Hex(T.Fore) + ";margin-left:4px}" +
            "#tc,#td{flex:none;width:44px;text-align:center;font:12px Consolas,monospace}" +
            ".sl{flex:1;position:relative;height:18px;cursor:pointer}#vol{flex:none;width:80px;margin-right:4px}" +
            ".tr,.fi{position:absolute;left:0;top:7px;height:4px;border-radius:2px}.tr{right:0;background:" + track + "}.fi{width:0;background:" + acc + "}" +
            ".kn{position:absolute;top:3px;left:0;width:12px;height:12px;margin-left:-6px;border-radius:50%;background:" + acc + ";display:none}" +
            ".hit{position:absolute;left:0;right:0;top:0;bottom:0;background:#000;opacity:0}.sl:hover .kn{display:block}.sl:hover .tr,.sl:hover .fi{top:6px;height:6px}" +
            "#l{position:absolute;left:0;right:0;bottom:0;overflow:auto;padding:0 24px;text-align:center}#l:before{content:'';display:block;height:12px}#l:after{content:'';display:block;height:35vh}#l p{margin:.45em 0;opacity:.5;cursor:pointer;font-size:1.15em}" +
            "#l p.on{opacity:1;font-weight:600;color:" + Hex(T.Accent) + "}#e{color:#d33}";
        string script = "<script>var m=document.getElementById('m'),l=document.getElementById('l'),sub=document.getElementById('sub'),cur=null;" +
            "function fit(){l.style.top=document.getElementById('h').offsetHeight+'px';if(sub)sub.style.fontSize=Math.max(14,m.offsetWidth/28)+'px';}" +
            "fit();setInterval(fit,250);if(m){" +
            "m.onerror=function(){document.getElementById('e').innerText='" + Tr.S("No se puede reproducir: el MP4 debe ser H.264 con audio AAC; el audio, MP3 o M4A/AAC.").Replace("\\", "\\\\").Replace("'", "\\'") + "';};" +
            "setInterval(function(){var ps=l.getElementsByTagName('p'),a=null,t=m.currentTime;" +
            "for(var i=0;i<ps.length;i++){if(parseFloat(ps[i].getAttribute('data-t'))<=t)a=ps[i];else break;}" +
            "if(a!==cur){if(cur)cur.className='';cur=a;if(sub)sub.innerHTML=a?'<span>'+a.innerHTML+'</span>':'';if(a){a.className='on';l.scrollTop+=a.getBoundingClientRect().top-l.getBoundingClientRect().top-(l.clientHeight-a.offsetHeight)/2;}}},100);" +
            "l.onclick=function(e){e=e||window.event;var p=e.target||e.srcElement;while(p&&p.tagName!='P')p=p.parentNode;" +
            "if(p){m.currentTime=parseFloat(p.getAttribute('data-t'));m.play();}};" +
            // Our own controls: play/pause, times, a seek bar and a volume bar, both clickable and draggable.
            "var $=function(i){return document.getElementById(i);};" +
            "function fmt(s){s=Math.floor(s||0);var r=s%60;return Math.floor(s/60)+':'+(r<10?'0':'')+r;}" +
            "function show(id,f){var c=$(id).childNodes;c[1].style.width=c[2].style.left=f*100+'%';}" +
            "function slider(id,set){var s=$(id),h=s,drag=false;" +
            "function at(e){e=e||window.event;var r=s.getBoundingClientRect();set(Math.max(0,Math.min(1,(e.clientX-r.left)/(r.right-r.left))));}" +
            "h.onmousedown=function(e){drag=true;h.setCapture();at(e);return false;};h.onmousemove=function(e){if(drag)at(e);};" +
            "h.onmouseup=function(){drag=false;h.releaseCapture();};}" +
            "function upd(){show('bar',m.duration?m.currentTime/m.duration:0);$('tc').innerText=fmt(m.currentTime);$('td').innerText=fmt(m.duration);}" +
            "slider('bar',function(f){if(m.duration)m.currentTime=f*m.duration;upd();});" +
            "slider('vol',function(f){m.volume=f;m.muted=false;});" +
            "$('pp').onclick=m.onclick=function(){if(m.paused)m.play();else m.pause();};$('mu').onclick=function(){m.muted=!m.muted;};" +
            "m.onplay=function(){$('pp').className='';$('pp').innerHTML='&#10074;&#10074;';};" +
            "m.onpause=m.onended=function(){$('pp').className='p';$('pp').innerHTML='&#9654;';};" +
            "m.onvolumechange=function(){show('vol',m.muted?0:m.volume);$('mu').innerHTML=m.muted||!m.volume?'&#128263;':'&#128266;';};" +
            "m.ontimeupdate=m.ondurationchange=upd;m.onvolumechange();}</script>";
        string shell = Shell(d).Replace("</style>", css + "</style>").Replace("<body></body>", "<body><div id='h'>" + player + "</div><div id='l'></div>" + script + "</body>");
        string lyrics = Markup.Lrc(d.Text);
        var el = previewShell == shell && web.ReadyState == WebBrowserReadyState.Complete && web.Document != null ? web.Document.GetElementById("l") : null;
        if (el != null) { el.InnerHtml = lyrics; return; }
        previewShell = shell;
        web.DocumentText = shell.Replace("<div id='l'></div>", "<div id='l'>" + lyrics + "</div>");
    }

    // IE can't open file URLs with non-ASCII characters, so such files go through their 8.3 short name.
    static string MediaUrl(string path)
    {
        if (path.Any(c => c > 127))
        {
            var sb = new StringBuilder(1024);
            if (Native.GetShortPathName(path, sb, sb.Capacity) > 0) path = sb.ToString();
        }
        return new Uri(path).AbsoluteUri;
    }

    // Track, fill, knob, and an invisible layer on top so IE never lets a click fall through; the LRC script relies on this order.
    static string Slider(string id) { return "<div class='sl' id='" + id + "'><div class='tr'></div><div class='fi'></div><div class='kn'></div><div class='hit'></div></div>"; }

    void PickMedia()
    {
        var d = Cur;
        using (var dlg = new OpenFileDialog
        {
            Title = Tr.S("Audio o vídeo de la letra"),
            Filter = Tr.S("Audio y vídeo (MP3, M4A, AAC, MP4)|*.mp3;*.m4a;*.aac;*.mp4;*.m4v|Todos los archivos|*.*")
        })
        {
            if (d.FilePath != null) dlg.InitialDirectory = Path.GetDirectoryName(d.FilePath);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            d.Media = dlg.FileName;
        }
        RenderPreview();
    }

    // Scrolls the preview to the same relative position as the editor.
    void SyncPreviewScroll()
    {
        var d = Cur;
        if (d == null || split.Panel2Collapsed || web.Document == null || web.Document.Body == null || PreviewKind(d) == "lrc") return;
        var b = d.Box;
        int first = b.GetLineFromCharIndex(b.GetCharIndexFromPosition(new Point(1, 1)));
        int last = b.GetLineFromCharIndex(b.GetCharIndexFromPosition(new Point(1, b.ClientSize.Height - 1)));
        int total = b.GetLineFromCharIndex(b.TextLength) + 1;
        double ratio = Math.Max(0, Math.Min(1, (double)first / Math.Max(1, total - (last - first + 1))));
        var root = web.Document.Body.Parent ?? web.Document.Body;
        root.ScrollTop = (int)(ratio * Math.Max(0, root.ScrollRectangle.Height - root.ClientRectangle.Height));
    }

    // Links clicked in the preview open outside it: web pages in the browser, local text files here.
    void OnPreviewNavigating(object s, WebBrowserNavigatingEventArgs e)
    {
        var u = e.Url;
        if (u.Scheme == "about") return;
        e.Cancel = true;
        if (u.Scheme == "blocplus") BeginInvoke((Action)PickMedia); // the "choose audio/video" link of the LRC preview
        else if (u.Scheme == "http" || u.Scheme == "https" || u.Scheme == "mailto") Process.Start(u.AbsoluteUri);
        else if (u.IsFile && File.Exists(u.LocalPath)) OpenPath(u.LocalPath);
    }

    // ---------- updates ----------

    // Asks GitHub in the background; a manual check also reports "up to date" and errors.
    void CheckUpdates(bool manual)
    {
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            var r = Updater.Latest();
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (Updater.IsNewer(r))
                    {
                        lUpdate.Tag = r;
                        lUpdate.Text = Tr.S("⬆ Actualización disponible: {0}", r.Version);
                        lUpdate.Visible = true;
                        if (manual) PromptUpdate(r);
                    }
                    else if (manual)
                        MessageBox.Show(this, r == null ? Tr.S("No se pudo comprobar si hay actualizaciones. Revisa tu conexión a internet.") : Tr.S("Tienes la última versión ({0}).", Version),
                            App, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            }
            catch (InvalidOperationException) { } // window already closed
        });
    }

    void PromptUpdate(Updater.Release r)
    {
        string notes = r.Notes.Length > 1200 ? r.Notes.Substring(0, 1200) + "..." : r.Notes;
        if (MessageBox.Show(this, Tr.S("Hay una nueva versión de {0}: {1} (tienes la {2}).\n\n{3}\n\n¿Descargarla e instalarla ahora? {0} se reiniciará y conservará tus pestañas.", App, r.Version, Version, notes),
                App, MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
        if (r.Url == null) { Process.Start(r.Page); return; }
        lUpdate.Text = Tr.S("Descargando actualización...");
        string exe = Application.ExecutablePath, tmp = Path.Combine(Path.GetTempPath(), App + "-" + r.Version + ".exe");
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            Exception error = null;
            try
            {
                Updater.Download(r, tmp);
                Setup.ReplaceExe(tmp, exe);
            }
            catch (Exception ex) { error = ex; }
            BeginInvoke((Action)(() =>
            {
                if (error != null) { lUpdate.Text = Tr.S("⬆ Actualización disponible: {0}", r.Version); Err(error); return; }
                restartExe = exe;
                Close();
            }));
        });
    }

    // ---------- settings window ----------

    static string Changelog()
    {
        using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("CHANGELOG.md"))
        {
            if (s == null) return "";
            using (var r = new StreamReader(s))
            {
                // Strip the bits of Markdown that look noisy in a plain TextBox.
                string t = Regex.Replace(Normalize(r.ReadToEnd()), @"^# [^\n]*\n+", "");
                t = Regex.Replace(t, @"^#+ ", "", RegexOptions.Multiline);
                t = Regex.Replace(t, @"^- ", "• ", RegexOptions.Multiline).Replace("**", "");
                return t.Trim().Replace("\n", "\r\n");
            }
        }
    }

    void ShowSettings()
    {
        using (var f = new Form { Text = Tr.S("Ajustes - {0} {1}", App, Version), Font = Font, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ShowIcon = false, StartPosition = FormStartPosition.CenterParent, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink })
        {
            Func<string, bool, CheckBox> chk = (text, on) => new CheckBox { Text = text, Checked = on, AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
            Func<string, Label> head = text => new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 12, 3, 4) };
            var cTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Font.Height * 10 };
            cTheme.Items.AddRange(ThemeNames);
            cTheme.SelectedIndex = themeMode;
            var cTab = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Font.Height * 10 };
            cTab.Items.AddRange(new object[] { "2", "4", "8" });
            cTab.SelectedItem = tabSize.ToString();
            var langs = Tr.Available().Select(c => new CultureInfo(c))
                .Select(c => new { c.Name, Text = char.ToUpper(c.NativeName[0], c) + c.NativeName.Substring(1) }).OrderBy(x => x.Text).ToList();
            var cLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Font.Height * 10, DropDownWidth = Font.Height * 16 };
            cLang.Items.Add(Tr.S("Idioma de Windows"));
            foreach (var x in langs) cLang.Items.Add(x.Text);
            cLang.SelectedIndex = langs.FindIndex(x => x.Name == lang) + 1;
            var chosen = font;
            var bFont = new Button { Text = Tr.S("Fuente: {0} {1} pt", font.Name, font.SizeInPoints), AutoSize = true };
            bFont.Click += (s, e) =>
            {
                using (var fd = new FontDialog { Font = chosen, ShowEffects = false })
                    if (fd.ShowDialog(f) == DialogResult.OK) { chosen = fd.Font; bFont.Text = Tr.S("Fuente: {0} {1} pt", chosen.Name, chosen.SizeInPoints); }
            };
            var xWrap = chk(Tr.S("Ajuste de línea"), wrap);
            var xLines = chk(Tr.S("Mostrar números de línea"), lineNumbers);
            var xSyntax = chk(Tr.S("Resaltado de sintaxis"), syntax);
            var xIndent = chk(Tr.S("Sangría automática al pulsar Intro"), autoIndent);
            var xSpaces = chk(Tr.S("El tabulador inserta espacios"), spaces);
            var xSession = chk(Tr.S("Restaurar pestañas y texto sin guardar al abrir"), restoreSession);
            var xTop = chk(Tr.S("Ventana siempre visible"), TopMost);
            var xUpdates = chk(Tr.S("Buscar actualizaciones al iniciar"), checkUpdates);
            xUpdates.Visible = !Setup.Packaged; // the Store updates it

            var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 24, 0) };
            left.Controls.AddRange(new Control[] {
                head(Tr.S("Apariencia")), new Label { Text = Tr.S("Tema"), AutoSize = true }, cTheme,
                new Label { Text = Tr.S("Idioma"), AutoSize = true }, cLang, bFont, xLines, xSyntax,
                head(Tr.S("Edición")), xWrap, xIndent, xSpaces, new Label { Text = Tr.S("Ancho del tabulador"), AutoSize = true }, cTab,
                head(Tr.S("Ventana")), xSession, xTop, xUpdates });

            var log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = Changelog(), Width = Font.Height * 30, Height = Font.Height * 22, WordWrap = true };
            var github = new Button { Text = "GitHub", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 3, 2) };
            github.Click += (s, e) => Process.Start("https://github.com/" + Setup.Repo);
            var title = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            title.Controls.AddRange(new Control[] {
                new Label { Text = App + " " + Version, AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold), Margin = new Padding(3, 8, 3, 2) },
                github });
            var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            right.Controls.AddRange(new Control[] {
                title,
                new Label { Text = Tr.S("Un editor de texto ligero para Windows."), AutoSize = true },
                head(Tr.S("Novedades")), log });

            var ok = new Button { Text = Tr.S("Aceptar"), DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = Tr.S("Cancelar"), DialogResult = DialogResult.Cancel, AutoSize = true };
            var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0) };
            buttons.Controls.AddRange(new Control[] { ok, cancel });
            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(14) };
            grid.Controls.Add(left, 0, 0);
            grid.Controls.Add(right, 1, 0);
            grid.Controls.Add(buttons, 1, 1);
            f.Controls.Add(grid);
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            ThemeForm(f, T);
            log.Select(0, 0);
            if (f.ShowDialog(this) != DialogResult.OK) return;

            themeMode = cTheme.SelectedIndex;
            tabSize = int.Parse((string)cTab.SelectedItem);
            font = chosen;
            wrap = xWrap.Checked;
            lineNumbers = xLines.Checked;
            syntax = xSyntax.Checked;
            autoIndent = xIndent.Checked;
            spaces = xSpaces.Checked;
            restoreSession = xSession.Checked;
            TopMost = xTop.Checked;
            checkUpdates = xUpdates.Checked;
            string newLang = cLang.SelectedIndex == 0 ? "" : langs[cLang.SelectedIndex - 1].Name;
            bool langChanged = newLang != lang;
            lang = newLang;
            ApplyEditorSettings();
            ApplyThemeAll();
            try { SaveSettings(); } catch { }
            if (langChanged && MessageBox.Show(this, Tr.S("El nuevo idioma se verá al reiniciar {0}.", App) + "\n\n" + Tr.S("¿Reiniciar ahora?"),
                    App, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                restartExe = Application.ExecutablePath;
                Close();
            }
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys k)
    {
        if (k == (Keys.Control | Keys.Tab) || k == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            int n = docs.Count;
            if (n > 1) SwitchTo(docs[(sel + ((k & Keys.Shift) != 0 ? n - 1 : 1)) % n]);
            return true;
        }
        if (k == (Keys.Control | Keys.N)) { SwitchTo(NewDoc()); return true; } // Notepad habit, same as Ctrl+T
        return base.ProcessCmdKey(ref msg, k);
    }

    // ---------- session & settings persistence ----------

    // Like modern Notepad: closing never nags; open tabs and unsaved text come back next time.
    void SaveSession()
    {
        Directory.CreateDirectory(SessDir);
        var lines = new List<string> { sel.ToString() };
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int n = 0;
        foreach (var d in docs)
        {
            string tmp = "";
            if ((d.Dirty || d.FilePath == null) && d.Box.TextLength > 0)
            {
                tmp = "tab" + (n++) + ".txt";
                File.WriteAllText(Path.Combine(SessDir, tmp), d.Text, Encoding.UTF8);
                keep.Add(tmp);
            }
            else if (d.FilePath == null) continue;
            lines.Add(string.Join("\t", d.FilePath ?? "", tmp, d.Enc, d.Eol, d.Dirty ? 1 : 0, d.Box.SelectionStart, d.Lang == null ? "" : d.Lang.Name, d.Url ?? ""));
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
                }
                else if (path != null && File.Exists(path))
                {
                    d = NewDoc();
                    LoadInto(d, path, File.ReadAllBytes(path));
                }
                else continue;
                if (c.Length > 6) d.Lang = Lang.All.FirstOrDefault(l => l.Name == c[6]);
                if (c.Length > 7 && c[7].Length > 0) d.Url = c[7];
                int s;
                if (int.TryParse(c[5], out s)) d.Box.Select(Math.Min(s, d.Box.TextLength), 0);
                UpdateTitle(d);
            }
            catch { }
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
