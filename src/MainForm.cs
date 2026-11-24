using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PcPowerMonitor
{
    public class MainForm : Form
    {
        public const string AppVersion = "3.0.0";

        private readonly HardwareMonitorService _monitor;
        private readonly EnergyStorage _storage;
        private readonly AppSettings _settings;
        private readonly CultureInfo _ptBr = new CultureInfo("pt-BR");
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private readonly Timer _pollTimer;
        private DateTime _lastTickUtc = DateTime.UtcNow;
        private bool _paused;
        private bool _allowExit;
        private bool _webReady;
        private TelemetrySnapshot _lastSnapshot;

        // Alert cooldowns
        private readonly Dictionary<string, DateTime> _alertCooldowns = new Dictionary<string, DateTime>();
        private string _budgetAlertDay = "";

        private WebView2 _webView;
        private Panel _loadingPanel;
        private Label _loadingLabel;
        private OverlayForm _overlayForm;

        private NotifyIcon _trayIcon;
        private ToolStripMenuItem _trayPauseItem;
        private ToolStripMenuItem _trayOverlayItem;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public MainForm(bool startMinimized)
        {
            _storage = new EnergyStorage();
            _settings = _storage.LoadSettings();
            _monitor = new HardwareMonitorService();

            Text = "VoltPulse — PC Power, Voltage & Thermal Monitor";
            Size = new Size(1260, 820);
            MinimumSize = new Size(980, 660);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(11, 16, 27);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.5f);
            Icon = LoadAppIcon();

            ApplyTitleBarTheme(_settings.Theme != "light");

            BuildWebHost();
            SetupTray();

            if (_settings.ShowOverlay)
                SetOverlayVisible(true, false);

            _pollTimer = new Timer { Interval = Math.Max(1000, _settings.PollIntervalSeconds * 1000) };
            _pollTimer.Tick += (s, e) => PollAndPush();
            _pollTimer.Start();

            Shown += (s, e) => InitializeWebViewAsync();

            if (startMinimized)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
                Load += (s, e) => Hide();
            }
        }

        private void ApplyTitleBarTheme(bool dark)
        {
            try
            {
                int val = dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref val, sizeof(int));
            }
            catch { }
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
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(11, 16, 27),
                Visible = false
            };

            Controls.Add(_webView);
            Controls.Add(_loadingPanel);
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

                    case "saveSettings":
                        HandleSaveSettings(msg);
                        break;

                    case "togglePause":
                        TogglePause();
                        break;

                    case "toggleOverlay":
                        SetOverlayVisible(!_settings.ShowOverlay, true);
                        PollAndPush(true);
                        break;

                    case "startSession":
                        string sName = msg.ContainsKey("name") ? Convert.ToString(msg["name"]) : "Sessão";
                        _storage.StartSession(sName);
                        PollAndPush(true);
                        break;

                    case "stopSession":
                        _storage.StopSession();
                        PollAndPush(true);
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

        private void HandleSaveSettings(Dictionary<string, object> msg)
        {
            if (!msg.ContainsKey("settings")) return;
            var sDict = msg["settings"] as Dictionary<string, object>;
            if (sDict == null) return;

            _settings.TariffPerKwh = ReadDouble(sDict, "TariffPerKwh", _settings.TariffPerKwh, 0.01, 50.0);
            _settings.CurrencySymbol = ReadString(sDict, "CurrencySymbol", _settings.CurrencySymbol);
            if (string.IsNullOrWhiteSpace(_settings.CurrencySymbol)) _settings.CurrencySymbol = "R$";
            _settings.PsuEfficiencyPercent = ReadDouble(sDict, "PsuEfficiencyPercent", _settings.PsuEfficiencyPercent, 50.0, 99.0);
            _settings.BaseSystemWatts = ReadDouble(sDict, "BaseSystemWatts", _settings.BaseSystemWatts, 0.0, 500.0);
            _settings.PollIntervalSeconds = (int)ReadDouble(sDict, "PollIntervalSeconds", _settings.PollIntervalSeconds, 1.0, 60.0);
            _settings.TempWarningC = ReadDouble(sDict, "TempWarningC", _settings.TempWarningC, 40.0, 100.0);
            _settings.TempCriticalC = ReadDouble(sDict, "TempCriticalC", _settings.TempCriticalC, _settings.TempWarningC + 5.0, 110.0);
            _settings.MinimizeToTrayOnClose = ReadBool(sDict, "MinimizeToTrayOnClose", _settings.MinimizeToTrayOnClose);
            _settings.StartWithWindows = ReadBool(sDict, "StartWithWindows", _settings.StartWithWindows);
            _settings.Theme = ReadString(sDict, "Theme", _settings.Theme) == "light" ? "light" : "dark";
            string lang = ReadString(sDict, "Language", _settings.Language);
            _settings.Language = (lang == "en-US") ? "en-US" : "pt-BR";
            _settings.EnableAlerts = ReadBool(sDict, "EnableAlerts", _settings.EnableAlerts);
            _settings.MonthlyBudgetAlert = ReadDouble(sDict, "MonthlyBudgetAlert", _settings.MonthlyBudgetAlert, 0.0, 100000.0);
            bool wantOverlay = ReadBool(sDict, "ShowOverlay", _settings.ShowOverlay);

            SetOverlayVisible(wantOverlay, false);

            _storage.SaveSettings(_settings);
            _pollTimer.Interval = Math.Max(1000, _settings.PollIntervalSeconds * 1000);
            ApplyTitleBarTheme(_settings.Theme != "light");

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
                        _settings.OverlayX,
                        _settings.OverlayY,
                        RestoreFromTray,
                        () => { SetOverlayVisible(false, true); PollAndPush(true); },
                        (x, y) =>
                        {
                            _settings.OverlayX = x;
                            _settings.OverlayY = y;
                            _storage.SaveSettings(_settings);
                        });
                }
                if (!_overlayForm.Visible) _overlayForm.Show();
                if (_lastSnapshot != null) _overlayForm.UpdateMetrics(_lastSnapshot, _settings);
            }
            else
            {
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
            if (_paused && !forcePush) return;

            DateTime nowUtc = DateTime.UtcNow;
            double elapsed = (nowUtc - _lastTickUtc).TotalSeconds;
            _lastTickUtc = nowUtc;

            var snap = _monitor.Capture(_settings, _paused);
            _lastSnapshot = snap;

            if (!_paused && elapsed > 0.2)
            {
                _storage.RecordSample(DateTime.Now, snap.EstimatedWallWatts, elapsed, snap.CpuTempC, snap.GpuTempC);
            }

            var summary = _storage.GetSummary(_settings.TariffPerKwh);
            var session = _storage.GetSessionSnapshot();

            // Atualiza Overlay se visível
            if (_overlayForm != null && !_overlayForm.IsDisposed && _overlayForm.Visible)
            {
                _overlayForm.UpdateMetrics(snap, _settings);
            }

            // Verifica alertas nativos
            if (_settings.EnableAlerts && !_paused)
            {
                CheckAndNotifyAlerts(snap, summary);
            }

            // Tooltip da bandeja
            string tempPart = snap.CpuTempC.HasValue ? string.Format(_ptBr, " | CPU {0:0}°C", snap.CpuTempC.Value)
                : (snap.GpuTempC.HasValue ? string.Format(_ptBr, " | GPU {0:0}°C", snap.GpuTempC.Value) : "");
            string tip = string.Format(_ptBr, "VoltPulse: {0:0}W{1} | Hoje {2}{3:0.00}",
                snap.EstimatedWallWatts, tempPart, _settings.CurrencySymbol, summary.TodayCost);
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (_trayIcon != null) _trayIcon.Text = tip;

            if (_webReady && _webView != null && _webView.CoreWebView2 != null)
            {
                var payload = new DashboardPayload
                {
                    Type = "telemetry",
                    Telemetry = snap,
                    Summary = summary,
                    Session = session,
                    Settings = _settings,
                    DataDirectory = _storage.DataDirectory,
                    AppVersion = AppVersion
                };

                try
                {
                    string jsonStr = _json.Serialize(payload);
                    _webView.CoreWebView2.PostWebMessageAsJson(jsonStr);
                }
                catch { }
            }
        }

        private void CheckAndNotifyAlerts(TelemetrySnapshot snap, EnergySummary summary)
        {
            if (_trayIcon == null) return;
            bool isEn = _settings.Language == "en-US";

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

            if (_settings.MonthlyBudgetAlert > 0 && summary.MonthCost >= _settings.MonthlyBudgetAlert)
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
                Icon = LoadAppIcon(),
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

        private void RestoreFromTray()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            _allowExit = true;
            _pollTimer.Stop();
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
            _monitor.Dispose();
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowExit && _settings.MinimizeToTrayOnClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                _trayIcon.ShowBalloonTip(2000, "VoltPulse", "Continuando o monitoramento na bandeja do sistema.", ToolTipIcon.Info);
                return;
            }

            _storage.Flush();
            _monitor.Dispose();
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
