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
