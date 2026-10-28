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

}
