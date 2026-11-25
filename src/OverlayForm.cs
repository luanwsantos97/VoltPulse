using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PcPowerMonitor
{
    /// <summary>
    /// Mini Widget Flutuante (HUD) com renderizacao 32-bit ARGB premultiplied via UpdateLayeredWindow.
    /// Garante cantos arredondados 100% suaves (zero serrilhado/pixelizacao) e sombra real flutuante.
    /// </summary>
    public class OverlayForm : Form
    {
        private readonly CultureInfo _ptBr = new CultureInfo("pt-BR");
        private readonly Action _onOpenMain;
        private readonly Action _onHideOverlay;
        private readonly Action<int, int> _onMoved;

        private string _wattsNum = "--";
        private string _cpuVal = "--°";
        private string _gpuVal = "--°";
        private string _ramVal = "--%";
        private Color _cpuDot = Color.FromArgb(52, 211, 153);
        private Color _gpuDot = Color.FromArgb(52, 211, 153);
        private bool _cpuAlert;
        private bool _gpuAlert;
        private bool _isLight;
        private bool _microMode;
        private bool _isHovered;
        private double _baseOpacity = 0.84;

        private ToolStripMenuItem _microMenuItem;

        // Dimensoes da pilula visivel + margem transparente para sombra suave
        private const int ShadowPad = 7;
        private const int PillHeight = 26;
        private const int CompactPillWidth = 256;
        private const int MicroPillWidth = 190;

        // Win32 Layered Window & Drag APIs
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd,
            IntPtr hdcDst,
            ref WinPoint pptDst,
            ref WinSize psize,
            IntPtr hdcSrc,
            ref WinPoint pptSrc,
            int crKey,
            ref BLENDFUNCTION pblend,
            int dwFlags);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int ULW_ALPHA = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        private struct WinPoint
        {
            public int x;
            public int y;
            public WinPoint(int x, int y) { this.x = x; this.y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinSize
        {
            public int cx;
            public int cy;
            public WinSize(int cx, int cy) { this.cx = cx; this.cy = cy; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        public OverlayForm(int savedX, int savedY, Action onOpenMain, Action onHideOverlay, Action<int, int> onMoved)
        {
            _onOpenMain = onOpenMain;
            _onHideOverlay = onHideOverlay;
            _onMoved = onMoved;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Cursor = Cursors.SizeAll;
            Size = new Size(CompactPillWidth + ShadowPad * 2, PillHeight + ShadowPad * 2);

            if (savedX >= 0 && savedY >= 0 && IsPointVisibleOnAnyScreen(savedX, savedY))
            {
                Location = new Point(savedX, savedY);
            }
            else
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - 10, wa.Top + 8);
            }

            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir VoltPulse", null, (s, e) => { if (_onOpenMain != null) _onOpenMain(); });
            _microMenuItem = new ToolStripMenuItem("Modo Micro (ocultar RAM)", null, (s, e) => ToggleMicroMode());
            menu.Items.Add(_microMenuItem);

            var opacityMenu = new ToolStripMenuItem("Transparência");
            opacityMenu.DropDownItems.Add("Ultra discreto (68%)", null, (s, e) => SetBaseOpacity(0.68));
            opacityMenu.DropDownItems.Add("Discreto (84% — padrão)", null, (s, e) => SetBaseOpacity(0.84));
            opacityMenu.DropDownItems.Add("Sólido (96%)", null, (s, e) => SetBaseOpacity(0.96));
            menu.Items.Add(opacityMenu);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Ocultar Mini Widget", null, (s, e) => { if (_onHideOverlay != null) _onHideOverlay(); });
            ContextMenuStrip = menu;

            HandleCreated += (s, e) => RenderLayered();
            Shown += (s, e) => RenderLayered();

            MouseEnter += (s, e) => { _isHovered = true; RenderLayered(); };
            MouseLeave += (s, e) => { _isHovered = false; RenderLayered(); };

            MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (e.Clicks == 2)
                    {
                        if (_onOpenMain != null) _onOpenMain();
                        return;
                    }
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                    if (_onMoved != null) _onMoved(Left, Top);
                }
            };
        }

        private static bool IsPointVisibleOnAnyScreen(int x, int y)
        {
            var pt = new Point(x + 28, y + 16);
            foreach (var scr in Screen.AllScreens)
            {
                if (scr.WorkingArea.Contains(pt)) return true;
            }
            return false;
        }

        private void SetBaseOpacity(double value)
        {
            _baseOpacity = value;
            RenderLayered();
        }

        private void ToggleMicroMode()
        {
            _microMode = !_microMode;
            if (_microMenuItem != null) _microMenuItem.Checked = _microMode;
            int rightEdge = Right;
            int newW = (_microMode ? MicroPillWidth : CompactPillWidth) + ShadowPad * 2;
            Width = newW;
            var wa = Screen.FromControl(this).WorkingArea;
            if (rightEdge > wa.Left + wa.Width / 2)
                Left = Math.Max(wa.Left, rightEdge - Width);
            RenderLayered();
        }

        public void UpdateMetrics(TelemetrySnapshot snap, AppSettings settings)
        {
            if (IsDisposed) return;
            _isLight = settings.Theme == "light";
            _wattsNum = string.Format(_ptBr, "{0:0}", snap.EstimatedWallWatts);
            _cpuVal = snap.CpuTempC.HasValue ? string.Format(_ptBr, "{0:0}°", snap.CpuTempC.Value) : "--°";
            _gpuVal = snap.GpuTempC.HasValue ? string.Format(_ptBr, "{0:0}°", snap.GpuTempC.Value) : "--°";
            _ramVal = snap.RamLoadPercent.HasValue ? string.Format(_ptBr, "{0:0}%", snap.RamLoadPercent.Value) : "--%";

            _cpuDot = GetTempColor(snap.CpuTempC, settings.TempWarningC, settings.TempCriticalC, out _cpuAlert);
            _gpuDot = GetTempColor(snap.GpuTempC, settings.TempWarningC, settings.TempCriticalC, out _gpuAlert);
            RenderLayered();
        }

        private Color GetTempColor(double? c, double warn, double crit, out bool isAlert)
        {
            isAlert = false;
            if (!c.HasValue) return Color.FromArgb(148, 163, 184);
            if (c.Value >= crit) { isAlert = true; return Color.FromArgb(248, 113, 113); }
            if (c.Value >= warn) { isAlert = true; return Color.FromArgb(251, 191, 36); }
            if (c.Value <= 45) return _isLight ? Color.FromArgb(2, 132, 199) : Color.FromArgb(56, 189, 248);
            return _isLight ? Color.FromArgb(16, 185, 129) : Color.FromArgb(52, 211, 153);
        }

        /// <summary>
        /// Gera o Bitmap 32-bit ARGB premultiplied com anti-aliasing total (tambem usado para preview/teste).
        /// </summary>
        public Bitmap RenderToBitmap()
        {
            int pillW = _microMode ? MicroPillWidth : CompactPillWidth;
            int pillH = PillHeight;
            int totalW = pillW + ShadowPad * 2;
            int totalH = pillH + ShadowPad * 2;

            var bmp = new Bitmap(totalW, totalH, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                RectangleF pillRect = new RectangleF(ShadowPad, ShadowPad, pillW, pillH);
                float radius = pillH / 2f;

                // 1. Sombra flutuante suave (multicamada per-pixel alpha)
                for (int i = 4; i >= 1; i--)
                {
                    int alpha = _isLight ? (8 + (5 - i) * 4) : (10 + (5 - i) * 7);
                    RectangleF sRect = new RectangleF(
                        pillRect.X - i * 0.6f,
                        pillRect.Y + i * 0.65f,
                        pillRect.Width + i * 1.2f,
                        pillRect.Height + i * 0.7f);
                    using (var sp = CreateRoundedPathF(sRect, radius + i * 0.5f))
                    using (var sb = new SolidBrush(Color.FromArgb(alpha, 2, 6, 18)))
                    {
                        g.FillPath(sb, sp);
                    }
                }

                // 2. Fundo principal com gradiente sutil (vidro fosco escuro ou claro)
                Color bgTop = _isLight
                    ? Color.FromArgb(245, 255, 255, 255)
                    : Color.FromArgb(236, 17, 24, 39);
                Color bgBot = _isLight
                    ? Color.FromArgb(242, 241, 245, 249)
                    : Color.FromArgb(242, 10, 15, 26);

                using (var pillPath = CreateRoundedPathF(pillRect, radius))
                using (var bgBrush = new LinearGradientBrush(pillRect, bgTop, bgBot, LinearGradientMode.Vertical))
                {
                    g.FillPath(bgBrush, pillPath);

                    // Brilho interno superior super discreto (efeito vidro moderno)
                    RectangleF rimRect = new RectangleF(pillRect.X + 0.8f, pillRect.Y + 0.8f, pillRect.Width - 1.6f, pillRect.Height - 1.6f);
                    using (var rimPath = CreateRoundedPathF(rimRect, radius - 0.8f))
                    using (var rimPen = new Pen(_isLight ? Color.FromArgb(140, 255, 255, 255) : Color.FromArgb(22, 255, 255, 255), 1.0f))
                    {
                        g.DrawPath(rimPen, rimPath);
                    }

                    // Borda externa ultra fina 100% anti-aliased
                    Color borderCol = _isHovered
                        ? (_isLight ? Color.FromArgb(160, 2, 132, 199) : Color.FromArgb(135, 56, 189, 248))
                        : (_isLight ? Color.FromArgb(140, 148, 163, 184) : Color.FromArgb(65, 148, 163, 184));
                    using (var borderPen = new Pen(borderCol, 1.0f))
                    {
                        g.DrawPath(borderPen, pillPath);
                    }
                }

                // Paleta tipografica limpa
                Color valPrimary = _isLight ? Color.FromArgb(255, 15, 23, 42) : Color.FromArgb(255, 241, 245, 249);
                Color lblMuted = _isLight ? Color.FromArgb(255, 100, 116, 139) : Color.FromArgb(255, 132, 149, 173);
                Color cyanCol = _isLight ? Color.FromArgb(255, 2, 132, 199) : Color.FromArgb(255, 56, 189, 248);
                Color emeraldCol = _isLight ? Color.FromArgb(255, 16, 185, 129) : Color.FromArgb(255, 52, 211, 153);
                Color purpleCol = _isLight ? Color.FromArgb(255, 124, 58, 237) : Color.FromArgb(255, 192, 132, 252);
                Color sepCol = _isLight ? Color.FromArgb(55, 100, 116, 139) : Color.FromArgb(38, 255, 255, 255);

                float x0 = pillRect.X;
                float y0 = pillRect.Y;
                float cy = y0 + pillH / 2f;

                // 3. Raio vetorial com gradiente cyan -> emerald
                var bolt = new[]
                {
                    new PointF(x0 + 13.8f, y0 + 6.2f),
                    new PointF(x0 + 9.4f,  y0 + 13.6f),
                    new PointF(x0 + 12.6f, y0 + 13.6f),
                    new PointF(x0 + 11.4f, y0 + 19.8f),
                    new PointF(x0 + 16.8f, y0 + 12.0f),
                    new PointF(x0 + 13.5f, y0 + 12.0f)
                };
                using (var boltBrush = new LinearGradientBrush(
                    new PointF(x0 + 9f, y0 + 6f),
                    new PointF(x0 + 17f, y0 + 20f),
                    cyanCol, emeraldCol))
                {
                    g.FillPolygon(boltBrush, bolt);
                }

                using (var fontVal = new Font("Segoe UI Semibold", 8.5f, FontStyle.Regular, GraphicsUnit.Point))
                using (var fontUnit = new Font("Segoe UI Semibold", 7.2f, FontStyle.Regular, GraphicsUnit.Point))
                using (var fontLbl = new Font("Segoe UI Semibold", 6.5f, FontStyle.Regular, GraphicsUnit.Point))
                using (var sepPen = new Pen(sepCol, 1.0f))
                {
                    // Segmento 1: Watts (x: 19..64)
                    DrawWattsSegment(g, _wattsNum, "W", fontVal, fontUnit, valPrimary, cyanCol, x0 + 19f, y0, 44f, pillH);
                    g.DrawLine(sepPen, x0 + 65f, y0 + 7f, x0 + 65f, y0 + pillH - 7f);

                    // Segmento 2: CPU (x: 68..126)
                    DrawMetricPill(g, _cpuDot, "CPU", _cpuVal, fontLbl, fontVal, lblMuted, _cpuAlert ? _cpuDot : valPrimary, x0 + 69f, y0, 56f, pillH);
                    g.DrawLine(sepPen, x0 + 127f, y0 + 7f, x0 + 127f, y0 + pillH - 7f);

                    // Segmento 3: GPU (x: 131..187)
                    DrawMetricPill(g, _gpuDot, "GPU", _gpuVal, fontLbl, fontVal, lblMuted, _gpuAlert ? _gpuDot : valPrimary, x0 + 131f, y0, 56f, pillH);

                    if (!_microMode)
                    {
                        g.DrawLine(sepPen, x0 + 189f, y0 + 7f, x0 + 189f, y0 + pillH - 7f);
                        // Segmento 4: RAM (x: 193..251)
                        DrawMetricPill(g, purpleCol, "RAM", _ramVal, fontLbl, fontVal, lblMuted, valPrimary, x0 + 193f, y0, 58f, pillH);
                    }
                }
            }

            return bmp;
        }

        private static void DrawWattsSegment(
            Graphics g, string numText, string unitText,
            Font fontVal, Font fontUnit, Color valCol, Color unitCol,
            float x, float y, float width, float height)
        {
            using (var sf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap })
            {
                SizeF sNum = g.MeasureString(numText, fontVal, 100, sf);
                SizeF sUnit = g.MeasureString(unitText, fontUnit, 40, sf);
                float gap = 1.2f;
                float total = sNum.Width + gap + sUnit.Width;
                float sx = x + Math.Max(0, (width - total) / 2f);
                float vy = y + (height - sNum.Height) / 2f;
                float uy = y + (height - sUnit.Height) / 2f + 0.6f;

                using (var bVal = new SolidBrush(valCol))
                using (var bUnit = new SolidBrush(unitCol))
                {
                    g.DrawString(numText, fontVal, bVal, sx, vy, sf);
                    g.DrawString(unitText, fontUnit, bUnit, sx + sNum.Width + gap, uy, sf);
                }
            }
        }

        private static void DrawMetricPill(
            Graphics g, Color dotCol, string label, string val,
            Font fontLbl, Font fontVal, Color lblCol, Color valCol,
            float x, float y, float width, float height)
        {
            using (var sf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap })
            {
                float dotD = 4.4f;
                float gap1 = 3.5f;
                float gap2 = 3.0f;

                SizeF sLbl = g.MeasureString(label, fontLbl, 60, sf);
                SizeF sVal = g.MeasureString(val, fontVal, 60, sf);
                float total = dotD + gap1 + sLbl.Width + gap2 + sVal.Width;
                float sx = x + Math.Max(0, (width - total) / 2f);

                // Micro LED com brilho suave
                float dotY = y + (height - dotD) / 2f + 0.3f;
                using (var glow = new SolidBrush(Color.FromArgb(55, dotCol.R, dotCol.G, dotCol.B)))
                    g.FillEllipse(glow, sx - 1.3f, dotY - 1.3f, dotD + 2.6f, dotD + 2.6f);
                using (var dotB = new SolidBrush(dotCol))
                    g.FillEllipse(dotB, sx, dotY, dotD, dotD);

                float lx = sx + dotD + gap1;
                float ly = y + (height - sLbl.Height) / 2f + 0.5f;
                float vx = lx + sLbl.Width + gap2;
                float vy = y + (height - sVal.Height) / 2f;

                using (var bLbl = new SolidBrush(lblCol))
                using (var bVal = new SolidBrush(valCol))
                {
                    g.DrawString(label, fontLbl, bLbl, lx, ly, sf);
                    g.DrawString(val, fontVal, bVal, vx, vy, sf);
                }
            }
        }

        private void RenderLayered()
        {
            if (IsDisposed || !IsHandleCreated) return;

            using (var bmp = RenderToBitmap())
            {
                IntPtr screenDc = GetDC(IntPtr.Zero);
                IntPtr memDc = CreateCompatibleDC(screenDc);
                IntPtr hBitmap = IntPtr.Zero;
                IntPtr oldBitmap = IntPtr.Zero;

                try
                {
                    hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                    oldBitmap = SelectObject(memDc, hBitmap);

                    var size = new WinSize(bmp.Width, bmp.Height);
                    var srcLoc = new WinPoint(0, 0);
                    var topPos = new WinPoint(Left, Top);

                    double effOpacity = _isHovered ? Math.Min(0.98, _baseOpacity + 0.12) : _baseOpacity;
                    byte alphaByte = (byte)Math.Max(80, Math.Min(255, (int)Math.Round(effOpacity * 255.0)));

                    var blend = new BLENDFUNCTION
                    {
                        BlendOp = AC_SRC_OVER,
                        BlendFlags = 0,
                        SourceConstantAlpha = alphaByte,
                        AlphaFormat = AC_SRC_ALPHA
                    };

                    UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref srcLoc, 0, ref blend, ULW_ALPHA);
                }
                finally
                {
                    if (hBitmap != IntPtr.Zero)
                    {
                        SelectObject(memDc, oldBitmap);
                        DeleteObject(hBitmap);
                    }
                    DeleteDC(memDc);
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        private static GraphicsPath CreateRoundedPathF(RectangleF bounds, float radius)
        {
            float d = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_LAYERED;
                return cp;
            }
        }
    }
}
