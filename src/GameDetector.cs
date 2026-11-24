using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PcPowerMonitor
{
    public class GameDetector
    {
        private static readonly string[] GameLibraryMarkers = new[]
        {
            @"\steamapps\common\",
            @"\epic games\",
            @"\riot games\",
            @"\xboxgames\",
            @"\battle.net\",
            @"\gog galaxy\games\",
            @"\ubisoft game launcher\games\",
            @"\ea games\",
            @"\origin games\"
        };

        private static readonly HashSet<string> IgnoredProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
            "code", "devenv", "rider64", "idea64", "windowsterminal", "powershell", "pwsh", "cmd",
            "discord", "spotify", "telegram", "whatsapp", "slack", "teams", "zoom",
            "vlc", "mpv", "wmplayer", "mpc-hc64", "obs64", "obs32", "streamlabs obs",
            "voltpulse", "pcpowermonitor", "taskmgr", "searchhost", "shellexperiencehost",
            "startmenuexperiencehost", "lockapp", "applicationframehost", "mstsc",
            "steam", "steamwebhelper", "steamservice", "epicgameslauncher", "epicwebhelper",
            "riotclientservices", "riotclientux", "riotclientuxrender", "vgc", "vgtray",
            "eadesktop", "eabackgroundservice", "origin", "battle.net", "agent",
            "ubisoftconnect", "upc", "galaxyclient", "crashpad_handler", "crashreporter",
            "unitycrashhandler64", "easyanticheat", "easyanticheat_eos", "beservice",
            "unrealcefsubprocess", "wallpaper32", "wallpaper64", "mumuplayer", "bluestacks"
        };

        private string _activeGameProcess;
        private string _activeGameTitle;
        private DateTime _lastSeenUtc = DateTime.MinValue;
        private DateTime _lastScanUtc = DateTime.MinValue;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        public GameModeStatus Check(double? gpuLoadPercent, bool enabled)
        {
            if (!enabled)
            {
                _activeGameProcess = null;
                _activeGameTitle = null;
                return new GameModeStatus { Active = false };
            }

            DateTime now = DateTime.UtcNow;
            if ((now - _lastScanUtc).TotalSeconds < 3.5)
            {
                return BuildStatus();
            }
            _lastScanUtc = now;

            string detectedProc = null;
            string detectedTitle = null;

            // 1. Verifica janela ativa (tela cheia ou jogo em biblioteca)
            CheckForeground(gpuLoadPercent, out detectedProc, out detectedTitle);

            // 2. Se nao detectou na janela ativa (ex: Alt+Tab), verifica se ha processo rodando em pasta de biblioteca de jogos
            if (detectedProc == null)
            {
                CheckGameLibraryProcesses(out detectedProc, out detectedTitle);
            }

            if (detectedProc != null)
            {
                _activeGameProcess = detectedProc;
                _activeGameTitle = detectedTitle;
                _lastSeenUtc = now;
            }
            else if (_activeGameProcess != null)
            {
                // Tolerancia de 15s para loadings/alt-tab
                if ((now - _lastSeenUtc).TotalSeconds > 15.0)
                {
                    _activeGameProcess = null;
                    _activeGameTitle = null;
                }
            }

            return BuildStatus();
        }

        private GameModeStatus BuildStatus()
        {
            bool active = !string.IsNullOrEmpty(_activeGameProcess);
            return new GameModeStatus
            {
                Active = active,
                ProcessName = _activeGameProcess ?? "",
                GameName = _activeGameTitle ?? "",
                QuietActive = active
            };
        }

        private void CheckForeground(double? gpuLoadPercent, out string procName, out string title)
        {
            procName = null;
            title = null;
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return;

                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid <= 4) return;

                using (var proc = Process.GetProcessById((int)pid))
                {
                    string name = proc.ProcessName;
                    if (string.IsNullOrWhiteSpace(name) || IgnoredProcesses.Contains(name)) return;

                    string exePath = TryGetProcessPath((int)pid);
                    bool inGameLib = IsInGameLibrary(exePath);
                    bool isFullscreen = IsWindowFullscreen(hwnd);
                    bool heavyGpu = gpuLoadPercent.HasValue && gpuLoadPercent.Value >= 40.0;

                    if (inGameLib || isFullscreen || heavyGpu)
                    {
                        procName = name;
                        title = CleanGameTitle(proc.MainWindowTitle, exePath, name);
                    }
                }
            }
            catch { }
        }

        private void CheckGameLibraryProcesses(out string procName, out string title)
        {
            procName = null;
            title = null;
            try
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.Id <= 4) continue;
                        string name = p.ProcessName;
                        if (string.IsNullOrWhiteSpace(name) || IgnoredProcesses.Contains(name)) continue;
                        if (p.MainWindowHandle == IntPtr.Zero) continue;

                        string path = TryGetProcessPath(p.Id);
                        if (IsInGameLibrary(path))
                        {
                            procName = name;
                            title = CleanGameTitle(p.MainWindowTitle, path, name);
                            break;
                        }
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch { }
        }

        private static bool IsInGameLibrary(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return false;
            string lower = exePath.ToLowerInvariant();
            if (lower.Contains("launcher") || lower.Contains("redist") || lower.Contains("setup") || lower.Contains("unins"))
                return false;
            foreach (var marker in GameLibraryMarkers)
            {
                if (lower.Contains(marker)) return true;
            }
            return false;
        }

        private static bool IsWindowFullscreen(IntPtr hwnd)
        {
            try
            {
                RECT r;
                if (!GetWindowRect(hwnd, out r)) return false;
                var screen = Screen.FromHandle(hwnd);
                if (screen == null) return false;
                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                return w >= screen.Bounds.Width - 4 && h >= screen.Bounds.Height - 4;
            }
            catch
            {
                return false;
            }
        }

        private static string TryGetProcessPath(int pid)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return null;
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref size))
                    return sb.ToString();
            }
            catch { }
            finally
            {
                if (h != IntPtr.Zero) CloseHandle(h);
            }
            return null;
        }

        private static string CleanGameTitle(string windowTitle, string exePath, string fallbackName)
        {
            if (!string.IsNullOrWhiteSpace(windowTitle) && windowTitle.Length <= 48)
                return windowTitle.Trim();
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                string lower = exePath.ToLowerInvariant();
                foreach (var marker in GameLibraryMarkers)
                {
                    int idx = lower.IndexOf(marker, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        string rest = exePath.Substring(idx + marker.Length);
                        int slash = rest.IndexOf('\\');
                        string folder = slash > 0 ? rest.Substring(0, slash) : rest;
                        if (!string.IsNullOrWhiteSpace(folder)) return folder.Trim();
                    }
                }
                return Path.GetFileNameWithoutExtension(exePath);
            }
            return fallbackName;
        }
    }
}
