// BlocPlus: a Notepad replacement. WinForms, no dependencies, builds with the csc.exe
// that ships with Windows (.NET Framework 4.x). Run build.cmd; the sources are src\*.cs.
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
[assembly: AssemblyVersion("1.4.0")]

// UI strings are written in Spanish and translated with the embedded lang\<culture>.txt
// matching the language chosen in settings, or else the Windows display language (English
// when there is none): one "Spanish<TAB>translation" pair per line, \n for line breaks, {0} for arguments.
static class Tr
{
    public static readonly string Lang = Setup.Ini("lang") ?? ""; // "" follows Windows; read before any window exists
    static readonly Dictionary<string, string> map = Load();

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
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
}

// Errors the app recovers from on its own, kept in %APPDATA%\BlocPlus\log.txt for bug reports.
static class Log
{
    public static void Write(string what, Exception ex)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Setup.App), f = Path.Combine(dir, "log.txt");
            Directory.CreateDirectory(dir);
            if (File.Exists(f) && new FileInfo(f).Length > 256 * 1024) File.Delete(f);
            File.AppendAllText(f, DateTime.Now.ToString("s") + " " + what + ": " + ex + Environment.NewLine);
        }
        catch { }
    }
}

// One window: a second BlocPlus hands its files to the running one through a named pipe and exits.
static class Instance
{
    static readonly string Pipe = Setup.App + "-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value + "-" + Process.GetCurrentProcess().SessionId;

    // True when a running instance took the arguments (paths made absolute, since its folder differs).
    public static bool Send(string[] args)
    {
        try
        {
            using (var c = new System.IO.Pipes.NamedPipeClientStream(".", Pipe, System.IO.Pipes.PipeDirection.Out))
            {
                c.Connect(300);
                Native.AllowSetForegroundWindow(-1 /*ASFW_ANY*/);
                var w = new StreamWriter(c, new UTF8Encoding(false));
                foreach (var a in args) w.WriteLine(Regex.IsMatch(a, "^https?://", RegexOptions.IgnoreCase) ? a : Path.Combine(Environment.CurrentDirectory, a));
                w.Flush();
                return true;
            }
        }
        catch (Exception) { return false; }
    }

    // Calls open with each batch of arguments, on a background thread. While another instance still owns
    // the pipe (two windows on purpose, or the old one closing after an update) it keeps retrying.
    public static void Listen(Action<string[]> open)
    {
        new System.Threading.Thread(() =>
        {
            while (true)
                try
                {
                    using (var s = new System.IO.Pipes.NamedPipeServerStream(Pipe, System.IO.Pipes.PipeDirection.In, 1))
                    {
                        s.WaitForConnection();
                        var lines = new List<string>();
                        var r = new StreamReader(s, Encoding.UTF8);
                        for (string l; (l = r.ReadLine()) != null; ) lines.Add(l);
                        open(lines.ToArray());
                    }
                }
                catch (IOException) { System.Threading.Thread.Sleep(2000); }
                catch (Exception ex) { Log.Write("instance pipe", ex); return; }
        }) { IsBackground = true }.Start();
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var files = args.Where(a => !a.StartsWith("--")).ToArray();
        if (!args.Contains("--new-instance") && !args.Contains("--install") && !args.Contains("--uninstall") && Setup.Ini("single") != "0" &&
            !Path.GetFileName(Application.ExecutablePath).StartsWith("BlocPlus-Setup", StringComparison.OrdinalIgnoreCase) && Instance.Send(files)) return;
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
        Application.Run(new MainForm(files));
    }
}

