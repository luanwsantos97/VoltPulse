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
    /// Recursos:
    /// - Bordas 100% anti-aliased e sombra suave real
    /// - Layout Horizontal ou Vertical alternavel direto no clique direito
    /// - Modo Auto-Ocultar (revela ao passar o mouse em cima)
    /// - Contador de FPS automatico durante jogos (via ETW seguro, zero injecao)
    /// - Modo Micro (oculta RAM) e 3 niveis de transparencia
    /// </summary>
    public class OverlayForm : Form
    {
        private readonly CultureInfo _ptBr = new CultureInfo("pt-BR");
        private readonly Action _onOpenMain;
        private readonly Action _onHideOverlay;
        private readonly Action<int, int> _onMoved;
        private readonly Action<bool, bool, bool, double> _onPrefsChanged;

        private string _wattsNum = "--";
        private string _cpuVal = "--°";
        private string _gpuVal = "--°";
        private string _ramVal = "--%";
        private int? _fpsVal;
        private Color _cpuDot = Color.FromArgb(52, 211, 153);
        private Color _gpuDot = Color.FromArgb(52, 211, 153);
        private bool _cpuAlert;
        private bool _gpuAlert;
        private bool _isLight;

        private bool _verticalMode;
        private bool _microMode;
        private bool _autoHide;
        private double _baseOpacity = 0.84;

        private bool _isHovered;
        private bool _menuOpen;
        private bool _gameClickThrough;
        private readonly Timer _hideDelayTimer;

        private ToolStripMenuItem _verticalMenuItem;
        private ToolStripMenuItem _microMenuItem;
        private ToolStripMenuItem _autoHideMenuItem;

        // Dimensoes das pilulas/cartoes + margem transparente para sombra suave
        private const int ShadowPad = 7;
        private const int HorizPillHeight = 26;
        private const int HorizCompactW = 256;
        private const int HorizMicroW = 190;
        private const int HorizFpsExtraW = 62;

        private const int VertCardWidth = 108;
        private const int VertRowH = 20;
        private const int VertPadV = 7;

        // Win32 Layered Window & Drag APIs (somente System32)
        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool DeleteObject(IntPtr hObject);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
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

        public OverlayForm(
            AppSettings settings,
            Action onOpenMain,
            Action onHideOverlay,
            Action<int, int> onMoved,
            Action<bool, bool, bool, double> onPrefsChanged)
        {
            _onOpenMain = onOpenMain;
            _onHideOverlay = onHideOverlay;
            _onMoved = onMoved;
            _onPrefsChanged = onPrefsChanged;

            if (settings != null)
            {
                _verticalMode = settings.OverlayVertical;
                _microMode = settings.OverlayMicroMode;
                _autoHide = settings.OverlayAutoHide;
                _baseOpacity = (settings.OverlayOpacity >= 0.45 && settings.OverlayOpacity <= 1.0)
                    ? settings.OverlayOpacity : 0.84;
                _isLight = settings.Theme == "light";
            }

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Cursor = Cursors.SizeAll;

            Size = ComputeCurrentFormSize();

            int savedX = settings != null ? settings.OverlayX : -1;
            int savedY = settings != null ? settings.OverlayY : -1;

            if (savedX >= 0 && savedY >= 0 && IsPointVisibleOnAnyScreen(savedX, savedY))
            {
                var scr = Screen.FromPoint(new Point(savedX + 20, savedY + 10));
                var wa = scr.WorkingArea;
                int cx = Math.Max(wa.Left, Math.Min(savedX, wa.Right - Width));
                int cy = Math.Max(wa.Top, Math.Min(savedY, wa.Bottom - Height));
                Location = new Point(cx, cy);
            }
            else
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - 10, wa.Top + 8);
            }

            _hideDelayTimer = new Timer { Interval = 450 };
            _hideDelayTimer.Tick += (s, e) =>
            {
                _hideDelayTimer.Stop();
                if (!ClientRectangle.Contains(PointToClient(Cursor.Position)) && !_menuOpen)
                {
                    _isHovered = false;
                    RenderLayered();
                }
            };

            var menu = new ContextMenuStrip();
            menu.Opening += (s, e) => { _menuOpen = true; _hideDelayTimer.Stop(); RenderLayered(); };
            menu.Closed += (s, e) => { _menuOpen = false; _hideDelayTimer.Start(); };

            menu.Items.Add("Abrir VoltPulse", null, (s, e) => { if (_onOpenMain != null) _onOpenMain(); });
            menu.Items.Add(new ToolStripSeparator());

            _verticalMenuItem = new ToolStripMenuItem("Layout Vertical", null, (s, e) => ToggleVerticalMode()) { Checked = _verticalMode };
            _microMenuItem = new ToolStripMenuItem("Modo Micro (ocultar RAM)", null, (s, e) => ToggleMicroMode()) { Checked = _microMode };
            _autoHideMenuItem = new ToolStripMenuItem("Auto-ocultar (revelar ao passar o mouse)", null, (s, e) => ToggleAutoHide()) { Checked = _autoHide };
            menu.Items.Add(_verticalMenuItem);
            menu.Items.Add(_microMenuItem);
            menu.Items.Add(_autoHideMenuItem);

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

            MouseEnter += (s, e) =>
            {
                _hideDelayTimer.Stop();
                if (!_isHovered)
                {
                    _isHovered = true;
                    RenderLayered();
                }
            };
            MouseLeave += (s, e) =>
            {
                if (_autoHide)
                {
                    _hideDelayTimer.Stop();
                    _hideDelayTimer.Start();
                }
                else
                {
                    _isHovered = false;
                    RenderLayered();
                }
            };

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

        private bool HasFps { get { return _fpsVal.HasValue && _fpsVal.Value > 0; } }

        private Size ComputePillSize()
        {
            if (_verticalMode)
            {
                int rows = 3 + (_microMode ? 0 : 1) + (HasFps ? 1 : 0);
                int h = VertPadV * 2 + rows * VertRowH + (HasFps ? 5 : 0);
                return new Size(VertCardWidth, h);
            }
            else
            {
                int w = (_microMode ? HorizMicroW : HorizCompactW) + (HasFps ? HorizFpsExtraW : 0);
                return new Size(w, HorizPillHeight);
            }
        }

        private Size ComputeCurrentFormSize()
        {
            var pill = ComputePillSize();
            return new Size(pill.Width + ShadowPad * 2, pill.Height + ShadowPad * 2);
        }

        private void ApplyResizeAndAnchor()
        {
            Size target = ComputeCurrentFormSize();
            if (Size == target) return;

            int oldRight = Right;
            var wa = Screen.FromControl(this).WorkingArea;
            Size = target;

            // Se estava ancorado na metade direita, mantem a borda direita alinhada!
            if (oldRight > wa.Left + wa.Width / 2)
            {
                Left = Math.Max(wa.Left, Math.Min(oldRight - Width, wa.Right - Width));
            }
            if (Top + Height > wa.Bottom)
            {
                Top = Math.Max(wa.Top, wa.Bottom - Height);
            }
            if (_onMoved != null) _onMoved(Left, Top);
        }

        private void NotifyPrefs()
        {
            if (_onPrefsChanged != null)
                _onPrefsChanged(_verticalMode, _microMode, _autoHide, _baseOpacity);
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
            NotifyPrefs();
            RenderLayered();
        }

        public void SetModesForPreview(bool vertical, bool micro, bool autoHide, bool hovered)
        {
            _verticalMode = vertical;
            _microMode = micro;
            _autoHide = autoHide;
            _isHovered = hovered;
            Size = ComputeCurrentFormSize();
        }

        private void ToggleVerticalMode()
        {
            _verticalMode = !_verticalMode;
            if (_verticalMenuItem != null) _verticalMenuItem.Checked = _verticalMode;
            ApplyResizeAndAnchor();
            NotifyPrefs();
            RenderLayered();
        }

        private void ToggleMicroMode()
        {
            _microMode = !_microMode;
            if (_microMenuItem != null) _microMenuItem.Checked = _microMode;
            ApplyResizeAndAnchor();
            NotifyPrefs();
            RenderLayered();
        }

        private void ToggleAutoHide()
        {
            _autoHide = !_autoHide;
            if (_autoHideMenuItem != null) _autoHideMenuItem.Checked = _autoHide;
            NotifyPrefs();
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

            bool hadFps = HasFps;
            _fpsVal = (snap.CurrentFps.HasValue && snap.CurrentFps.Value > 0) ? snap.CurrentFps : null;

            _cpuDot = GetTempColor(snap.CpuTempC, settings.TempWarningC, settings.TempCriticalC, out _cpuAlert);
            _gpuDot = GetTempColor(snap.GpuTempC, settings.TempWarningC, settings.TempCriticalC, out _gpuAlert);

            if (hadFps != HasFps)
            {
                ApplyResizeAndAnchor();
            }
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

        private Color GetFpsColor(int fps)
        {
            if (fps >= 60) return _isLight ? Color.FromArgb(5, 150, 105) : Color.FromArgb(52, 211, 153);
            if (fps >= 30) return _isLight ? Color.FromArgb(217, 119, 6) : Color.FromArgb(251, 191, 36);
            return _isLight ? Color.FromArgb(220, 38, 38) : Color.FromArgb(248, 113, 113);
        }

        public Bitmap RenderToBitmap()
        {
            Size pill = ComputePillSize();
            int pillW = pill.Width;
            int pillH = pill.Height;
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

                // MODO AUTO-OCULTAR RECOLHIDO: area inteira invisivel (alpha=1 para detectar MouseEnter facil)
                // + micro aba/tirinha elegante para o usuario saber exatamente onde passar o mouse!
                if (_autoHide && !_isHovered && !_menuOpen)
                {
                    using (var hitBrush = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
                    {
                        g.FillRectangle(hitBrush, pillRect);
                    }

                    RectangleF handleRect = _verticalMode
                        ? new RectangleF(pillRect.Right - 6f, pillRect.Y + (pillH - 36f) / 2f, 4.5f, 36f)
                        : new RectangleF(pillRect.X + (pillW - 44f) / 2f, pillRect.Y + 3f, 44f, 4.5f);

                    float hr = 2.2f;
                    Color hTop = _isLight ? Color.FromArgb(145, 2, 132, 199) : Color.FromArgb(130, 56, 189, 248);
                    Color hBot = _isLight ? Color.FromArgb(145, 16, 185, 129) : Color.FromArgb(130, 52, 211, 153);
                    using (var hp = CreateRoundedPathF(handleRect, hr))
                    using (var hb = new LinearGradientBrush(handleRect, hTop, hBot, _verticalMode ? LinearGradientMode.Vertical : LinearGradientMode.Horizontal))
                    {
                        g.FillPath(hb, hp);
                    }
                    return bmp;
                }

                float radius = _verticalMode ? 12f : (pillH / 2f);

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

                    RectangleF rimRect = new RectangleF(pillRect.X + 0.8f, pillRect.Y + 0.8f, pillRect.Width - 1.6f, pillRect.Height - 1.6f);
                    using (var rimPath = CreateRoundedPathF(rimRect, radius - 0.8f))
                    using (var rimPen = new Pen(_isLight ? Color.FromArgb(140, 255, 255, 255) : Color.FromArgb(22, 255, 255, 255), 1.0f))
                    {
                        g.DrawPath(rimPen, rimPath);
                    }

                    Color borderCol = _isHovered
                        ? (_isLight ? Color.FromArgb(160, 2, 132, 199) : Color.FromArgb(135, 56, 189, 248))
                        : (_isLight ? Color.FromArgb(140, 148, 163, 184) : Color.FromArgb(65, 148, 163, 184));
                    using (var borderPen = new Pen(borderCol, 1.0f))
                    {
                        g.DrawPath(borderPen, pillPath);
                    }
                }

                Color valPrimary = _isLight ? Color.FromArgb(255, 15, 23, 42) : Color.FromArgb(255, 241, 245, 249);
                Color lblMuted = _isLight ? Color.FromArgb(255, 100, 116, 139) : Color.FromArgb(255, 132, 149, 173);
                Color cyanCol = _isLight ? Color.FromArgb(255, 2, 132, 199) : Color.FromArgb(255, 56, 189, 248);
                Color emeraldCol = _isLight ? Color.FromArgb(255, 16, 185, 129) : Color.FromArgb(255, 52, 211, 153);
                Color purpleCol = _isLight ? Color.FromArgb(255, 124, 58, 237) : Color.FromArgb(255, 192, 132, 252);
                Color sepCol = _isLight ? Color.FromArgb(55, 100, 116, 139) : Color.FromArgb(38, 255, 255, 255);

                if (_verticalMode)
                    DrawVerticalContent(g, pillRect, valPrimary, lblMuted, cyanCol, emeraldCol, purpleCol, sepCol);
                else
                    DrawHorizontalContent(g, pillRect, valPrimary, lblMuted, cyanCol, emeraldCol, purpleCol, sepCol);
            }

            return bmp;
        }

        private void DrawHorizontalContent(
            Graphics g, RectangleF pillRect,
            Color valPrimary, Color lblMuted, Color cyanCol, Color emeraldCol, Color purpleCol, Color sepCol)
        {
            float x0 = pillRect.X;
            float y0 = pillRect.Y;
            float pillH = pillRect.Height;

            // Raio vetorial
            DrawBolt(g, x0 + 9.4f, y0 + 6.2f, cyanCol, emeraldCol);

            using (var fontVal = new Font("Segoe UI Semibold", 8.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (var fontUnit = new Font("Segoe UI Semibold", 7.2f, FontStyle.Regular, GraphicsUnit.Point))
            using (var fontLbl = new Font("Segoe UI Semibold", 6.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (var sepPen = new Pen(sepCol, 1.0f))
            {
                float curX = x0 + 19f;

                // FPS (somente quando ha jogo rodando com frames ativos!)
                if (HasFps)
                {
                    int fps = _fpsVal.Value;
                    Color fpsCol = GetFpsColor(fps);
                    DrawWattsSegment(g, fps.ToString(), "FPS", fontVal, fontLbl, fpsCol, lblMuted, curX, y0, 56f, pillH);
                    curX += 58f;
                    g.DrawLine(sepPen, curX, y0 + 7f, curX, y0 + pillH - 7f);
                    curX += 4f;
                }

                // Watts
                DrawWattsSegment(g, _wattsNum, "W", fontVal, fontUnit, valPrimary, cyanCol, curX, y0, 44f, pillH);
                curX += 46f;
                g.DrawLine(sepPen, curX, y0 + 7f, curX, y0 + pillH - 7f);
                curX += 4f;

                // CPU
                DrawMetricPill(g, _cpuDot, "CPU", _cpuVal, fontLbl, fontVal, lblMuted, _cpuAlert ? _cpuDot : valPrimary, curX, y0, 56f, pillH);
                curX += 58f;
                g.DrawLine(sepPen, curX, y0 + 7f, curX, y0 + pillH - 7f);
                curX += 4f;

                // GPU
                DrawMetricPill(g, _gpuDot, "GPU", _gpuVal, fontLbl, fontVal, lblMuted, _gpuAlert ? _gpuDot : valPrimary, curX, y0, 56f, pillH);

                if (!_microMode)
                {
                    curX += 58f;
                    g.DrawLine(sepPen, curX, y0 + 7f, curX, y0 + pillH - 7f);
                    curX += 4f;
                    DrawMetricPill(g, purpleCol, "RAM", _ramVal, fontLbl, fontVal, lblMuted, valPrimary, curX, y0, 58f, pillH);
                }
            }
        }

        private void DrawVerticalContent(
            Graphics g, RectangleF pillRect,
            Color valPrimary, Color lblMuted, Color cyanCol, Color emeraldCol, Color purpleCol, Color sepCol)
        {
            float x0 = pillRect.X;
            float y = pillRect.Y + VertPadV;
            float w = pillRect.Width;

            using (var fontVal = new Font("Segoe UI Semibold", 8.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (var fontFps = new Font("Segoe UI Bold", 9.2f, FontStyle.Bold, GraphicsUnit.Point))
            using (var fontUnit = new Font("Segoe UI Semibold", 7.0f, FontStyle.Regular, GraphicsUnit.Point))
            using (var fontLbl = new Font("Segoe UI Semibold", 6.8f, FontStyle.Regular, GraphicsUnit.Point))
            using (var sepPen = new Pen(sepCol, 1.0f))
            {
                // Linha de topo: FPS em destaque quando em jogo
                if (HasFps)
                {
                    int fps = _fpsVal.Value;
                    Color fpsCol = GetFpsColor(fps);
                    DrawVerticalRow(g, fpsCol, "FPS", fps.ToString(), "", fontLbl, fontFps, fontUnit, lblMuted, fpsCol, fpsCol, x0, y, w, VertRowH, false);
                    y += VertRowH + 2f;
                    g.DrawLine(sepPen, x0 + 10f, y, x0 + w - 10f, y);
                    y += 3f;
                }

                // Linha Watts (com raio vetorial no lugar do dot)
                DrawVerticalRow(g, cyanCol, "PWR", _wattsNum, "W", fontLbl, fontVal, fontUnit, lblMuted, valPrimary, cyanCol, x0, y, w, VertRowH, true, emeraldCol);
                y += VertRowH;

                // Linha CPU
                DrawVerticalRow(g, _cpuDot, "CPU", _cpuVal, "", fontLbl, fontVal, fontUnit, lblMuted, _cpuAlert ? _cpuDot : valPrimary, cyanCol, x0, y, w, VertRowH, false);
                y += VertRowH;

                // Linha GPU
                DrawVerticalRow(g, _gpuDot, "GPU", _gpuVal, "", fontLbl, fontVal, fontUnit, lblMuted, _gpuAlert ? _gpuDot : valPrimary, cyanCol, x0, y, w, VertRowH, false);
                y += VertRowH;

                // Linha RAM (se nao estiver em modo Micro)
                if (!_microMode)
                {
                    DrawVerticalRow(g, purpleCol, "RAM", _ramVal, "", fontLbl, fontVal, fontUnit, lblMuted, valPrimary, cyanCol, x0, y, w, VertRowH, false);
                }
            }
        }

        private static void DrawBolt(Graphics g, float x, float y, Color c1, Color c2)
        {
            var bolt = new[]
            {
                new PointF(x + 4.4f, y),
                new PointF(x,        y + 7.4f),
                new PointF(x + 3.2f, y + 7.4f),
                new PointF(x + 2.0f, y + 13.6f),
                new PointF(x + 7.4f, y + 5.8f),
                new PointF(x + 4.1f, y + 5.8f)
            };
            using (var boltBrush = new LinearGradientBrush(new PointF(x, y), new PointF(x + 8f, y + 14f), c1, c2))
            {
                g.FillPolygon(boltBrush, bolt);
            }
        }

        private static void DrawVerticalRow(
            Graphics g, Color dotCol, string label, string value, string unit,
            Font fontLbl, Font fontVal, Font fontUnit,
            Color lblCol, Color valCol, Color unitCol,
            float x, float y, float width, float rowH, bool useBolt, Color boltCol2 = default(Color))
        {
            using (var sf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap })
            {
                float padL = 10f;
                float padR = 10f;

                if (useBolt)
                {
                    // Mini raio 6x10
                    float bx = x + padL - 0.5f;
                    float by = y + (rowH - 10.5f) / 2f;
                    var bolt = new[]
                    {
                        new PointF(bx + 3.3f, by),
                        new PointF(bx,        by + 5.7f),
                        new PointF(bx + 2.4f, by + 5.7f),
                        new PointF(bx + 1.5f, by + 10.5f),
                        new PointF(bx + 5.7f, by + 4.5f),
                        new PointF(bx + 3.1f, by + 4.5f)
                    };
                    using (var bb = new LinearGradientBrush(new PointF(bx, by), new PointF(bx + 6f, by + 11f), dotCol, boltCol2.IsEmpty ? dotCol : boltCol2))
                        g.FillPolygon(bb, bolt);
                }
                else
                {
                    float dotD = 4.4f;
                    float dotX = x + padL;
                    float dotY = y + (rowH - dotD) / 2f + 0.3f;
                    using (var glow = new SolidBrush(Color.FromArgb(55, dotCol.R, dotCol.G, dotCol.B)))
                        g.FillEllipse(glow, dotX - 1.3f, dotY - 1.3f, dotD + 2.6f, dotD + 2.6f);
                    using (var dotB = new SolidBrush(dotCol))
                        g.FillEllipse(dotB, dotX, dotY, dotD, dotD);
                }

                SizeF sLbl = g.MeasureString(label, fontLbl, 60, sf);
                float lx = x + padL + 9f;
                float ly = y + (rowH - sLbl.Height) / 2f + 0.5f;
                using (var bLbl = new SolidBrush(lblCol))
                    g.DrawString(label, fontLbl, bLbl, lx, ly, sf);

                SizeF sVal = g.MeasureString(value, fontVal, 80, sf);
                SizeF sUnit = string.IsNullOrEmpty(unit) ? SizeF.Empty : g.MeasureString(unit, fontUnit, 30, sf);
                float gap = string.IsNullOrEmpty(unit) ? 0f : 1.2f;
                float rightEdge = x + width - padR;
                float vx = rightEdge - sVal.Width - gap - sUnit.Width;
                float vy = y + (rowH - sVal.Height) / 2f;

                using (var bVal = new SolidBrush(valCol))
                    g.DrawString(value, fontVal, bVal, vx, vy, sf);

                if (!string.IsNullOrEmpty(unit))
                {
                    float uy = y + (rowH - sUnit.Height) / 2f + 0.6f;
                    using (var bUnit = new SolidBrush(unitCol))
                        g.DrawString(unit, fontUnit, bUnit, vx + sVal.Width + gap, uy, sf);
                }
            }
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
                float gap = 1.6f;
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

                    bool hiddenPeek = _autoHide && !_isHovered && !_menuOpen;
                    double effOpacity = hiddenPeek
                        ? 1.0
                        : (_isHovered ? Math.Min(0.98, _baseOpacity + 0.12) : _baseOpacity);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing && _hideDelayTimer != null)
            {
                _hideDelayTimer.Stop();
                _hideDelayTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        public void SetGameClickThrough(bool enabled)
        {
            if (_gameClickThrough == enabled) return;
            _gameClickThrough = enabled;
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                if (enabled)
                    ex |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                else
                    ex &= ~WS_EX_TRANSPARENT;
                SetWindowLong(Handle, GWL_EXSTYLE, ex);
                if (enabled && _isHovered)
                {
                    _isHovered = false;
                    RenderLayered();
                }
            }
            catch { }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
                if (_gameClickThrough) cp.ExStyle |= WS_EX_TRANSPARENT;
                return cp;
            }
        }
    }
}
