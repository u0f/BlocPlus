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
        mSpaces.Checked = showSpaces;
        foreach (var d in docs)
        {
            var x = d;
            Silently(x, () => { x.Box.WordWrap = wrap; x.Box.Font = font; });
            x.Gut.Visible = lineNumbers;
            x.Box.ShowSpaces = showSpaces;
            x.Box.Invalidate();
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
        d.Box.Faint = t.GutterFore;
        d.Box.WordTint = t.IsDark ? Color.FromArgb(60, 160, 160, 160) : Color.FromArgb(60, 120, 120, 120);
        d.Box.FindTint = t.IsDark ? Color.FromArgb(90, 234, 92, 0) : Color.FromArgb(110, 255, 200, 60);
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
        ThemeControls(find.Controls, t);
        find.Style(t);
        find.More.Renderer = tabMenu.Renderer = ctx.Renderer;
        Recolor(find.More.Items, t);
        ThemeResults(t);
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

}
