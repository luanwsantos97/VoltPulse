using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PcPowerMonitor
{
    /// <summary>
    /// Detector cirurgico de jogos (zero falsos positivos com Lightshot, Antigravity, navegadores, apps Electron, etc.).
    /// Criterios rigorosos:
    /// 1) Processo dentro de diretorio oficial de biblioteca de jogos (Steam, Epic, Riot, Xbox, EA, Ubisoft, Battle.net, GOG, Rockstar, HoYoverse...)
    /// 2) Executavel conhecido de jogo popular (Valorant, LoL, CS2, GTA5, Fortnite, Roblox, Minecraft, Cyberpunk, Elden Ring, etc.)
    /// 3) Processo em primeiro plano carregando DLL exclusiva de Engine/SDK de jogo (UnityPlayer, steam_api, EOSSDK, fmod, dinput8, nvngx_dlss, amd_ags_x64, PhysX, bink2w64)
    ///    e explicitamente NAO sendo app de captura, navegador, IDE ou Electron/CEF/WebView2.
    /// </summary>
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
            @"\origin games\",
            @"\rockstar games\",
            @"\hoyoverse\",
            @"\mihoyo\",
            @"\wargaming.net\"
        };

        private static readonly HashSet<string> KnownGameExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cs2", "csgo", "valorant-win64-shipping", "valorant", "league of legends", "fortniteclient-win64-shipping",
            "gta5", "gta5_enhanced", "gtav", "fivem_gtaprocess", "rdr2", "cyberpunk2077", "eldenring", "dota2",
            "tslgame", "r5apex", "r5apex_dx12", "overwatch", "rocketleague", "robloxplayerbeta", "genshinimpact",
            "starrail", "zenlesszonezero", "cod", "mw2", "blackops6", "helldivers2", "palworld-win64-shipping",
            "bg3", "bg3_dx11", "witcher3", "hogwartslegacy", "starfield", "fallout4", "skyrimse",
            "fc24", "fc25", "ea sports fc 25", "fifa23", "nba2k24", "nba2k25", "forzahorizon5", "forzahorizon4", "fh5",
            "rainbowsix", "rainbowsix_vulkan", "destiny2", "warframe.x64", "pathofexile", "pathofexile_x64", "pathofexile2", "pathofexile2_x64",
            "diablo iv", "wow", "wowclassic", "ffxiv_dx11", "escapefromtarkov", "rustclient",
            "dayz_x64", "arkascended", "shootergame", "terraria", "stardew valley", "hollow_knight",
            "hades", "hades2", "deadbydaylight-win64-shipping", "phasmophobia", "lethal company",
            "among us", "fallguys_client_game", "bf2042", "bfv", "bf1", "bf4", "bf3", "tarkov", "pubg",
            "minecraft.windows", "eurotrucks2", "amtrucks", "acs", "ac2-win64-shipping", "f1_24", "f1_23",
            "b1-win64-shipping", "stalker2-win64-shipping", "marvel-win64-shipping", "deltaforceclient-win64-shipping",
            "godofwar", "gowr", "ghostoftsushima", "re4", "valheim", "factorio", "satisfactory"
        };

        private static readonly Dictionary<string, string> FriendlyGameNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "bf1", "Battlefield 1" },
            { "bfv", "Battlefield V" },
            { "bf4", "Battlefield 4" },
            { "bf3", "Battlefield 3" },
            { "bf2042", "Battlefield 2042" },
            { "cs2", "Counter-Strike 2" },
            { "valorant-win64-shipping", "VALORANT" },
            { "fortniteclient-win64-shipping", "Fortnite" },
            { "gta5", "GTA V" },
            { "gta5_enhanced", "GTA V Enhanced" },
            { "rdr2", "Red Dead Redemption 2" },
            { "cyberpunk2077", "Cyberpunk 2077" },
            { "eldenring", "Elden Ring" },
            { "r5apex", "Apex Legends" },
            { "r5apex_dx12", "Apex Legends" },
            { "tslgame", "PUBG" },
            { "fc25", "EA SPORTS FC 25" },
            { "fc24", "EA SPORTS FC 24" },
            { "league of legends", "League of Legends" },
            { "dota2", "Dota 2" }
        };

        // DLLs altamente especificas de engines e SDKs de jogos (ausentes em Lightshot, navegadores, IDEs, Office, etc.)
        private static readonly string[] GameEngineDllMarkers = new[]
        {
            "unityplayer.dll",
            "gameassembly.dll",
            "steam_api64.dll",
            "steam_api.dll",
            "eossdk-win64-shipping.dll",
            "eossdk-win32-shipping.dll",
            "fmod64.dll",
            "fmodstudio.dll",
            "fmod.dll",
            "bink2w64.dll",
            "binkw64.dll",
            "nvngx_dlss.dll",
            "amd_ags_x64.dll",
            "physx3_x64.dll",
            "physx_64.dll",
            "cryrenderd3d11.dll",
            "xaudio2_7.dll",
            "dinput8.dll",
            "galaxy64.dll",
            "uplay_r1_loader64.dll",
            "uplay_r2_loader64.dll"
        };

        // Se o processo carregar qualquer uma destas DLLs, trata-se de navegador/Electron/WebView2 (nao e jogo!)
        private static readonly string[] NonGameFrameworkDlls = new[]
        {
            "chrome_elf.dll",
            "webview2loader.dll",
            "msedge_elf.dll",
            "xul.dll"
        };

        private static readonly HashSet<string> IgnoredProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc",
            "code", "devenv", "rider64", "idea64", "antigravity", "cursor", "windsurf",
            "windowsterminal", "powershell", "pwsh", "cmd", "conhost",
            "discord", "spotify", "telegram", "whatsapp", "slack", "teams", "zoom", "notion",
            "lightshot", "sharex", "greenshot", "snippingtool", "screenclippinghost", "snagit32", "gyazo",
            "vlc", "mpv", "wmplayer", "mpc-hc64", "potplayermini64", "obs64", "obs32", "streamlabs obs",
            "voltpulse", "pcpowermonitor", "taskmgr", "perfmon", "mmc", "regedit",
            "searchhost", "shellexperiencehost", "startmenuexperiencehost", "lockapp", "applicationframehost", "mstsc",
            "steam", "steamwebhelper", "steamservice", "epicgameslauncher", "epicwebhelper",
            "riotclientservices", "riotclientux", "riotclientuxrender", "vgc", "vgtray",
            "eadesktop", "eabackgroundservice", "origin", "battle.net", "agent",
            "ubisoftconnect", "upc", "galaxyclient", "crashpad_handler", "crashreporter",
            "unitycrashhandler64", "easyanticheat", "easyanticheat_eos", "beservice",
            "unrealcefsubprocess", "wallpaper32", "wallpaper64", "mumuplayer", "bluestacks",
            "photoshop", "premiere", "afterfx", "illustrator", "blender", "davinci resolve",
            "winword", "excel", "powerpnt", "outlook", "acrobat", "acrord32",
            "radeonsoftware", "amdrsserv", "nvcontainer", "nvidia share", "geforce experience", "msiafterburner", "rtss"
        };

        private string _activeGameProcess;
        private string _activeGameTitle;
        private int _activeGamePid;
        private DateTime _lastSeenUtc = DateTime.MinValue;
        private DateTime _lastScanUtc = DateTime.MinValue;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public GameModeStatus Check(double? gpuLoadPercent, bool enabled)
        {
            if (!enabled)
            {
                _activeGameProcess = null;
                _activeGameTitle = null;
                _activeGamePid = 0;
                return new GameModeStatus { Active = false };
            }

            DateTime now = DateTime.UtcNow;
            if ((now - _lastScanUtc).TotalSeconds < 4.0)
            {
                return BuildStatus();
            }
            _lastScanUtc = now;

            string detectedProc = null;
            string detectedTitle = null;
            int detectedPid = 0;

            // 1. Verifica janela ativa com criterios rigorosos (zero falsos positivos)
            CheckForeground(out detectedProc, out detectedTitle, out detectedPid);

            // 2. Verifica processos com janela aberta em bibliotecas oficiais de jogos ou na whitelist de jogos
            if (detectedProc == null)
            {
                CheckRunningGames(out detectedProc, out detectedTitle, out detectedPid);
            }

            if (detectedProc != null)
            {
                _activeGameProcess = detectedProc;
                _activeGameTitle = detectedTitle;
                _activeGamePid = detectedPid;
                _lastSeenUtc = now;
            }
            else if (_activeGameProcess != null)
            {
                if ((now - _lastSeenUtc).TotalSeconds > 15.0)
                {
                    _activeGameProcess = null;
                    _activeGameTitle = null;
                    _activeGamePid = 0;
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
                ProcessId = active ? _activeGamePid : 0,
                ProcessName = _activeGameProcess ?? "",
                GameName = _activeGameTitle ?? "",
                QuietActive = active
            };
        }

        private void CheckForeground(out string procName, out string title, out int foundPid)
        {
            procName = null;
            title = null;
            foundPid = 0;
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
                    if (IsSystemOrUtilityPath(exePath)) return;

                    // Minecraft Java Edition
                    if (string.Equals(name, "javaw", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(proc.MainWindowTitle) &&
                            proc.MainWindowTitle.IndexOf("Minecraft", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            procName = name;
                            title = "Minecraft";
                            foundPid = (int)pid;
                        }
                        return;
                    }

                    bool isKnown = KnownGameExecutables.Contains(name);
                    bool inGameLib = IsInGameLibrary(exePath);
                    bool hasGameEngine = !isKnown && !inGameLib && HasGameEngineModules(proc);

                    if (isKnown || inGameLib || hasGameEngine)
                    {
                        procName = name;
                        title = CleanGameTitle(proc.MainWindowTitle, exePath, name);
                        foundPid = (int)pid;
                    }
                }
            }
            catch { }
        }

        private void CheckRunningGames(out string procName, out string title, out int foundPid)
        {
            procName = null;
            title = null;
            foundPid = 0;
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

                        // Jogos protegidos por Anti-Cheat (ex: EA AntiCheat em BF1/FC25, Vanguard) bloqueiam MainWindowHandle/OpenProcess.
                        // Se o executavel esta na whitelist confirmada de jogos, detecta imediatamente!
                        if (KnownGameExecutables.Contains(name))
                        {
                            string wTitle = "";
                            try { wTitle = p.MainWindowTitle; } catch { }
                            string kPath = TryGetProcessPath(p.Id);
                            procName = name;
                            title = CleanGameTitle(wTitle, kPath, name);
                            foundPid = p.Id;
                            break;
                        }

                        if (p.MainWindowHandle == IntPtr.Zero) continue;

                        string path = TryGetProcessPath(p.Id);
                        if (IsSystemOrUtilityPath(path)) continue;

                        if (IsInGameLibrary(path))
                        {
                            procName = name;
                            title = CleanGameTitle(p.MainWindowTitle, path, name);
                            foundPid = p.Id;
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

        private static bool HasGameEngineModules(Process proc)
        {
            try
            {
                bool foundEngine = false;
                foreach (ProcessModule mod in proc.Modules)
                {
                    string modName = (mod.ModuleName ?? "").ToLowerInvariant();
                    foreach (var nonGame in NonGameFrameworkDlls)
                    {
                        if (modName == nonGame) return false; // Navegador / Electron / WebView2 -> nunca e jogo
                    }
                    if (!foundEngine)
                    {
                        foreach (var eng in GameEngineDllMarkers)
                        {
                            if (modName == eng) { foundEngine = true; break; }
                        }
                    }
                }
                return foundEngine;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSystemOrUtilityPath(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return false;
            string lower = exePath.ToLowerInvariant();
            if (lower.StartsWith(@"c:\windows\")) return true;
            if (lower.Contains(@"\skillbrains\lightshot\")) return true;
            if (lower.Contains(@"\antigravity\")) return true;
            if (lower.Contains(@"\microsoft vs code\")) return true;
            if (lower.Contains(@"\google\chrome\")) return true;
            if (lower.Contains(@"\microsoft\edge\")) return true;
            if (lower.Contains(@"\mozilla firefox\")) return true;
            if (lower.Contains(@"\voltpulse\")) return true;
            return false;
        }

        private static bool IsInGameLibrary(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return false;
            string lower = exePath.ToLowerInvariant();
            if (lower.Contains("launcher") || lower.Contains("redist") || lower.Contains("setup") ||
                lower.Contains("unins") || lower.Contains("crash") || lower.Contains("helper") ||
                lower.Contains("updater") || lower.Contains("overlay") || lower.Contains("anticheat"))
                return false;

            foreach (var marker in GameLibraryMarkers)
            {
                if (lower.Contains(marker)) return true;
            }
            return false;
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
            string friendly;
            if (!string.IsNullOrWhiteSpace(fallbackName) && FriendlyGameNames.TryGetValue(fallbackName, out friendly))
                return friendly;
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
