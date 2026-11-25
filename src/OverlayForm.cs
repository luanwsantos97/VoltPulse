using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PcPowerMonitor
{
    public class OverlayForm : Form
    {
        private readonly CultureInfo _ptBr = new CultureInfo("pt-BR");
        private readonly Action _onOpenMain;
        private readonly Action _onHideOverlay;
        private readonly Action<int, int> _onMoved;

        private string _wattsVal = "--W";
        private string _cpuVal = "--°";
        private string _gpuVal = "--°";
        private string _ramVal = "--%";
        private Color _cpuColor = Color.FromArgb(52, 211, 153);
        private Color _gpuColor = Color.FromArgb(52, 211, 153);
        private bool _isLight;
        private bool _microMode;
        private double _baseOpacity = 0.76;

        private ToolStripMenuItem _microMenuItem;

        private const int OverlayHeight = 24;
        private const int CompactWidth = 236;
        private const int MicroWidth = 178;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        public OverlayForm(int savedX, int savedY, Action onOpenMain, Action onHideOverlay, Action<int, int> onMoved)
        {
            _onOpenMain = onOpenMain;
            _onHideOverlay = onHideOverlay;
            _onMoved = onMoved;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Opacity = _baseOpacity;
            Size = new Size(CompactWidth, OverlayHeight);
            StartPosition = FormStartPosition.Manual;
            Cursor = Cursors.SizeAll;

            if (savedX >= 0 && savedY >= 0 && IsPointVisibleOnAnyScreen(savedX, savedY))
            {
                Location = new Point(savedX, savedY);
            }
            else
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - 14, wa.Top + 12);
            }

            ApplyRoundedRegion();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir VoltPulse", null, (s, e) => { if (_onOpenMain != null) _onOpenMain(); });
            _microMenuItem = new ToolStripMenuItem("Modo Micro (ocultar RAM)", null, (s, e) => ToggleMicroMode());
            menu.Items.Add(_microMenuItem);

            var opacityMenu = new ToolStripMenuItem("Transparência");
            opacityMenu.DropDownItems.Add("Ultra discreto (65%)", null, (s, e) => SetBaseOpacity(0.65));
            opacityMenu.DropDownItems.Add("Discreto (76% — padrão)", null, (s, e) => SetBaseOpacity(0.76));
            opacityMenu.DropDownItems.Add("Sólido (92%)", null, (s, e) => SetBaseOpacity(0.92));
            menu.Items.Add(opacityMenu);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Ocultar Mini Widget", null, (s, e) => { if (_onHideOverlay != null) _onHideOverlay(); });
            ContextMenuStrip = menu;

            MouseEnter += (s, e) => { Opacity = Math.Min(0.95, _baseOpacity + 0.16); };
            MouseLeave += (s, e) => { Opacity = _baseOpacity; };

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
            var pt = new Point(x + 20, y + 10);
            foreach (var scr in Screen.AllScreens)
            {
                if (scr.WorkingArea.Contains(pt)) return true;
            }
            return false;
        }

        private void SetBaseOpacity(double value)
        {
            _baseOpacity = value;
            Opacity = _baseOpacity;
        }

        private void ToggleMicroMode()
        {
            _microMode = !_microMode;
            if (_microMenuItem != null) _microMenuItem.Checked = _microMode;
            int rightEdge = Right;
            Width = _microMode ? MicroWidth : CompactWidth;
            // Mantém alinhado à direita se estiver perto da borda direita
            var wa = Screen.FromControl(this).WorkingArea;
            if (rightEdge > wa.Left + wa.Width / 2)
                Left = Math.Max(wa.Left, rightEdge - Width);
            ApplyRoundedRegion();
            Invalidate();
        }

        private void ApplyRoundedRegion()
        {
            using (var path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), 11))
            {
                Region = new Region(path);
            }
        }

        public void UpdateMetrics(TelemetrySnapshot snap, AppSettings settings)
        {
            if (IsDisposed) return;
            _isLight = settings.Theme == "light";
            _wattsVal = string.Format(_ptBr, "{0:0}W", snap.EstimatedWallWatts);
            _cpuVal = snap.CpuTempC.HasValue ? string.Format(_ptBr, "{0:0}°", snap.CpuTempC.Value) : "--°";
            _gpuVal = snap.GpuTempC.HasValue ? string.Format(_ptBr, "{0:0}°", snap.GpuTempC.Value) : "--°";
            _ramVal = snap.RamLoadPercent.HasValue ? string.Format(_ptBr, "{0:0}%", snap.RamLoadPercent.Value) : "--%";

            _cpuColor = GetTempColor(snap.CpuTempC, settings.TempWarningC, settings.TempCriticalC);
            _gpuColor = GetTempColor(snap.GpuTempC, settings.TempWarningC, settings.TempCriticalC);
            Invalidate();
        }

        private Color GetTempColor(double? c, double warn, double crit)
        {
            if (!c.HasValue) return Color.FromArgb(148, 163, 184);
            if (c.Value >= crit) return Color.FromArgb(248, 113, 113);
            if (c.Value >= warn) return Color.FromArgb(251, 191, 36);
            if (c.Value <= 45) return _isLight ? Color.FromArgb(2, 132, 199) : Color.FromArgb(125, 211, 252);
            return _isLight ? Color.FromArgb(5, 150, 105) : Color.FromArgb(110, 231, 183);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color bg = _isLight ? Color.FromArgb(248, 250, 252) : Color.FromArgb(12, 17, 28);
            Color border = _isLight ? Color.FromArgb(203, 213, 225) : Color.FromArgb(42, 54, 74);
            Color sepCol = _isLight ? Color.FromArgb(226, 232, 240) : Color.FromArgb(36, 47, 66);
            Color lblCol = _isLight ? Color.FromArgb(100, 116, 139) : Color.FromArgb(120, 136, 160);
            Color cyanCol = _isLight ? Color.FromArgb(2, 132, 199) : Color.FromArgb(56, 189, 248);
            Color purpleCol = _isLight ? Color.FromArgb(124, 58, 237) : Color.FromArgb(196, 181, 253);

            using (var path = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 11))
            using (var br = new SolidBrush(bg))
            using (var pen = new Pen(border, 1.0f))
            {
                g.FillPath(br, path);
                g.DrawPath(pen, path);
            }

            // Mini raio discreto à esquerda
            var bolt = new[]
            {
                new PointF(12.2f, 5.5f),
                new PointF(8.4f, 12.4f),
                new PointF(11.3f, 12.4f),
                new PointF(10.1f, 18.5f),
                new PointF(15.2f, 10.8f),
                new PointF(12.2f, 10.8f)
            };
            using (var b = new SolidBrush(cyanCol))
                g.FillPolygon(b, bolt);

            using (var fontVal = new Font("Segoe UI Semibold", 8.0f))
            using (var fontLbl = new Font("Segoe UI", 7.0f))
            using (var sepPen = new Pen(sepCol, 1f))
            {
                // Watts
                DrawSingle(g, _wattsVal, fontVal, cyanCol, 17, 43);
                g.DrawLine(sepPen, 62, 6, 62, Height - 6);

                // CPU
                DrawLabeled(g, "C", _cpuVal, fontLbl, fontVal, lblCol, _cpuColor, 65, 50);
                g.DrawLine(sepPen, 117, 6, 117, Height - 6);

                // GPU
                DrawLabeled(g, "G", _gpuVal, fontLbl, fontVal, lblCol, _gpuColor, 120, 50);

                if (!_microMode)
                {
                    g.DrawLine(sepPen, 172, 6, 172, Height - 6);
                    DrawLabeled(g, "R", _ramVal, fontLbl, fontVal, lblCol, purpleCol, 175, 53);
                }
            }
        }

        private void DrawSingle(Graphics g, string text, Font font, Color color, int x, int width)
        {
            var rect = new Rectangle(x, 0, width, Height);
            TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawLabeled(Graphics g, string label, string value, Font fontLbl, Font fontVal, Color lblColor, Color valColor, int x, int width)
        {
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            Size szLbl = TextRenderer.MeasureText(g, label, fontLbl, new Size(width, Height), flags);
            Size szVal = TextRenderer.MeasureText(g, value, fontVal, new Size(width, Height), flags);
            int gap = 3;
            int total = szLbl.Width + gap + szVal.Width;
            int startX = x + Math.Max(0, (width - total) / 2);

            TextRenderer.DrawText(g, label, fontLbl, new Rectangle(startX, 1, szLbl.Width, Height - 1), lblColor, flags);
            TextRenderer.DrawText(g, value, fontVal, new Rectangle(startX + szLbl.Width + gap, 0, szVal.Width, Height), valColor, flags);
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            int d = radius * 2;
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
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW (oculta do Alt+Tab)
                return cp;
            }
        }
    }
}
