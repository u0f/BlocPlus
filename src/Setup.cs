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

// Per-user install, no admin rights: copies the exe to %LOCALAPPDATA%\Programs\BlocPlus and
// registers a Start menu shortcut, "Open with" / default-app entries and an uninstall entry.
static class Setup
{
    public const string App = "BlocPlus", Repo = "u0f/BlocPlus";
    public static readonly string Version = typeof(Setup).Assembly.GetName().Version.ToString(3);
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BlocPlus", ProgId = "BlocPlus.Document";
    static readonly string[] Exts = ("txt log md markdown json jsonc xml yml yaml ini cfg conf toml csv tsv cs c h cpp hpp java kt js jsx ts tsx " +
        "py rb go rs php ps1 psm1 sh bat cmd sql lua html htm css scss xaml csproj props targets config env gitignore editorconfig diff patch").Split(' ');

    // One value of settings.ini, for what is needed before MainForm loads the rest; null when unset.
    public static string Ini(string key)
    {
        try
        {
            foreach (var l in File.ReadAllLines(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), App, "settings.ini")))
                if (l.StartsWith(key + "=")) return l.Substring(key.Length + 1);
        }
        catch { }
        return null;
    }

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

// Checks the latest GitHub release of the repo. An update is installed only when its SHA-256 matches
// the digest GitHub publishes for the asset, or a BlocPlus.exe.sha256 asset next to it.
static class Updater
{
    public class Release { public Version Version; public string Notes, Url, Page, Sha256; }

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
                string sums = null;
                foreach (Dictionary<string, object> a in (object[])o["assets"])
                {
                    string name = (string)a["name"];
                    if (name == Setup.App + ".exe.sha256") sums = (string)a["browser_download_url"];
                    if (name != Setup.App + ".exe") continue;
                    r.Url = (string)a["browser_download_url"];
                    object digest;
                    if (a.TryGetValue("digest", out digest) && digest is string && ((string)digest).StartsWith("sha256:"))
                        r.Sha256 = Sha(((string)digest).Substring(7));
                }
                if (r.Sha256 == null && sums != null) r.Sha256 = Sha(wc.DownloadString(sums).Trim().Split(' ', '\t', '\n')[0]);
                return r;
            }
        }
        catch (Exception ex) { Log.Write("update check", ex); return null; }
    }

    // A well-formed hex SHA-256 in lower case, or null.
    static string Sha(string s) { s = s.Trim().ToLowerInvariant(); return Regex.IsMatch(s, "^[0-9a-f]{64}$") ? s : null; }

    public static bool IsNewer(Release r) { return r != null && r.Version > Version.Parse(Setup.Version); }

    public static void Download(Release r, string path)
    {
        using (var wc = new WebClient())
        {
            wc.Headers[HttpRequestHeader.UserAgent] = Setup.App;
            wc.DownloadFile(r.Url, path);
        }
        string sum;
        using (var f = File.OpenRead(path))
        using (var sha = System.Security.Cryptography.SHA256.Create())
            sum = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        if (sum != r.Sha256)
        {
            try { File.Delete(path); } catch { }
            throw new InvalidDataException(Tr.S("La descarga está dañada o ha sido modificada: su huella SHA-256 no coincide con la publicada."));
        }
    }
}
