using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PcPowerMonitor
{
    public class MainForm : Form
    {
        public const string AppVersion = "1.0.0";

        private HardwareMonitorService _monitor;
        private bool _monitorInitializing;
        private readonly EnergyStorage _storage;
        private readonly AppSettings _settings;
        private readonly GameDetector _gameDetector = new GameDetector();
        private readonly FpsMonitor _fpsMonitor = new FpsMonitor();
        private readonly SafePowerTuner _tuner = new SafePowerTuner();
        private readonly LocalApiServer _localApi = new LocalApiServer();
        private bool _inBackgroundEcoMode;

        // Eco Engine Pro Automations (Idle AFK + Thermal Guard)
        private bool _idleEcoActive;
        private bool _thermalGuardActive;
        private string _profileBeforeAuto = "stock";
        private DateTime _highTempSinceUtc = DateTime.MinValue;
        private readonly CultureInfo _ptBr = new CultureInfo("pt-BR");
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private readonly Timer _pollTimer;
        private DateTime _lastTickUtc = DateTime.UtcNow;
        private bool _paused;
        private bool _allowExit;
        private bool _webReady;
        private TelemetrySnapshot _lastSnapshot;
        private GameModeStatus _lastGameMode = new GameModeStatus();
        private bool _overlayAutoShownByGame;
        private UpdateInfo _updateInfo = new UpdateInfo();

        // Alert cooldowns
        private readonly Dictionary<string, DateTime> _alertCooldowns = new Dictionary<string, DateTime>();
        private string _budgetAlertDay = "";

        private WebView2 _webView;
        private Panel _loadingPanel;
        private Label _loadingLabel;
        private OverlayForm _overlayForm;
        private Size _savedNormalSize;
        private bool _wasMinimized;
        private bool _inUserResize;
        private bool _restoringNormalSize;
        private bool? _lastPushedMaximized;

        private NotifyIcon _trayIcon;
        private ToolStripMenuItem _trayPauseItem;
        private ToolStripMenuItem _trayOverlayItem;
        private Icon _baseAppIcon;
        private IntPtr _dynamicTrayIconHandle = IntPtr.Zero;

        // Win32 Frameless Drag, Resize, Native DWM Animations, Rounded Corners & Proper Maximize
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private static double GetSystemIdleSeconds()
        {
            try
            {
                var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (GetLastInputInfo(ref lii))
                {
                    uint diff = unchecked((uint)Environment.TickCount - lii.dwTime);
                    return diff / 1000.0;
                }
            }
            catch { }
            return 0.0;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass, ref PROCESS_POWER_THROTTLING_STATE processInformation, int processInformationSize);

        private const int ProcessPowerThrottling = 4;
        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        private static void SetEcoQos(bool enable)
        {
            try
            {
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = enable ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0
                };
                SetProcessInformation(Process.GetCurrentProcess().Handle, ProcessPowerThrottling, ref state, Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
            }
            catch { }
        }

        private static void TrimMemory()
        {
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCACTIVATE = 0x0086;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;
        private const int SC_MAXIMIZE = 0xF030;
        private const int SC_RESTORE = 0xF120;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int CS_DBLCLKS = 0x0008;
        private const int SM_CXSIZEFRAME = 32;
        private const int SM_CYSIZEFRAME = 33;
        private const int SM_CXPADDEDBORDER = 92;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NCCALCSIZE_PARAMS
        {
            public RECT rgrc0;
            public RECT rgrc1;
            public RECT rgrc2;
            public IntPtr lppos;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
                cp.ClassStyle |= CS_DBLCLKS;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowChrome(_settings != null && _settings.Theme != "light");
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        protected override void SetClientSizeCore(int x, int y)
        {
            // Com WM_NCCALCSIZE removendo a area nao-cliente, Size == ClientSize.
            // Evita que o WinForms some +39px de altura e +16px de largura a cada minimize/restore!
            SetBoundsCore(Left, Top, x, y, BoundsSpecified.Size);
        }

        public MainForm(bool startMinimized)
        {
            _storage = new EnergyStorage();
            _settings = _storage.LoadSettings();
            _baseAppIcon = LoadAppIcon();

            var wa = Screen.PrimaryScreen.WorkingArea;
            int initW = Math.Max(960, Math.Min(1240, wa.Width - 40));
            int initH = Math.Max(640, Math.Min(780, wa.Height - 40));

            Text = "VoltPulse — PC Power, Voltage & Thermal Monitor";
            FormBorderStyle = FormBorderStyle.Sizable;
            Size = new Size(initW, initH);
            _savedNormalSize = Size;
            MinimumSize = new Size(960, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(11, 16, 27);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.5f);
            Icon = _baseAppIcon;
            Padding = Padding.Empty;

            ApplyWindowChrome(_settings.Theme != "light");

            BuildWebHost();
            LayoutWebView();
            SetupTray();

            if (_settings.ShowOverlay)
                SetOverlayVisible(true, false);

            if (_settings.LocalApiEnabled)
                _localApi.Start(_settings.LocalApiPort);

            // Prioridade abaixo do normal para nunca disputar CPU com jogos ou trabalho
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

            if (_settings.AutoEcoOutsideGames || (!string.IsNullOrEmpty(_settings.TuningProfile) && _settings.TuningProfile != "stock"))
                ApplyConfiguredEcoOrCustom("startup");

            _pollTimer = new Timer { Interval = Math.Max(1000, _settings.PollIntervalSeconds * 1000) };
            _pollTimer.Tick += (s, e) => PollAndPush();

            ResizeBegin += (s, e) => { _inUserResize = true; };

            ResizeEnd += (s, e) =>
            {
                _inUserResize = false;
                if (WindowState == FormWindowState.Normal && Width >= MinimumSize.Width && Height >= MinimumSize.Height)
                    _savedNormalSize = Size;
            };

            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    _wasMinimized = true;
                    // Nao redimensiona o WebView2 para 154x22 ao minimizar: evita re-layout e "pulo/scroll" ao restaurar!
                    return;
                }
                if (WindowState == FormWindowState.Normal)
                {
                    _wasMinimized = false;
                }
                LayoutWebView();
                PushWindowState();
            };

            Shown += (s, e) =>
            {
                InitializeWebViewAsync();
                InitializeHardwareAsync();
                if (_settings.AutoCheckUpdates) CheckUpdatesAsync(false);
            };

            if (startMinimized)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
                Load += (s, e) =>
                {
                    Hide();
                    InitializeHardwareAsync();
                    if (_settings.AutoCheckUpdates) CheckUpdatesAsync(false);
                };
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SYSCOMMAND)
            {
                int cmd = m.WParam.ToInt32() & 0xFFF0;
                if ((cmd == SC_MINIMIZE || cmd == SC_MAXIMIZE) && WindowState == FormWindowState.Normal)
                {
                    if (Width >= MinimumSize.Width && Height >= MinimumSize.Height)
                        _savedNormalSize = Size;
                    if (cmd == SC_MINIMIZE) _wasMinimized = true;
                }
                else if (cmd == SC_RESTORE)
                {
                    base.WndProc(ref m);
                    if (WindowState == FormWindowState.Normal && !_restoringNormalSize &&
                        _savedNormalSize.Width >= MinimumSize.Width && _savedNormalSize.Height >= MinimumSize.Height &&
                        Size != _savedNormalSize)
                    {
                        _restoringNormalSize = true;
                        Size = _savedNormalSize;
                        _restoringNormalSize = false;
                    }
                    if (!IsIconic(Handle)) _wasMinimized = false;
                    return;
                }
            }

            // Intercepta o tamanho ANTES de ser aplicado na janela/WebView2 para impedir qualquer frame inflado (+16x39)
            if (m.Msg == WM_WINDOWPOSCHANGING && !_inUserResize &&
                _savedNormalSize.Width >= MinimumSize.Width && _savedNormalSize.Height >= MinimumSize.Height)
            {
                try
                {
                    var wp = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
                    if ((wp.flags & SWP_NOSIZE) == 0 && wp.cx >= MinimumSize.Width && wp.cy >= MinimumSize.Height &&
                        !IsZoomed(Handle) && _wasMinimized)
                    {
                        if (wp.cx != _savedNormalSize.Width || wp.cy != _savedNormalSize.Height)
                        {
                            wp.cx = _savedNormalSize.Width;
                            wp.cy = _savedNormalSize.Height;
                            Marshal.StructureToPtr(wp, m.LParam, false);
                        }
                    }
                }
                catch { }
            }

            // Remove 100% da barra de titulo nativa preservando animacoes DWM (minimizar/maximizar/Aero Snap)
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                if (IsZoomed(Handle))
                {
                    try
                    {
                        var ncp = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(NCCALCSIZE_PARAMS));
                        int pad = GetSystemMetrics(SM_CXPADDEDBORDER);
                        int fx = GetSystemMetrics(SM_CXSIZEFRAME) + pad;
                        int fy = GetSystemMetrics(SM_CYSIZEFRAME) + pad;
                        ncp.rgrc0.Left += fx;
                        ncp.rgrc0.Top += fy;
                        ncp.rgrc0.Right -= fx;
                        ncp.rgrc0.Bottom -= fy;
                        Marshal.StructureToPtr(ncp, m.LParam, false);
                    }
                    catch { }
                }
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == WM_NCACTIVATE)
            {
                // Impede o Windows de desenhar barra/borda nativa ao focar ou desfocar a janela
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                int grip = 6;
                Point pt = PointToClient(new Point((int)(m.LParam.ToInt64() & 0xFFFF), (int)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
                bool left = pt.X <= grip;
                bool right = pt.X >= ClientSize.Width - grip;
                bool top = pt.Y <= grip;
                bool bottom = pt.Y >= ClientSize.Height - grip;

                if (top && left) m.Result = (IntPtr)13;       // HTTOPLEFT
                else if (top && right) m.Result = (IntPtr)14; // HTTOPRIGHT
                else if (bottom && left) m.Result = (IntPtr)16;  // HTBOTTOMLEFT
                else if (bottom && right) m.Result = (IntPtr)17; // HTBOTTOMRIGHT
                else if (left) m.Result = (IntPtr)10;         // HTLEFT
                else if (right) m.Result = (IntPtr)11;        // HTRIGHT
                else if (top) m.Result = (IntPtr)12;          // HTTOP
                else if (bottom) m.Result = (IntPtr)15;       // HTBOTTOM
                return;
            }

            base.WndProc(ref m);
        }

        private void ApplyWindowChrome(bool dark)
        {
            try
            {
                int val = dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref val, sizeof(int));
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                var margins = new MARGINS { Left = 0, Right = 0, Top = 0, Bottom = 1 };
                DwmExtendFrameIntoClientArea(Handle, ref margins);
            }
            catch { }

            BackColor = dark ? Color.FromArgb(11, 16, 27) : Color.FromArgb(226, 232, 240);
        }

        private void BuildWebHost()
        {
            _loadingPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(11, 16, 27)
            };

            _loadingLabel = new Label
            {
                Text = "⚡ Iniciando VoltPulse...",
                Font = new Font("Segoe UI Semibold", 13f),
                ForeColor = Color.FromArgb(148, 163, 184),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            _loadingPanel.Controls.Add(_loadingLabel);

            _webView = new WebView2
            {
                Dock = DockStyle.None,
                Anchor = AnchorStyles.None,
                DefaultBackgroundColor = Color.FromArgb(11, 16, 27),
                Visible = false
            };

            Controls.Add(_webView);
            Controls.Add(_loadingPanel);
        }

        private void LayoutWebView()
        {
            if (_webView == null || _webView.IsDisposed) return;
            if (WindowState == FormWindowState.Minimized) return;

            int cw = ClientSize.Width;
            int ch = ClientSize.Height;
            if (cw < 300 || ch < 200) return;

            int pad = WindowState == FormWindowState.Maximized ? 0 : 3;
            var target = new Rectangle(pad, pad, Math.Max(1, cw - pad * 2), Math.Max(1, ch - pad * 2));
            if (_webView.Bounds != target)
            {
                _webView.Bounds = target;
            }
        }

        private void PushWindowState()
        {
            if (!_webReady || _webView == null || _webView.CoreWebView2 == null) return;
            bool isMax = WindowState == FormWindowState.Maximized;
            if (_lastPushedMaximized.HasValue && _lastPushedMaximized.Value == isMax) return;
            _lastPushedMaximized = isMax;
            try
            {
                var obj = new Dictionary<string, object>
                {
                    { "Type", "windowState" },
                    { "IsMaximized", isMax }
                };
                _webView.CoreWebView2.PostWebMessageAsJson(_json.Serialize(obj));
            }
            catch { }
        }

        private async void InitializeWebViewAsync()
        {
            try
            {
                string userDataDir = Path.Combine(_storage.DataDirectory, "WebView2Data");
                Directory.CreateDirectory(userDataDir);

                var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
                await _webView.EnsureCoreWebView2Async(env);

                var core = _webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string uiDir = Path.Combine(exeDir, "ui");
                if (!Directory.Exists(uiDir))
                {
                    string devUi = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "ui"));
                    if (Directory.Exists(devUi)) uiDir = devUi;
                }

                core.SetVirtualHostNameToFolderMapping(
                    "voltpulse.local",
                    uiDir,
                    CoreWebView2HostResourceAccessKind.Allow);

                core.WebMessageReceived += OnWebMessageReceived;
                core.NavigationCompleted += (s, e) =>
                {
                    LayoutWebView();
                    _webView.Visible = true;
                    _loadingPanel.Visible = false;
                };

                core.Navigate("https://voltpulse.local/index.html");
            }
            catch (Exception ex)
            {
                _loadingLabel.Text =
                    "Não foi possível inicializar o Microsoft Edge WebView2.\n\n" +
                    "Instale o WebView2 Runtime da Microsoft ou verifique as DLLs na pasta.\n\n" +
                    "Detalhe: " + ex.Message;
            }
        }

        private void PushInitProgress(int percent, string message)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<int, string>(PushInitProgress), percent, message); } catch { }
                return;
            }
            if (_webReady && _webView != null && _webView.CoreWebView2 != null)
            {
                try
                {
                    var obj = new Dictionary<string, object>
                    {
                        { "Type", "initProgress" },
                        { "Percent", percent },
                        { "Message", message }
                    };
                    _webView.CoreWebView2.PostWebMessageAsJson(_json.Serialize(obj));
                }
                catch { }
            }
        }

        private void InitializeHardwareAsync()
        {
            if (_monitor != null || _monitorInitializing) return;
            _monitorInitializing = true;

            Task.Run(() =>
            {
                var svc = new HardwareMonitorService((pct, msg) => PushInitProgress(pct, msg));
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        _monitor = svc;
                        _monitorInitializing = false;
                        _lastTickUtc = DateTime.UtcNow;
                        _pollTimer.Start();
                        PollAndPush(true);
                    }));
                }
                catch { }
            });
        }

        private async void CheckUpdatesAsync(bool notifyUpToDate)
        {
            var info = await UpdateChecker.CheckLatestAsync(AppVersion);
            _updateInfo = info;
            if (!IsDisposed)
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        PollAndPush(true);
                        if (notifyUpToDate && !info.UpdateAvailable && _webReady && _webView != null && _webView.CoreWebView2 != null)
                        {
                            var toastMsg = new Dictionary<string, object>
                            {
                                { "Type", "toast" },
                                { "Message", "✓ VoltPulse v" + AppVersion + " já está atualizado!" }
                            };
                            _webView.CoreWebView2.PostWebMessageAsJson(_json.Serialize(toastMsg));
                        }
                    }));
                }
                catch { }
            }
        }

        private async void InstallLatestUpdateAsync()
        {
            if (_updateInfo == null || !_updateInfo.UpdateAvailable) return;
            if (!string.IsNullOrWhiteSpace(_updateInfo.DownloadUrl))
            {
                bool ok = await UpdateChecker.DownloadAndRunInstallerAsync(_updateInfo.DownloadUrl);
                if (ok)
                {
                    ExitApplication();
                    return;
                }
            }
            if (!string.IsNullOrWhiteSpace(_updateInfo.ReleasePageUrl))
            {
                try { Process.Start(new ProcessStartInfo(_updateInfo.ReleasePageUrl) { UseShellExecute = true }); } catch { }
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = e.WebMessageAsJson;
                var msg = _json.Deserialize<Dictionary<string, object>>(raw);
                if (msg == null || !msg.ContainsKey("action")) return;

                string action = Convert.ToString(msg["action"]);
                switch (action)
                {
                    case "ready":
                        _webReady = true;
                        PollAndPush(true);
                        break;

                    case "winDrag":
                        ReleaseCapture();
                        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                        break;

                    case "winMinimize":
                        PostMessage(Handle, WM_SYSCOMMAND, (IntPtr)SC_MINIMIZE, IntPtr.Zero);
                        break;

                    case "winMaximize":
                        PostMessage(Handle, WM_SYSCOMMAND, (IntPtr)(WindowState == FormWindowState.Maximized ? SC_RESTORE : SC_MAXIMIZE), IntPtr.Zero);
                        break;

                    case "winClose":
                        Close();
                        break;

                    case "resetLocale":
                        _settings.ApplySystemLocaleDefaults();
                        _storage.SaveSettings(_settings);
                        PollAndPush(true);
                        break;

                    case "saveSettings":
                        HandleSaveSettings(msg);
                        break;

                    case "togglePause":
                        TogglePause();
                        break;

                    case "toggleOverlay":
                        _overlayAutoShownByGame = false;
                        SetOverlayVisible(!_settings.ShowOverlay, true);
                        PollAndPush(true);
                        break;

                    case "startSession":
                        string sName = msg.ContainsKey("name") ? Convert.ToString(msg["name"]) : "Sessão";
                        _storage.StartSession(sName, false);
                        PollAndPush(true);
                        break;

                    case "stopSession":
                        _storage.StopSession();
                        PollAndPush(true);
                        break;

                    case "applyTuningProfile":
                        string prof = msg.ContainsKey("profile") ? Convert.ToString(msg["profile"]) : "stock";
                        bool autoEco = msg.ContainsKey("autoEco") && Convert.ToBoolean(msg["autoEco"]);
                        _idleEcoActive = false;
                        _thermalGuardActive = false;
                        _settings.TuningProfile = prof;
                        _settings.AutoEcoOutsideGames = autoEco;
                        _tuner.SetAutomationFlags(false, false, "manual");
                        _tuner.ApplyProfile(prof, autoEco, "manual");
                        _storage.SaveSettings(_settings);
                        PollAndPush(true);
                        break;

                    case "applyTuningCustom":
                        int cpuPct = (int)ReadDouble(msg, "cpuMaxPercent", 99, 50, 100);
                        int cpuBoost = (int)ReadDouble(msg, "cpuBoostMode", 3, 0, 3);
                        int cpuEpp = (int)ReadDouble(msg, "cpuEpp", 65, 0, 100);
                        bool passiveCool = ReadBool(msg, "passiveCooling", false);
                        bool pcieAspm = ReadBool(msg, "pcieAspmEco", false);
                        int gpuPct = (int)ReadDouble(msg, "gpuPowerPercent", 85, 65, 100);
                        bool autoEcoC = ReadBool(msg, "autoEco", _settings.AutoEcoOutsideGames);
                        _idleEcoActive = false;
                        _thermalGuardActive = false;
                        _settings.TuningProfile = "custom";
                        _settings.AutoEcoOutsideGames = autoEcoC;
                        _settings.CustomCpuMaxPercent = cpuPct;
                        _settings.CustomCpuBoostMode = cpuBoost;
                        _settings.CustomCpuEpp = cpuEpp;
                        _settings.CustomPassiveCooling = passiveCool;
                        _settings.CustomPcieAspmEco = pcieAspm;
                        _settings.CustomGpuPowerPercent = gpuPct;
                        _tuner.SetAutomationFlags(false, false, "manual");
                        _tuner.ApplyCustom("custom", cpuPct, 5, cpuBoost, cpuEpp, passiveCool, pcieAspm, gpuPct, autoEcoC, "manual");
                        _storage.SaveSettings(_settings);
                        PollAndPush(true);
                        break;

                    case "saveEcoAutomations":
                        _settings.AutoEcoOutsideGames = ReadBool(msg, "autoEcoOutsideGames", _settings.AutoEcoOutsideGames);
                        _settings.AutoIdleEco = ReadBool(msg, "autoIdleEco", _settings.AutoIdleEco);
                        _settings.AutoIdleMinutes = (int)ReadDouble(msg, "autoIdleMinutes", _settings.AutoIdleMinutes, 2, 180);
                        _settings.ThermalGuardEnabled = ReadBool(msg, "thermalGuardEnabled", _settings.ThermalGuardEnabled);
                        _settings.ThermalGuardLimitC = ReadDouble(msg, "thermalGuardLimitC", _settings.ThermalGuardLimitC, 65, 100);
                        if (_settings.AutoEcoOutsideGames && !_lastGameMode.Active && _settings.TuningProfile == "stock")
                        {
                            _settings.TuningProfile = "eco";
                            _tuner.ApplyProfile("eco", true, "game_auto");
                        }
                        _storage.SaveSettings(_settings);
                        PollAndPush(true);
                        break;

                    case "restoreStockTuning":
                        _idleEcoActive = false;
                        _thermalGuardActive = false;
                        _settings.TuningProfile = "stock";
                        _settings.AutoEcoOutsideGames = false;
                        _tuner.RestoreStock();
                        _storage.SaveSettings(_settings);
                        PollAndPush(true);
                        break;

                    case "checkUpdates":
                        CheckUpdatesAsync(true);
                        break;

                    case "installUpdate":
                        InstallLatestUpdateAsync();
                        break;

                    case "openDataDir":
                        _storage.Flush();
                        try { Process.Start("explorer.exe", _storage.DataDirectory); } catch { }
                        break;

                    case "exportCsv":
                        ExportCsvDialog();
                        break;

                    case "clearHistory":
                        _storage.ClearHistory();
                        PollAndPush(true);
                        break;

                    case "restartAdmin":
                        RestartAsAdmin();
                        break;

                    case "installDeps":
                        DependencyManager.EnsureDependenciesOnStartup(false);
                        PollAndPush(true);
                        break;

                    case "applySmartOptimize":
                        HandleSmartOptimize(msg);
                        break;

                    case "toggleLocalApi":
                        _settings.LocalApiEnabled = !_settings.LocalApiEnabled;
                        if (_settings.LocalApiEnabled) _localApi.Start(_settings.LocalApiPort);
                        else _localApi.Stop();
                        _storage.SaveSettings(_settings);
                        PollAndPush(true);
                        break;

                    case "openUrl":
                        if (msg.ContainsKey("url"))
                        {
                            string url = Convert.ToString(msg["url"]);
                            if (!string.IsNullOrEmpty(url) &&
                                (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                 url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
                            {
                                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                            }
                        }
                        break;
                }
            }
            catch { }
        }

        private void HandleSmartOptimize(Dictionary<string, object> msg)
        {
            var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (msg.ContainsKey("items") && msg["items"] is object[])
            {
                foreach (var o in (object[])msg["items"])
                {
                    string s = Convert.ToString(o);
                    if (!string.IsNullOrWhiteSpace(s)) items.Add(s.Trim());
                }
            }
            else if (msg.ContainsKey("items") && msg["items"] is System.Collections.ArrayList)
            {
                foreach (var o in (System.Collections.ArrayList)msg["items"])
                {
                    string s = Convert.ToString(o);
                    if (!string.IsNullOrWhiteSpace(s)) items.Add(s.Trim());
                }
            }

            if (items.Count == 0) return;

            bool wantEcoProfile = items.Contains("eco_profile");
            if (items.Contains("auto_eco_games"))
            {
                _settings.AutoEcoOutsideGames = true;
                wantEcoProfile = true;
            }
            if (items.Contains("idle_afk"))
            {
                _settings.AutoIdleEco = true;
                if (_settings.AutoIdleMinutes < 2) _settings.AutoIdleMinutes = 10;
            }
            if (items.Contains("thermal_guard"))
            {
                _settings.ThermalGuardEnabled = true;
                if (_settings.ThermalGuardLimitC < 65 || _settings.ThermalGuardLimitC > 100)
                    _settings.ThermalGuardLimitC = 85.0;
            }
            if (items.Contains("ultra_low_bg"))
            {
                _settings.UltraLowBackgroundMode = true;
                _settings.MinimizeToTrayOnClose = true;
            }
            if (items.Contains("auto_game"))
            {
                _settings.AutoGameMode = true;
            }
            if (items.Contains("restore_stock_exit"))
            {
                _settings.RestoreStockOnExit = true;
            }
            if (items.Contains("pcie_aspm"))
            {
                _settings.CustomPcieAspmEco = true;
                if (_settings.TuningProfile == "custom")
                    ApplyConfiguredEcoOrCustom("smart_opt");
            }

            if (wantEcoProfile && !_lastGameMode.Active)
            {
                if (_settings.TuningProfile == "stock" || string.IsNullOrEmpty(_settings.TuningProfile))
                    _settings.TuningProfile = "eco";
                _idleEcoActive = false;
                _thermalGuardActive = false;
                _tuner.SetAutomationFlags(false, false, "manual");
                ApplyConfiguredEcoOrCustom("manual");
            }

            _storage.SaveSettings(_settings);
            PollAndPush(true);
        }

        private void HandleSaveSettings(Dictionary<string, object> msg)
        {
            if (!msg.ContainsKey("settings")) return;
            var sDict = msg["settings"] as Dictionary<string, object>;
            if (sDict == null) return;

            _settings.TariffPerKwh = ReadDouble(sDict, "TariffPerKwh", _settings.TariffPerKwh, 0.001, 5000.0);
            _settings.CurrencySymbol = ReadString(sDict, "CurrencySymbol", _settings.CurrencySymbol);
            _settings.CurrencyCode = ReadString(sDict, "CurrencyCode", _settings.CurrencyCode);
            if (string.IsNullOrWhiteSpace(_settings.CurrencySymbol))
            {
                var def = new AppSettings();
                _settings.CurrencySymbol = def.CurrencySymbol;
                _settings.CurrencyCode = def.CurrencyCode;
            }
            _settings.PsuEfficiencyPercent = ReadDouble(sDict, "PsuEfficiencyPercent", _settings.PsuEfficiencyPercent, 50.0, 99.0);
            _settings.BaseSystemWatts = ReadDouble(sDict, "BaseSystemWatts", _settings.BaseSystemWatts, 0.0, 500.0);
            _settings.PollIntervalSeconds = (int)ReadDouble(sDict, "PollIntervalSeconds", _settings.PollIntervalSeconds, 1.0, 60.0);
            _settings.TempWarningC = ReadDouble(sDict, "TempWarningC", _settings.TempWarningC, 40.0, 100.0);
            _settings.TempCriticalC = ReadDouble(sDict, "TempCriticalC", _settings.TempCriticalC, _settings.TempWarningC + 5.0, 110.0);
            _settings.MinimizeToTrayOnClose = ReadBool(sDict, "MinimizeToTrayOnClose", _settings.MinimizeToTrayOnClose);
            _settings.StartWithWindows = ReadBool(sDict, "StartWithWindows", _settings.StartWithWindows);
            _settings.Theme = AppSettings.NormalizeTheme(ReadString(sDict, "Theme", _settings.Theme));
            _settings.StartView = AppSettings.NormalizeStartView(ReadString(sDict, "StartView", _settings.StartView));
            _settings.Language = AppSettings.NormalizeLanguage(ReadString(sDict, "Language", _settings.Language));
            _settings.EnableAlerts = ReadBool(sDict, "EnableAlerts", _settings.EnableAlerts);
            _settings.MonthlyBudgetAlert = ReadDouble(sDict, "MonthlyBudgetAlert", _settings.MonthlyBudgetAlert, 0.0, 1000000.0);
            bool wantOverlay = ReadBool(sDict, "ShowOverlay", _settings.ShowOverlay);

            // v4.0 settings
            _settings.AutoGameMode = ReadBool(sDict, "AutoGameMode", _settings.AutoGameMode);
            _settings.AutoOverlayInGame = ReadBool(sDict, "AutoOverlayInGame", _settings.AutoOverlayInGame);
            _settings.QuietStart = ReadBool(sDict, "QuietStart", _settings.QuietStart);
            _settings.TrayIconMode = AppSettings.NormalizeTrayMode(ReadString(sDict, "TrayIconMode", _settings.TrayIconMode));
            _settings.LocalApiEnabled = ReadBool(sDict, "LocalApiEnabled", _settings.LocalApiEnabled);
            _settings.LocalApiPort = (int)ReadDouble(sDict, "LocalApiPort", _settings.LocalApiPort, 1024, 65535);
            _settings.AutoCheckUpdates = ReadBool(sDict, "AutoCheckUpdates", _settings.AutoCheckUpdates);
            _settings.RestoreStockOnExit = ReadBool(sDict, "RestoreStockOnExit", _settings.RestoreStockOnExit);
            _settings.UltraLowBackgroundMode = ReadBool(sDict, "UltraLowBackgroundMode", _settings.UltraLowBackgroundMode);

            SetOverlayVisible(wantOverlay, false);

            if (_settings.LocalApiEnabled)
                _localApi.Start(_settings.LocalApiPort);
            else
                _localApi.Stop();

            _storage.SaveSettings(_settings);
            _pollTimer.Interval = Math.Max(1000, _settings.PollIntervalSeconds * 1000);
            ApplyWindowChrome(_settings.Theme != "light");

            PollAndPush(true);
        }

        private void SetOverlayVisible(bool show, bool persist)
        {
            _settings.ShowOverlay = show;
            if (show)
            {
                if (_overlayForm == null || _overlayForm.IsDisposed)
                {
                    _overlayForm = new OverlayForm(
                        _settings,
                        RestoreFromTray,
                        () => { _overlayAutoShownByGame = false; SetOverlayVisible(false, true); PollAndPush(true); },
                        (x, y) =>
                        {
                            _settings.OverlayX = x;
                            _settings.OverlayY = y;
                            _storage.SaveSettings(_settings);
                        },
                        (vert, micro, autoHide, opacity) =>
                        {
                            _settings.OverlayVertical = vert;
                            _settings.OverlayMicroMode = micro;
                            _settings.OverlayAutoHide = autoHide;
                            _settings.OverlayOpacity = opacity;
                            _storage.SaveSettings(_settings);
                        });
                }
                if (!_overlayForm.Visible) _overlayForm.Show();
                if (_lastSnapshot != null) _overlayForm.UpdateMetrics(_lastSnapshot, _settings);
            }
            else
            {
                _fpsMonitor.Stop();
                if (_overlayForm != null && !_overlayForm.IsDisposed)
                {
                    _overlayForm.Hide();
                }
            }

            if (_trayOverlayItem != null)
                _trayOverlayItem.Checked = show;

            if (persist)
                _storage.SaveSettings(_settings);
        }

        private static double ReadDouble(Dictionary<string, object> d, string key, double fallback, double min, double max)
        {
            if (!d.ContainsKey(key) || d[key] == null) return fallback;
            double v;
            if (double.TryParse(Convert.ToString(d[key], CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return Math.Max(min, Math.Min(max, v));
            return fallback;
        }

        private static bool ReadBool(Dictionary<string, object> d, string key, bool fallback)
        {
            if (!d.ContainsKey(key) || d[key] == null) return fallback;
            bool b;
            if (bool.TryParse(Convert.ToString(d[key]), out b)) return b;
            return fallback;
        }

        private static string ReadString(Dictionary<string, object> d, string key, string fallback)
        {
            if (!d.ContainsKey(key) || d[key] == null) return fallback;
            return Convert.ToString(d[key]).Trim();
        }

        private void ExportCsvDialog()
        {
            _storage.Flush();
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Exportar histórico de energia (CSV)";
                dlg.Filter = "Arquivo CSV (*.csv)|*.csv";
                dlg.FileName = "voltpulse_energia_" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        var summary = _storage.GetSummary(_settings.TariffPerKwh);
                        var sb = new StringBuilder();
                        sb.AppendLine("Data;HorasMonitoradas;MediaWatts;PicoWatts;ConsumoKwh;CustoEstimado;Moeda");
                        foreach (var r in summary.AllDays)
                        {
                            sb.AppendFormat(
                                CultureInfo.InvariantCulture,
                                "{0};{1:F2};{2:F1};{3:F1};{4:F4};{5:F2};{6}\r\n",
                                r.Date, r.ActiveSeconds / 3600.0, r.AverageWatts, r.PeakWatts,
                                r.EnergyKwh, r.EnergyKwh * _settings.TariffPerKwh, _settings.CurrencySymbol);
                        }
                        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Erro ao exportar CSV: " + ex.Message, "VoltPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void PollAndPush(bool forcePush = false)
        {
            if (_monitor == null) return;
            if (_paused && !forcePush) return;

            bool uiVisible = Visible && WindowState != FormWindowState.Minimized;
            bool overlayVisible = _overlayForm != null && !_overlayForm.IsDisposed && _overlayForm.Visible;
            bool bgUltraLow = !uiVisible && _settings.UltraLowBackgroundMode;

            // Ajusta intervalo adaptativo: 1s em jogo c/ Mini Widget, 5s na bandeja sem widget, ou intervalo configurado
            int targetInterval = (overlayVisible && _lastGameMode.Active)
                ? 1000
                : ((bgUltraLow && !overlayVisible)
                    ? Math.Max(5000, _settings.PollIntervalSeconds * 1000)
                    : Math.Max(1000, _settings.PollIntervalSeconds * 1000));
            if (_pollTimer.Interval != targetInterval)
                _pollTimer.Interval = targetInterval;

            DateTime nowUtc = DateTime.UtcNow;
            double elapsed = (nowUtc - _lastTickUtc).TotalSeconds;
            _lastTickUtc = nowUtc;

            var snap = _monitor.Capture(_settings, _paused, bgUltraLow);
            _lastSnapshot = snap;

            // Modo Jogo Automatico + Auto Eco + Idle AFK + Thermal Guard
            HandleAutoGameMode(snap);
            _tuner.UpdateStockBaseline(snap.EstimatedWallWatts);
            HandleEcoAutomations(snap);

            // Re-checa visibilidade do overlay (pode ter aberto automaticamente via AutoOverlayInGame)
            overlayVisible = _overlayForm != null && !_overlayForm.IsDisposed && _overlayForm.Visible;

            // Contador de FPS 100% seguro via ETW oficial do Windows (ativo APENAS durante jogo + Mini Widget visivel!)
            if (overlayVisible && _lastGameMode.Active && _lastGameMode.ProcessId > 4)
            {
                _fpsMonitor.EnsureTracking(_lastGameMode.ProcessId);
                snap.CurrentFps = _fpsMonitor.GetCurrentFps();
            }
            else
            {
                _fpsMonitor.Stop();
                snap.CurrentFps = null;
            }

            if (!_paused && elapsed > 0.2)
            {
                _storage.RecordSample(DateTime.Now, snap.EstimatedWallWatts, elapsed, snap.CpuTempC, snap.GpuTempC);
            }

            var summary = _storage.GetSummary(_settings.TariffPerKwh);

            // Atualiza Overlay se visivel
            if (_overlayForm != null && !_overlayForm.IsDisposed && _overlayForm.Visible)
            {
                _overlayForm.UpdateMetrics(snap, _settings);
            }

            // Atualiza icone dinamico na bandeja (logo / cpu_temp / watts)
            UpdateDynamicTrayIcon(snap);

            // Verifica alertas nativos
            if (_settings.EnableAlerts && !_paused)
            {
                CheckAndNotifyAlerts(snap, summary, _lastGameMode.QuietActive);
            }

            // Tooltip da bandeja
            string tempPart = snap.CpuTempC.HasValue ? string.Format(_ptBr, " | CPU {0:0}°C", snap.CpuTempC.Value)
                : (snap.GpuTempC.HasValue ? string.Format(_ptBr, " | GPU {0:0}°C", snap.GpuTempC.Value) : "");
            string tip = string.Format(_ptBr, "VoltPulse: {0:0}W{1} | Hoje {2}{3:0.00}",
                snap.EstimatedWallWatts, tempPart, _settings.CurrencySymbol, summary.TodayCost);
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (_trayIcon != null) _trayIcon.Text = tip;

            // OTIMIZACAO: nao serializa nem envia JSON se a janela estiver oculta na bandeja e API local desligada!
            if (!uiVisible && !_localApi.IsRunning && !forcePush)
                return;

            var session = _storage.GetSessionSnapshot();
            double memMb = 0;
            try { memMb = Math.Round(Process.GetCurrentProcess().WorkingSet64 / 1048576.0, 1); } catch { }

            var payload = new DashboardPayload
            {
                Type = "telemetry",
                Telemetry = snap,
                Summary = summary,
                Session = session,
                GameMode = _lastGameMode,
                Tuning = _tuner.GetStatus(snap.EstimatedWallWatts),
                Update = _updateInfo,
                Settings = _settings,
                AppMemoryMb = memMb,
                IsMaximized = WindowState == FormWindowState.Maximized,
                LocalApiRunning = _localApi.IsRunning,
                DataDirectory = _storage.DataDirectory,
                AppVersion = AppVersion
            };

            string jsonStr = null;
            try { jsonStr = _json.Serialize(payload); } catch { }

            if (!string.IsNullOrEmpty(jsonStr))
            {
                if (_localApi.IsRunning)
                    _localApi.UpdatePayloadJson(jsonStr);

                if (uiVisible && _webReady && _webView != null && _webView.CoreWebView2 != null)
                {
                    try { _webView.CoreWebView2.PostWebMessageAsJson(jsonStr); } catch { }
                }
            }
        }

        private void HandleAutoGameMode(TelemetrySnapshot snap)
        {
            var game = _gameDetector.Check(snap.GpuLoadPercent, _settings.AutoGameMode);
            bool wasActive = _lastGameMode.Active;
            _lastGameMode = game;

            if (game.Active && !wasActive)
            {
                var curSess = _storage.GetSessionSnapshot();
                if (!curSess.Active)
                {
                    _storage.StartSession("🎮 " + game.GameName, true);
                }

                // Se Auto Eco fora de jogos estiver ativo, libera performance Stock ao abrir jogo!
                if (_settings.AutoEcoOutsideGames)
                {
                    _tuner.ApplyProfile("stock", true);
                }

                if (_settings.AutoOverlayInGame && !_settings.ShowOverlay)
                {
                    _overlayAutoShownByGame = true;
                    SetOverlayVisible(true, false);
                }
            }
            else if (!game.Active && wasActive)
            {
                var curSess = _storage.GetSessionSnapshot();
                if (curSess.Active && curSess.AutoStarted)
                {
                    _storage.StopSession();
                }

                // Ao fechar o jogo, volta automaticamente para o perfil Eco ou ajuste fino personalizado!
                if (_settings.AutoEcoOutsideGames)
                {
                    ApplyConfiguredEcoOrCustom("game_auto");
                }

                if (_overlayAutoShownByGame)
                {
                    _overlayAutoShownByGame = false;
                    SetOverlayVisible(false, false);
                }
            }
        }

        private void ApplyConfiguredEcoOrCustom(string reason)
        {
            if (string.Equals(_settings.TuningProfile, "custom", StringComparison.OrdinalIgnoreCase))
            {
                _tuner.ApplyCustom(
                    "custom",
                    _settings.CustomCpuMaxPercent > 0 ? _settings.CustomCpuMaxPercent : 99,
                    5,
                    _settings.CustomCpuBoostMode,
                    _settings.CustomCpuEpp,
                    _settings.CustomPassiveCooling,
                    _settings.CustomPcieAspmEco,
                    _settings.CustomGpuPowerPercent > 0 ? _settings.CustomGpuPowerPercent : 85,
                    _settings.AutoEcoOutsideGames,
                    reason);
            }
            else if (string.Equals(_settings.TuningProfile, "ultra_eco", StringComparison.OrdinalIgnoreCase))
            {
                _tuner.ApplyProfile("ultra_eco", _settings.AutoEcoOutsideGames, reason);
            }
            else
            {
                _tuner.ApplyProfile("eco", _settings.AutoEcoOutsideGames, reason);
            }
        }

        private void HandleEcoAutomations(TelemetrySnapshot snap)
        {
            // 1. Protecao Termica Automatica (Thermal Guard)
            if (_settings.ThermalGuardEnabled)
            {
                double maxTemp = Math.Max(snap.CpuTempC ?? 0.0, snap.GpuTempC ?? 0.0);
                if (!_thermalGuardActive)
                {
                    if (maxTemp >= _settings.ThermalGuardLimitC)
                    {
                        if (_highTempSinceUtc == DateTime.MinValue) _highTempSinceUtc = DateTime.UtcNow;
                        if ((DateTime.UtcNow - _highTempSinceUtc).TotalSeconds >= 15.0)
                        {
                            _thermalGuardActive = true;
                            _profileBeforeAuto = _tuner.GetStatus().ActiveProfile;
                            _tuner.SetAutomationFlags(_idleEcoActive, true, "thermal_guard");
                            _tuner.ApplyProfile("eco", _settings.AutoEcoOutsideGames, "thermal_guard");
                        }
                    }
                    else
                    {
                        _highTempSinceUtc = DateTime.MinValue;
                    }
                }
                else
                {
                    if (maxTemp > 0 && maxTemp <= _settings.ThermalGuardLimitC - 12.0)
                    {
                        _thermalGuardActive = false;
                        _highTempSinceUtc = DateTime.MinValue;
                        _tuner.SetAutomationFlags(_idleEcoActive, false, "manual");
                        _tuner.ApplyProfile(_profileBeforeAuto, _settings.AutoEcoOutsideGames, "manual");
                    }
                }
            }

            // 2. Modo Idle Automatico (AFK) — entra em Ultra Silencioso apos X min sem mexer no mouse/teclado
            if (_settings.AutoIdleEco && !_lastGameMode.Active && !_thermalGuardActive)
            {
                double idleSec = GetSystemIdleSeconds();
                double thresholdSec = Math.Max(120, _settings.AutoIdleMinutes * 60);

                if (!_idleEcoActive && idleSec >= thresholdSec)
                {
                    _idleEcoActive = true;
                    _profileBeforeAuto = _tuner.GetStatus().ActiveProfile;
                    _tuner.SetAutomationFlags(true, _thermalGuardActive, "idle_afk");
                    _tuner.ApplyProfile("ultra_eco", _settings.AutoEcoOutsideGames, "idle_afk");
                }
                else if (_idleEcoActive && idleSec < 8.0)
                {
                    _idleEcoActive = false;
                    _tuner.SetAutomationFlags(false, _thermalGuardActive, "manual");
                    _tuner.ApplyProfile(_profileBeforeAuto, _settings.AutoEcoOutsideGames, "manual");
                }
            }
            else if (_idleEcoActive && (!_settings.AutoIdleEco || _lastGameMode.Active))
            {
                _idleEcoActive = false;
                _tuner.SetAutomationFlags(false, _thermalGuardActive, "manual");
            }
        }

        private void UpdateDynamicTrayIcon(TelemetrySnapshot snap)
        {
            if (_trayIcon == null) return;

            if (_settings.TrayIconMode == "logo")
            {
                if (_trayIcon.Icon != _baseAppIcon)
                    _trayIcon.Icon = _baseAppIcon;
                return;
            }

            string text = "--";
            Color bg = Color.FromArgb(15, 23, 42);
            Color fg = Color.FromArgb(56, 189, 248);

            if (_settings.TrayIconMode == "cpu_temp")
            {
                double? t = snap.CpuTempC ?? snap.GpuTempC;
                if (t.HasValue)
                {
                    text = ((int)Math.Round(t.Value)).ToString();
                    fg = t.Value >= _settings.TempCriticalC ? Color.FromArgb(248, 113, 113)
                       : (t.Value >= _settings.TempWarningC ? Color.FromArgb(251, 191, 36) : Color.FromArgb(52, 211, 153));
                }
            }
            else if (_settings.TrayIconMode == "watts")
            {
                int w = (int)Math.Round(snap.EstimatedWallWatts);
                text = w >= 1000 ? "1k" : w.ToString();
                fg = w >= 350 ? Color.FromArgb(251, 191, 36) : Color.FromArgb(56, 189, 248);
            }

            try
            {
                using (var bmp = new Bitmap(32, 32))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (var b = new SolidBrush(bg))
                        g.FillRectangle(b, 0, 0, 32, 32);
                    using (var pen = new Pen(fg, 2f))
                        g.DrawRectangle(pen, 1, 1, 29, 29);

                    float fSize = text.Length >= 3 ? 11.5f : 15f;
                    using (var font = new Font("Segoe UI", fSize, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var brush = new SolidBrush(fg))
                    {
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString(text, font, brush, new RectangleF(0, 1, 32, 31), sf);
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    var newIcon = (Icon)Icon.FromHandle(hIcon).Clone();
                    DestroyIcon(hIcon);

                    var old = _trayIcon.Icon;
                    _trayIcon.Icon = newIcon;
                    if (old != null && old != _baseAppIcon) old.Dispose();
                }
            }
            catch { }
        }

        private void CheckAndNotifyAlerts(TelemetrySnapshot snap, EnergySummary summary, bool quietGameMode)
        {
            if (_trayIcon == null) return;
            bool isEn = _settings.Language != "pt-BR";

            if (snap.CpuTempC.HasValue && snap.CpuTempC.Value >= _settings.TempCriticalC)
            {
                TriggerCooldownAlert(
                    "cpu_temp",
                    TimeSpan.FromMinutes(5),
                    isEn ? "VoltPulse — High CPU Temperature" : "VoltPulse — Temperatura Crítica da CPU",
                    isEn
                        ? string.Format("CPU reached {0:0.0} °C (limit: {1:0} °C).", snap.CpuTempC.Value, _settings.TempCriticalC)
                        : string.Format("A CPU atingiu {0:0.0} °C (limite: {1:0} °C).", snap.CpuTempC.Value, _settings.TempCriticalC),
                    ToolTipIcon.Warning);
            }

            if (snap.GpuTempC.HasValue && snap.GpuTempC.Value >= _settings.TempCriticalC)
            {
                TriggerCooldownAlert(
                    "gpu_temp",
                    TimeSpan.FromMinutes(5),
                    isEn ? "VoltPulse — High GPU Temperature" : "VoltPulse — Temperatura Crítica da GPU",
                    isEn
                        ? string.Format("GPU reached {0:0.0} °C (limit: {1:0} °C).", snap.GpuTempC.Value, _settings.TempCriticalC)
                        : string.Format("A GPU atingiu {0:0.0} °C (limite: {1:0} °C).", snap.GpuTempC.Value, _settings.TempCriticalC),
                    ToolTipIcon.Warning);
            }

            if (snap.AtxWarnings != null && snap.AtxWarnings.Count > 0)
            {
                TriggerCooldownAlert(
                    "atx_volt",
                    TimeSpan.FromMinutes(10),
                    isEn ? "VoltPulse — ATX Voltage Out of Range" : "VoltPulse — Voltagem ATX Fora da Faixa",
                    snap.AtxWarnings[0],
                    ToolTipIcon.Warning);
            }

            // Meta mensal fica silenciada enquanto joga (Modo Quieto)
            if (!quietGameMode && _settings.MonthlyBudgetAlert > 0 && summary.MonthCost >= _settings.MonthlyBudgetAlert)
            {
                string todayKey = DateTime.Today.ToString("yyyy-MM-dd");
                if (_budgetAlertDay != todayKey)
                {
                    _budgetAlertDay = todayKey;
                    _trayIcon.ShowBalloonTip(
                        5000,
                        isEn ? "VoltPulse — Monthly Budget Reached" : "VoltPulse — Meta Mensal Atingida",
                        isEn
                            ? string.Format("Monthly PC energy reached {0} {1:0.00} (budget: {0} {2:0.00}).", _settings.CurrencySymbol, summary.MonthCost, _settings.MonthlyBudgetAlert)
                            : string.Format("O gasto mensal do PC chegou a {0} {1:0.00} (meta: {0} {2:0.00}).", _settings.CurrencySymbol, summary.MonthCost, _settings.MonthlyBudgetAlert),
                        ToolTipIcon.Info);
                }
            }
        }

        private void TriggerCooldownAlert(string key, TimeSpan cooldown, string title, string message, ToolTipIcon icon)
        {
            DateTime last;
            if (_alertCooldowns.TryGetValue(key, out last) && (DateTime.UtcNow - last) < cooldown)
                return;
            _alertCooldowns[key] = DateTime.UtcNow;
            try { _trayIcon.ShowBalloonTip(4500, title, message, icon); } catch { }
        }

        private void SetupTray()
        {
            var menu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("Abrir VoltPulse", null, (s, e) => RestoreFromTray());
            _trayOverlayItem = new ToolStripMenuItem("Mini Widget Flutuante (Overlay)", null, (s, e) =>
            {
                _overlayAutoShownByGame = false;
                SetOverlayVisible(!_settings.ShowOverlay, true);
                PollAndPush(true);
            }) { Checked = _settings.ShowOverlay };
            _trayPauseItem = new ToolStripMenuItem("Pausar monitoramento", null, (s, e) => TogglePause());
            var exitItem = new ToolStripMenuItem("Sair completamente", null, (s, e) => ExitApplication());

            menu.Items.Add(openItem);
            menu.Items.Add(_trayOverlayItem);
            menu.Items.Add(_trayPauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                Icon = _baseAppIcon,
                Text = "VoltPulse — Monitor de Energia & Hardware",
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private void TogglePause()
        {
            _paused = !_paused;
            _trayPauseItem.Text = _paused ? "Retomar monitoramento" : "Pausar monitoramento";
            _lastTickUtc = DateTime.UtcNow;
            PollAndPush(true);
        }

        private async void EnterBackgroundEcoMode()
        {
            if (_inBackgroundEcoMode || !_settings.UltraLowBackgroundMode) return;
            _inBackgroundEcoMode = true;
            SetEcoQos(true);
            try
            {
                if (_webView != null && _webView.CoreWebView2 != null)
                {
                    await _webView.CoreWebView2.TrySuspendAsync();
                }
            }
            catch { }
            TrimMemory();
        }

        private void LeaveBackgroundEcoMode()
        {
            if (!_inBackgroundEcoMode) return;
            _inBackgroundEcoMode = false;
            SetEcoQos(false);
        }

        private void RestoreFromTray()
        {
            LeaveBackgroundEcoMode();
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            if (_savedNormalSize.Width >= MinimumSize.Width && _savedNormalSize.Height >= MinimumSize.Height && Size != _savedNormalSize)
            {
                _restoringNormalSize = true;
                Size = _savedNormalSize;
                _restoringNormalSize = false;
            }
            LayoutWebView();
            PushWindowState();
            Activate();
            if (_webView == null || _webView.CoreWebView2 == null)
                InitializeWebViewAsync();
            else
                PollAndPush(true);
        }

        private void ExitApplication()
        {
            _allowExit = true;
            _pollTimer.Stop();
            _localApi.Stop();
            if (_settings.RestoreStockOnExit && _settings.TuningProfile != "stock")
            {
                try { _tuner.RestoreStock(); } catch { }
            }
            _storage.Flush();
            if (_overlayForm != null && !_overlayForm.IsDisposed)
            {
                _overlayForm.Close();
                _overlayForm.Dispose();
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            if (_monitor != null) _monitor.Dispose();
            _fpsMonitor.Dispose();
            _tuner.Dispose();
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowExit && _settings.MinimizeToTrayOnClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                EnterBackgroundEcoMode();
                if (!_settings.QuietStart)
                {
                    _trayIcon.ShowBalloonTip(2000, "VoltPulse", "Continuando o monitoramento na bandeja do sistema.", ToolTipIcon.Info);
                }
                return;
            }

            _localApi.Stop();
            if (_settings.RestoreStockOnExit && _settings.TuningProfile != "stock")
            {
                try { _tuner.RestoreStock(); } catch { }
            }
            _storage.Flush();
            _fpsMonitor.Dispose();
            if (_monitor != null) _monitor.Dispose();
            if (_overlayForm != null && !_overlayForm.IsDisposed) _overlayForm.Close();
            if (_trayIcon != null) _trayIcon.Visible = false;
            base.OnFormClosing(e);
        }

        private void RestartAsAdmin()
        {
            try
            {
                _storage.Flush();
                var psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                ExitApplication();
            }
            catch { }
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                var associated = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (associated != null) return associated;
            }
            catch { }

            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bg = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(15, 23, 42), Color.FromArgb(30, 58, 138), 45f))
                    g.FillEllipse(bg, 1, 1, 30, 30);
                using (var ring = new Pen(Color.FromArgb(56, 189, 248), 2f))
                    g.DrawEllipse(ring, 2, 2, 28, 28);

                var bolt = new[]
                {
                    new PointF(18, 5),
                    new PointF(10, 17),
                    new PointF(16, 17),
                    new PointF(14, 27),
                    new PointF(22, 15),
                    new PointF(16, 15)
                };
                using (var boltBrush = new SolidBrush(Color.FromArgb(52, 211, 153)))
                    g.FillPolygon(boltBrush, bolt);

                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
