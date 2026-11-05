// The tray icon's pop-up: a mini dashboard in the dashboard's dark look (html\css\server.css colours, its fonts from
// html\fonts via PrivateFontCollection). Live once a second while open: state and version, GPU load with its graph line,
// VRAM, KV cache, connected clients, then the API address, video card, model and uptime (A-Z); buttons for Start/Stop, Restart, Console, Dashboard
// and Exit. Closes when it loses focus. C# 5 only. BCL only (Windows Forms / System.Drawing), no P/Invoke; the
// numbers come from WebServer.ServerStatusJson in-process (no HTTP).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Phoenix
{
    public sealed class TrayPanel : Form
    {
        public event Action<string> Clicked;   // button id: start, stop, restart, console, dashboard, exit

        static readonly Color Bg = Color.FromArgb(27, 27, 27), Card = Color.FromArgb(32, 32, 36), Line = Color.FromArgb(44, 44, 50),
                              Fg = Color.FromArgb(248, 247, 252), Soft = Color.FromArgb(214, 213, 224), Dim = Color.FromArgb(160, 159, 172),
                              Accent = Color.FromArgb(232, 193, 90), Ok = Color.FromArgb(76, 175, 80), Err = Color.FromArgb(224, 85, 85),
                              Track = Color.FromArgb(44, 44, 48);
        const int W = 340, Pad = 14, Radius = 4;   // layout units (every corner: the panel, cards, buttons and bars)
        // Everything is laid out in the units above and drawn S times larger (shapes, text, spacing, button hit areas).
        const float S = 1.3f;
        static int Px(int v) { return (int)Math.Round(v * S); }

        readonly PrivateFontCollection _fonts = new PrivateFontCollection();
        // Exo 2 for headings (title, card labels, row names, buttons); Syne Mono for every value
        readonly Font _title, _label, _key, _btn, _body, _big, _mono, _small;
        readonly Image _icon;
        readonly Timer _timer = new Timer();
        Dictionary<string, object> _s;   // the last /api/server-status, null = server stopped
        readonly List<double> _gpuHist = new List<double>();
        readonly string _root;

        sealed class Btn { public string Id, Label; public Rectangle R; public bool Enabled = true, Danger; }
        readonly List<Btn> _btns = new List<Btn>();
        Btn _hot;

        public TrayPanel(string root)
        {
            _root = root;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Bg;
            DoubleBuffered = true;
            Text = "Phoenix Server";
            string fonts = Path.Combine(root, "html", "fonts");
            // Headings in Exo 2 (Regular for the title, Light for the rest); values in Syne Mono
            AddFont(Path.Combine(fonts, "Exo2-Variable.ttf"));
            AddFont(Path.Combine(fonts, "SyneMono-Regular.ttf"));
            FontFamily light = Family("Exo 2 Light", "Segoe UI Light"), regular = Family("Exo 2", "Segoe UI"), mono = Family("Syne Mono", "Consolas");
            _title = new Font(regular, 12.5f, FontStyle.Regular);
            _label = new Font(light, 7.5f, FontStyle.Regular);
            _key = new Font(light, 9f, FontStyle.Regular);
            _btn = new Font(light, 9f, FontStyle.Regular);
            _body = new Font(mono, 8.5f, FontStyle.Regular);
            _big = new Font(mono, 15f, FontStyle.Regular);
            _mono = new Font(mono, 8.5f, FontStyle.Regular);
            _small = new Font(mono, 7f, FontStyle.Regular);
            try { _icon = Image.FromFile(Path.Combine(root, "html", "assets", "toolbar_icon.png")); } catch { }
            foreach (string[] b in new string[][] { new[] { "power", "Stop" }, new[] { "restart", "Restart" }, new[] { "console", "Console" },
                                                    new[] { "dashboard", "Dashboard" }, new[] { "exit", "Exit" } })
                _btns.Add(new Btn { Id = b[0], Label = b[1], Danger = b[0] == "exit" });
            ClientSize = new Size(Px(W), Px(432));
            _timer.Interval = 1000;
            _timer.Tick += delegate { Poll(); };
            Deactivate += delegate { Hide(); };
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); };
        }

        // The bundled font files (never an installed or downloaded font): a variable font adds one family per named
        // weight ("Exo 2", "Exo 2 Light", "Exo 2 Thin", ...).
        void AddFont(string file) { try { _fonts.AddFontFile(file); } catch { } }

        FontFamily Family(string name, string fallback)
        {
            try { foreach (FontFamily f in _fonts.Families) if (f.Name == name) return f; } catch { }
            return new FontFamily(fallback);
        }

        // Rounded corners and a drop shadow, like a flyout.
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x20000; /* CS_DROPSHADOW */ return cp; }
        }

        public void ShowNear(Point cursor)
        {
            Poll();
            Rectangle wa = Screen.FromPoint(cursor).WorkingArea;
            int x = Math.Max(wa.Left + 8, Math.Min(cursor.X - Width / 2, wa.Right - Width - 8));
            int y = cursor.Y > wa.Top + wa.Height / 2 ? wa.Bottom - Height - 8 : wa.Top + 8;   // above a bottom taskbar, below a top one
            Location = new Point(x, y);
            using (GraphicsPath p = Round(new Rectangle(0, 0, Width, Height), Radius)) Region = new Region(p);
            Show();
            Activate();
            _timer.Start();
        }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) _timer.Stop(); }

        void Poll()
        {
            WebServer s = ServerHost.Current;
            try { _s = s != null ? Json.Parse(s.ServerStatusJson()) as Dictionary<string, object> : null; } catch { _s = null; }
            if (_s != null) Push(_gpuHist, Json.GetNum(_s, "gpuLoad", 0));
            Invalidate();
        }

        static void Push(List<double> h, double v) { h.Add(v); if (h.Count > 40) h.RemoveAt(0); }

        // ---------- drawing ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            CultureInfo ic = CultureInfo.InvariantCulture;
            using (Pen border = new Pen(Color.FromArgb(58, 58, 66)))
            using (GraphicsPath p = Round(new Rectangle(0, 0, Width - 1, Height - 1), Px(Radius))) g.DrawPath(border, p);
            g.ScaleTransform(S, S);   // from here on: layout units

            bool on = _s != null;
            int y = Pad;

            // header: icon, name, state
            if (_icon != null) g.DrawImage(_icon, Pad, y, 22, 22);
            Str(g, "Phoenix Server", _title, Fg, Pad + 30, y + 1);
            string state = on ? "running" : "stopped";
            SizeF ss = g.MeasureString(state, _body);
            float sx = W - Pad - ss.Width;
            Str(g, state, _body, Soft, sx, y + 4);
            using (SolidBrush dot = new SolidBrush(on ? Ok : Err)) g.FillEllipse(dot, sx - 12, y + 9, 8, 8);
            y += 28;
            string ver = WebServer.AppVersion(_root);
            Str(g, (ver.Length > 0 ? "v" + ver : "") + (on ? "" : (ver.Length > 0 ? " · " : "") + "the API is closed · Start opens it"), _small, Dim, Pad, y);
            y += 20;

            // four tiles: GPU load (with its graph line), VRAM, KV cache, clients
            int tw = (W - 2 * Pad - 8) / 2, th = 66;
            Rectangle gpuR = new Rectangle(Pad, y, W - 2 * Pad, th);
            CardBox(g, gpuR, "GPU LOAD");
            double gpu = on ? Json.GetNum(_s, "gpuLoad", 0) : 0;
            Str(g, gpu.ToString("F0", ic) + "%", _big, Fg, gpuR.X + 10, gpuR.Y + 20);
            Bar(g, new Rectangle(gpuR.X + 10, gpuR.Bottom - 12, 96, 4), gpu / 100);
            Spark(g, new Rectangle(gpuR.X + 120, gpuR.Y + 14, gpuR.Width - 130, gpuR.Height - 24), _gpuHist, 100);
            y += th + 8;

            Rectangle vrR = new Rectangle(Pad, y, tw, th), kvR = new Rectangle(Pad + tw + 8, y, tw, th);
            CardBox(g, vrR, "VRAM");
            Dictionary<string, object> v = on ? _s["vram"] as Dictionary<string, object> : null;
            if (v != null)
            {
                double used = Json.GetNum(v, "used", 0), budget = Json.GetNum(v, "budget", 1);
                Str(g, (used / 1073741824).ToString("F1", ic) + " GB", _big, Fg, vrR.X + 10, vrR.Y + 20);
                string of = "of " + (budget / 1073741824).ToString("F1", ic);
                Str(g, of, _small, Dim, vrR.Right - 10 - g.MeasureString(of, _small).Width, vrR.Y + 28);
                Bar(g, new Rectangle(vrR.X + 10, vrR.Bottom - 12, vrR.Width - 20, 4), used / budget);
            }
            else Str(g, "–", _big, Dim, vrR.X + 10, vrR.Y + 20);

            CardBox(g, kvR, "KV CACHE");
            if (on && Json.GetStr(_s, "model", null) != null)
            {
                double kv = Json.GetNum(_s, "kvPct", 0);
                Str(g, kv.ToString(kv < 10 ? "F1" : "F0", ic) + "%", _big, Fg, kvR.X + 10, kvR.Y + 20);
                string kvt = Kilo((long)Json.GetNum(_s, "kvUsed", 0)) + " / " + Kilo((long)Json.GetNum(_s, "context", 0));
                Str(g, Fit(g, kvt, _small, kvR.Width - 76), _small, Dim, kvR.Right - 10 - g.MeasureString(Fit(g, kvt, _small, kvR.Width - 76), _small).Width, kvR.Y + 28);
                Bar(g, new Rectangle(kvR.X + 10, kvR.Bottom - 12, kvR.Width - 20, 4), kv / 100);
            }
            else Str(g, "–", _big, Dim, kvR.X + 10, kvR.Y + 20);
            y += th + 8;

            Rectangle clR = new Rectangle(Pad, y, W - 2 * Pad, 44);
            CardBox(g, clR, "CLIENTS");
            int connected = 0, total = 0;
            if (on)
                foreach (object o in (List<object>)_s["clients"])
                {
                    Dictionary<string, object> c = (Dictionary<string, object>)o;
                    total++;
                    if (c.ContainsKey("connected") && c["connected"] is bool && (bool)c["connected"]) connected++;
                }
            string cl = connected + " connected · " + total + " seen in the last hour";
            Str(g, Fit(g, cl, _body, clR.Width - 20), _body, Fg, clR.X + 10, clR.Y + 21);
            y += 44 + 10;

            // API, video card, loaded model and uptime, A-Z
            Rectangle infoR = new Rectangle(Pad, y, W - 2 * Pad, 102);
            CardBox(g, infoR, null);
            string api = on ? Json.GetStr(_s, "api", null) : null;
            bool key = on && _s.ContainsKey("apiKey") && _s["apiKey"] is bool && (bool)_s["apiKey"];
            string gpuName = on ? Json.GetStr(_s, "gpuName", "") : "";
            string model = on ? Json.GetStr(_s, "model", null) : null;   // the model the clients last chose
            KeyVal(g, "api", api != null ? api + (key ? " · key required" : "") : on ? "port " + WebServer.ApiPort + " busy" : "closed", infoR.X + 10, infoR.Y + 9, infoR.Width - 20);
            KeyVal(g, "gpu", gpuName.Length > 0 ? gpuName : "–", infoR.X + 10, infoR.Y + 31, infoR.Width - 20);
            KeyVal(g, "model", model != null ? model.Replace(".gguf", "") : on ? "none loaded" : "–", infoR.X + 10, infoR.Y + 53, infoR.Width - 20);
            KeyVal(g, "uptime", on ? Up((long)Json.GetNum(_s, "uptime", 0)) : "–", infoR.X + 10, infoR.Y + 75, infoR.Width - 20);
            y += 112;

            // buttons: 3 x 2
            int bw = (W - 2 * Pad - 16) / 3, bh = 30;
            for (int i = 0; i < _btns.Count; i++)
            {
                Btn b = _btns[i];
                b.R = new Rectangle(Pad + (i % 3) * (bw + 8), y + (i / 3) * (bh + 8), bw, bh);
                if (b.Id == "power") b.Label = on ? "Stop" : "Start";
                b.Enabled = on || b.Id == "power" || b.Id == "exit";
                DrawBtn(g, b);
            }
            int needed = Px(y + 2 * bh + 8 + Pad);
            if (ClientSize.Height != needed || ClientSize.Width != Px(W))
            {
                ClientSize = new Size(Px(W), needed);
                using (GraphicsPath p = Round(new Rectangle(0, 0, Width, Height), Px(Radius))) Region = new Region(p);
                if (Visible) { Rectangle wa = Screen.FromControl(this).WorkingArea; if (Bottom > wa.Bottom - 8) Top = wa.Bottom - 8 - Height; }   // grew past the taskbar
            }
        }

        // Token counts short enough for a half-width tile in the monospaced font: 840, 5.5k, 131k.
        static string Kilo(long n)
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            if (n < 1000) return n.ToString(ic);
            return n < 10000 ? (n / 1000.0).ToString("0.#", ic) + "k" : (n / 1000).ToString(ic) + "k";
        }

        static string Up(long s) { long h = s / 3600, m = s % 3600 / 60; return (h > 0 ? h + "h " : "") + m + "m " + (s % 60) + "s"; }

        static void Str(Graphics g, string s, Font f, Color c, float x, float y) { using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, x, y); }

        static string Fit(Graphics g, string s, Font f, int w)
        {
            if (g.MeasureString(s, f).Width <= w) return s;
            while (s.Length > 1 && g.MeasureString(s + "…", f).Width > w) s = s.Substring(0, s.Length - 1);
            return s + "…";
        }

        void CardBox(Graphics g, Rectangle r, string label)
        {
            using (GraphicsPath p = Round(r, Radius))
            using (SolidBrush b = new SolidBrush(Card))
            using (Pen pen = new Pen(Line)) { g.FillPath(b, p); g.DrawPath(pen, p); }
            if (label != null) Str(g, label, _label, Dim, r.X + 10, r.Y + 6);
        }

        void KeyVal(Graphics g, string k, string v, int x, int y, int w)
        {
            Str(g, k, _key, Dim, x, y);
            Str(g, Fit(g, v, _mono, w - 56), _mono, Fg, x + 56, y + 1);
        }

        static void Bar(Graphics g, Rectangle r, double frac)
        {
            frac = Math.Max(0, Math.Min(1, frac));
            int rad = Math.Min(Radius, r.Height / 2);   // a 4 px bar: fully rounded ends
            using (GraphicsPath tp = Round(r, rad))
            using (SolidBrush t = new SolidBrush(Track)) g.FillPath(t, tp);
            int fw = (int)(r.Width * frac);
            if (fw < 2 * rad) return;
            using (GraphicsPath ap = Round(new Rectangle(r.X, r.Y, fw, r.Height), rad))
            using (SolidBrush a = new SolidBrush(Accent)) g.FillPath(a, ap);
        }

        static void Spark(Graphics g, Rectangle r, List<double> h, double max)
        {
            if (h.Count < 2) return;
            double m = max;
            foreach (double v in h) m = Math.Max(m, v);
            if (m <= 0) m = 1;
            PointF[] pts = new PointF[h.Count];
            for (int i = 0; i < h.Count; i++) pts[i] = new PointF(r.X + (float)i * r.Width / 39f + (40 - h.Count) * r.Width / 39f, r.Bottom - (float)(h[i] / m) * r.Height);
            using (Pen p = new Pen(Color.FromArgb(200, Accent), 1.4f)) g.DrawLines(p, pts);
        }

        void DrawBtn(Graphics g, Btn b)
        {
            bool hot = b == _hot && b.Enabled;
            // Start green, Stop red, Restart gold (always); the rest neutral, Exit red only under the mouse
            Color tint = b.Id == "power" ? (b.Label == "Start" ? Ok : Err) : b.Id == "restart" ? Accent : Color.Empty;
            Color edge, c, fill;
            if (!b.Enabled) { edge = Line; c = Dim; fill = Card; }
            else if (tint != Color.Empty) { edge = Color.FromArgb(hot ? 230 : 140, tint); c = Fg; fill = Color.FromArgb(hot ? 60 : 28, tint); }
            else { edge = hot ? (b.Danger ? Err : Color.FromArgb(110, 110, 120)) : Color.FromArgb(58, 58, 66); c = Fg; fill = hot ? Color.FromArgb(40, 40, 46) : Card; }
            using (GraphicsPath p = Round(b.R, Radius))
            using (SolidBrush bg = new SolidBrush(Blend(fill)))
            using (Pen pen = new Pen(edge)) { g.FillPath(bg, p); g.DrawPath(pen, p); }
            SizeF sz = g.MeasureString(b.Label, _btn);
            Str(g, b.Label, _btn, c, b.R.X + (b.R.Width - sz.Width) / 2, b.R.Y + (b.R.Height - sz.Height) / 2);
        }

        // A see-through tint laid over the card colour (the panel has no real transparency).
        static Color Blend(Color c)
        {
            if (c.A == 255) return c;
            float a = c.A / 255f;
            return Color.FromArgb((int)(c.R * a + Card.R * (1 - a)), (int)(c.G * a + Card.G * (1 - a)), (int)(c.B * a + Card.B * (1 - a)));
        }

        static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---------- mouse ----------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            Btn h = null;
            Point at = new Point((int)(e.X / S), (int)(e.Y / S));   // button rectangles are in layout units
            foreach (Btn b in _btns) if (b.R.Contains(at) && b.Enabled) h = b;
            if (h != _hot) { _hot = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { if (_hot != null) { _hot = null; Invalidate(); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _hot == null) return;
            string id = _hot.Id == "power" ? (_s != null ? "stop" : "start") : _hot.Id;
            // window-opening actions and Exit close the panel; Start / Stop / Restart keep it open to watch the state change
            if (id != "start" && id != "stop" && id != "restart") Hide();
            if (Clicked != null) Clicked(id);
        }
    }
}
