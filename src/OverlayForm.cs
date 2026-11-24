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

        private string _wattsText = "-- W";
        private string _cpuText = "CPU --°";
        private string _gpuText = "GPU --°";
        private string _ramText = "RAM --%";
        private Color _cpuColor = Color.FromArgb(52, 211, 153);
        private Color _gpuColor = Color.FromArgb(52, 211, 153);
        private bool _isLight;

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
            Opacity = 0.92;
            Size = new Size(372, 38);
            StartPosition = FormStartPosition.Manual;
            Cursor = Cursors.SizeAll;

            if (savedX >= 0 && savedY >= 0)
            {
                Location = new Point(savedX, savedY);
            }
            else
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - 18, wa.Top + 18);
            }

            ApplyRoundedRegion();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir VoltPulse", null, (s, e) => { if (_onOpenMain != null) _onOpenMain(); });
            menu.Items.Add("Ocultar Mini Widget", null, (s, e) => { if (_onHideOverlay != null) _onHideOverlay(); });
            ContextMenuStrip = menu;

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

        private void ApplyRoundedRegion()
        {
            using (var path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), 14))
            {
                Region = new Region(path);
            }
        }

        public void UpdateMetrics(TelemetrySnapshot snap, AppSettings settings)
        {
            if (IsDisposed) return;
            _isLight = settings.Theme == "light";
            _wattsText = string.Format(_ptBr, "{0:0} W", snap.EstimatedWallWatts);
            _cpuText = snap.CpuTempC.HasValue ? string.Format(_ptBr, "CPU {0:0}°C", snap.CpuTempC.Value) : "CPU --°";
            _gpuText = snap.GpuTempC.HasValue ? string.Format(_ptBr, "GPU {0:0}°C", snap.GpuTempC.Value) : "GPU --°";
            _ramText = snap.RamLoadPercent.HasValue ? string.Format(_ptBr, "RAM {0:0}%", snap.RamLoadPercent.Value) : "RAM --%";

            _cpuColor = GetTempColor(snap.CpuTempC, settings.TempWarningC, settings.TempCriticalC);
            _gpuColor = GetTempColor(snap.GpuTempC, settings.TempWarningC, settings.TempCriticalC);
            Invalidate();
        }

        private Color GetTempColor(double? c, double warn, double crit)
        {
            if (!c.HasValue) return Color.FromArgb(148, 163, 184);
            if (c.Value >= crit) return Color.FromArgb(248, 113, 113);
            if (c.Value >= warn) return Color.FromArgb(251, 191, 36);
            if (c.Value <= 45) return _isLight ? Color.FromArgb(2, 132, 199) : Color.FromArgb(56, 189, 248);
            return _isLight ? Color.FromArgb(5, 150, 105) : Color.FromArgb(52, 211, 153);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color bg = _isLight ? Color.FromArgb(248, 250, 252) : Color.FromArgb(15, 23, 42);
            Color border = _isLight ? Color.FromArgb(2, 132, 199) : Color.FromArgb(56, 189, 248);
            Color sepCol = _isLight ? Color.FromArgb(203, 213, 225) : Color.FromArgb(51, 65, 85);
            Color cyanCol = _isLight ? Color.FromArgb(2, 132, 199) : Color.FromArgb(56, 189, 248);
            Color purpleCol = _isLight ? Color.FromArgb(124, 58, 237) : Color.FromArgb(167, 139, 250);

            using (var path = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 14))
            using (var br = new SolidBrush(bg))
            using (var pen = new Pen(border, 1.3f))
            {
                g.FillPath(br, path);
                g.DrawPath(pen, path);
            }

            // Mini raio à esquerda
            var bolt = new[]
            {
                new PointF(18, 8),
                new PointF(12, 20),
                new PointF(17, 20),
                new PointF(15, 30),
                new PointF(23, 17),
                new PointF(18, 17)
            };
            using (var b = new SolidBrush(cyanCol))
                g.FillPolygon(b, bolt);

            using (var fontBold = new Font("Segoe UI Semibold", 9.3f))
            using (var sepPen = new Pen(sepCol, 1f))
            {
                DrawSegment(g, _wattsText, fontBold, cyanCol, 28, 66);
                g.DrawLine(sepPen, 98, 9, 98, Height - 9);

                DrawSegment(g, _cpuText, fontBold, _cpuColor, 104, 82);
                g.DrawLine(sepPen, 190, 9, 190, Height - 9);

                DrawSegment(g, _gpuText, fontBold, _gpuColor, 196, 82);
                g.DrawLine(sepPen, 282, 9, 282, Height - 9);

                DrawSegment(g, _ramText, fontBold, purpleCol, 288, 76);
            }
        }

        private void DrawSegment(Graphics g, string text, Font font, Color color, int x, int width)
        {
            var rect = new Rectangle(x, 0, width, Height);
            TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
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
