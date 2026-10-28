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
        if (r.Url == null || r.Sha256 == null) { Process.Start(r.Page); return; } // nothing to verify it against: let the user download it
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
            var xPairs = chk(Tr.S("Cerrar comillas y paréntesis automáticamente"), autoClose);
            var xShow = chk(Tr.S("Mostrar espacios y tabulaciones"), showSpaces);
            var xSingle = chk(Tr.S("Abrir los archivos en la ventana que ya está abierta"), singleInstance);
            var xSession = chk(Tr.S("Restaurar pestañas y texto sin guardar al abrir"), restoreSession);
            var xTop = chk(Tr.S("Ventana siempre visible"), TopMost);
            var xUpdates = chk(Tr.S("Buscar actualizaciones al iniciar"), checkUpdates);
            xUpdates.Visible = !Setup.Packaged; // the Store updates it

            var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 24, 0) };
            left.Controls.AddRange(new Control[] {
                head(Tr.S("Apariencia")), new Label { Text = Tr.S("Tema"), AutoSize = true }, cTheme,
                new Label { Text = Tr.S("Idioma"), AutoSize = true }, cLang, bFont, xLines, xSyntax, xShow,
                head(Tr.S("Edición")), xWrap, xIndent, xSpaces, xPairs, new Label { Text = Tr.S("Ancho del tabulador"), AutoSize = true }, cTab,
                head(Tr.S("Ventana")), xSession, xSingle, xTop, xUpdates });

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
            autoClose = xPairs.Checked;
            showSpaces = xShow.Checked;
            singleInstance = xSingle.Checked; // takes effect on the next start
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
        // In the find bar's boxes, editing keys belong to the box, not to the editor's menu shortcuts.
        if (ActiveControl is TextBox || find.ContainsFocus || results.ContainsFocus)
        {
            var key = k & Keys.KeyCode;
            bool ctrl = (k & Keys.Modifiers) == Keys.Control || (k & Keys.Modifiers) == (Keys.Control | Keys.Shift);
            if (ctrl && "ACVXZYDUK".IndexOf((char)key) >= 0 && key >= Keys.A && key <= Keys.Z || (k & Keys.Modifiers) == Keys.Alt && (key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right))
                return false;
        }
        return base.ProcessCmdKey(ref msg, k);
    }
}
