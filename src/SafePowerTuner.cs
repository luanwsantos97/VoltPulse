using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace PcPowerMonitor
{
    public class PowerTuningStatus
    {
        public string ActiveProfile { get; set; }      // stock, eco, ultra_eco, custom
        public string EffectiveReason { get; set; }    // manual, game_auto, idle_afk, thermal_guard
        public bool AutoEcoOutsideGames { get; set; }
        public bool IdleEcoTriggered { get; set; }
        public bool ThermalGuardTriggered { get; set; }

        public int CpuMaxPercent { get; set; }         // 50 - 100
        public int CpuMinPercent { get; set; }         // 5 - 100
        public int CpuBoostMode { get; set; }          // 0=Off, 1=Enabled, 2=Aggressive, 3=EfficientEnabled
        public int CpuEpp { get; set; }                // 0 (perf) - 100 (max eco)
        public bool PassiveCooling { get; set; }
        public bool PcieAspmEco { get; set; }

        public bool HasDedicatedGpu { get; set; }
        public string DedicatedGpuVendor { get; set; } // NVIDIA, AMD, NONE
        public string DedicatedGpuName { get; set; }
        public bool GpuPowerLimitSupported { get; set; }
        public string GpuControlBackend { get; set; }  // nvidia-smi, AMD ADL OverdriveN, AMD ADL Overdrive6, none
        public double GpuDefaultWatts { get; set; }
        public double GpuMinWatts { get; set; }
        public double GpuCurrentLimitWatts { get; set; }
        public int GpuPowerLimitPercent { get; set; }  // 70 - 100
        public int GpuMinPercent { get; set; }         // 65 - 80

        // Baseline para comparativo Antes vs Depois
        public double BaselineStockWatts { get; set; }
        public double EstimatedWattsSaved { get; set; }

        public string SafetyNote { get; set; }
        public string LastActionMessage { get; set; }

        public PowerTuningStatus()
        {
            ActiveProfile = "stock";
            EffectiveReason = "manual";
            CpuMaxPercent = 100;
            CpuMinPercent = 5;
            CpuBoostMode = 2;
            CpuEpp = 0;
            DedicatedGpuVendor = "NONE";
            DedicatedGpuName = "";
            GpuControlBackend = "none";
            GpuPowerLimitPercent = 100;
            GpuMinPercent = 70;
        }
    }

    /// <summary>
    /// Motor 100% seguro de eficiencia energetica / undervolt termico (Safe Eco Engine Pro).
    /// - CPU: usa exclusivamente APIs oficiais do Windows powercfg (PROCTHROTTLEMAX, PROCTHROTTLEMIN,
    ///        PERFBOOSTMODE, PERFEPP Intel Speed Shift/AMD CPPC, SYSCOOLPOL e PCIe ASPM).
    /// - GPU Dedicada (integrada bloqueada):
    ///   * NVIDIA: nvidia-smi oficial dentro de [MinLimit, DefaultLimit] de fabrica.
    ///   * AMD Radeon: atiadlxx.dll oficial (OverdriveN / Overdrive6) dentro de [-30% .. 0%] da VBIOS.
    /// </summary>
    public class SafePowerTuner : IDisposable
    {
        private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
        private const string SetProcThrottleMin = "893dee8e-2bef-41e0-89c6-b55d0929964c";
        private const string SetProcThrottleMax = "bc5038f7-23e0-4960-96da-33abaf5935ec";
        private const string SetPerfBoostMode   = "be337238-0d82-4146-a960-4f3749d470c7";
        private const string SetPerfEpp         = "36687f9e-e3a5-4dbf-b1dc-15eb381c6863";
        private const string SetCoolingPolicy   = "94d3a615-a899-4ac5-ae2b-e4d8f634367f";

        private const string SubPcie = "501a4d13-42af-4429-9fd1-a8218c268e20";
        private const string SetPcieAspm = "ee12f906-d277-404b-b6da-e5fa1a576df5";

        private string _nvidiaSmiPath;
        private readonly AmdAdlPowerController _amdAdl = new AmdAdlPowerController();
        private readonly PowerTuningStatus _status = new PowerTuningStatus();

        public SafePowerTuner()
        {
            DetectDedicatedGpu();
        }

        public PowerTuningStatus GetStatus(double currentWallWatts = 0)
        {
            double saved = 0;
            if (_status.ActiveProfile != "stock" && _status.BaselineStockWatts > 20 && currentWallWatts > 10)
            {
                saved = Math.Max(0, Math.Round(_status.BaselineStockWatts - currentWallWatts, 1));
            }
            _status.EstimatedWattsSaved = saved;

            return new PowerTuningStatus
            {
                ActiveProfile = _status.ActiveProfile,
                EffectiveReason = _status.EffectiveReason,
                AutoEcoOutsideGames = _status.AutoEcoOutsideGames,
                IdleEcoTriggered = _status.IdleEcoTriggered,
                ThermalGuardTriggered = _status.ThermalGuardTriggered,
                CpuMaxPercent = _status.CpuMaxPercent,
                CpuMinPercent = _status.CpuMinPercent,
                CpuBoostMode = _status.CpuBoostMode,
                CpuEpp = _status.CpuEpp,
                PassiveCooling = _status.PassiveCooling,
                PcieAspmEco = _status.PcieAspmEco,
                HasDedicatedGpu = _status.HasDedicatedGpu,
                DedicatedGpuVendor = _status.DedicatedGpuVendor,
                DedicatedGpuName = _status.DedicatedGpuName,
                GpuPowerLimitSupported = _status.GpuPowerLimitSupported,
                GpuControlBackend = _status.GpuControlBackend,
                GpuDefaultWatts = _status.GpuDefaultWatts,
                GpuMinWatts = _status.GpuMinWatts,
                GpuCurrentLimitWatts = _status.GpuCurrentLimitWatts,
                GpuPowerLimitPercent = _status.GpuPowerLimitPercent,
                GpuMinPercent = _status.GpuMinPercent,
                BaselineStockWatts = _status.BaselineStockWatts,
                EstimatedWattsSaved = _status.EstimatedWattsSaved,
                SafetyNote = _status.SafetyNote,
                LastActionMessage = _status.LastActionMessage
            };
        }

        public void UpdateStockBaseline(double currentWallWatts)
        {
            if (_status.ActiveProfile != "stock" || currentWallWatts < 20) return;
            if (_status.BaselineStockWatts <= 0)
                _status.BaselineStockWatts = Math.Round(currentWallWatts, 1);
            else
                _status.BaselineStockWatts = Math.Round(_status.BaselineStockWatts * 0.85 + currentWallWatts * 0.15, 1);
        }

        public void SetAutomationFlags(bool idleTriggered, bool thermalTriggered, string reason)
        {
            _status.IdleEcoTriggered = idleTriggered;
            _status.ThermalGuardTriggered = thermalTriggered;
            if (!string.IsNullOrEmpty(reason)) _status.EffectiveReason = reason;
        }

        private void DetectDedicatedGpu()
        {
            try
            {
                var candidates = new List<string>();
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        string name = (Convert.ToString(o["Name"]) ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(name)) candidates.Add(name);
                    }
                }

                foreach (var gpu in candidates)
                {
                    if (IsIntegratedOrVirtualGpu(gpu)) continue;

                    if (gpu.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        gpu.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        gpu.IndexOf("RTX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        gpu.IndexOf("GTX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        gpu.IndexOf("Quadro", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _status.HasDedicatedGpu = true;
                        _status.DedicatedGpuVendor = "NVIDIA";
                        _status.DedicatedGpuName = gpu;
                        ProbeNvidiaSmi();
                        return;
                    }

                    if (gpu.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        gpu.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _status.HasDedicatedGpu = true;
                        _status.DedicatedGpuVendor = "AMD";
                        _status.DedicatedGpuName = gpu;
                        ProbeAmdAdl();
                        return;
                    }
                }

                _status.HasDedicatedGpu = false;
                _status.DedicatedGpuVendor = "NONE";
                _status.SafetyNote = "Nenhuma GPU dedicada (NVIDIA/AMD) elegível detectada (GPUs integradas são ignoradas por segurança). O controle avançado da CPU (EPP + Boost) continua 100% ativo.";
            }
            catch
            {
                _status.HasDedicatedGpu = false;
            }
        }

        public static bool IsIntegratedOrVirtualGpu(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string n = name.ToLowerInvariant();
            if (n.Contains("virtual") || n.Contains("basic display") || n.Contains("remote") ||
                n.Contains("parsec") || n.Contains("mumu") || n.Contains("vmware") || n.Contains("vbox") || n.Contains("citrix"))
                return true;
            if (n.Contains("intel")) return true;
            if (n.Contains("amd") || n.Contains("radeon"))
            {
                bool isDedicatedModel =
                    n.Contains(" rx ") || n.Contains("rx ") ||
                    n.Contains(" r9 ") || n.Contains(" r7 ") ||
                    n.Contains("pro w") || n.Contains("pro wx") ||
                    n.Contains("fury") || n.Contains("vega 56") || n.Contains("vega 64") ||
                    n.Contains("radeon vii");
                if (!isDedicatedModel) return true;
            }
            return false;
        }

        private void ProbeAmdAdl()
        {
            if (_amdAdl.Initialize())
            {
                _status.GpuPowerLimitSupported = true;
                _status.GpuControlBackend = _amdAdl.BackendName;
                _status.GpuMinPercent = _amdAdl.MinSafePercent;
                _status.GpuPowerLimitPercent = _amdAdl.CurrentPercent;
                _status.SafetyNote = string.Format(
                    "GPU dedicada AMD pronta via {0} oficial! Redução segura de Power Limit certificada pela VBIOS ({1}% – 100%, nunca acima de fábrica).",
                    _amdAdl.BackendName, _amdAdl.MinSafePercent);
            }
            else
            {
                _status.GpuPowerLimitSupported = false;
                _status.GpuControlBackend = "none";
                _status.SafetyNote = "GPU dedicada AMD detectada. Ajuste de CPU (EPP/Boost/ASPM) 100% ativo; para ajuste de Power Limit neste driver específico, utilize o AMD Software: Adrenalin Edition.";
            }
        }

        private void ProbeNvidiaSmi()
        {
            string[] paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
                @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe"
            };
            foreach (var p in paths)
            {
                if (File.Exists(p)) { _nvidiaSmiPath = p; break; }
            }
            if (string.IsNullOrEmpty(_nvidiaSmiPath)) return;

            try
            {
                string output = RunCommandCapture(_nvidiaSmiPath, "--query-gpu=power.default_limit,power.min_limit,power.limit --format=csv,noheader,nounits");
                if (string.IsNullOrWhiteSpace(output)) return;
                var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                var parts = line.Split(',');
                if (parts.Length >= 3)
                {
                    double defW, minW, curW;
                    if (double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out defW) &&
                        double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out minW) &&
                        double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out curW) &&
                        defW > 20.0 && minW > 10.0)
                    {
                        _status.GpuPowerLimitSupported = true;
                        _status.GpuControlBackend = "nvidia-smi";
                        _status.GpuDefaultWatts = Math.Round(defW, 1);
                        _status.GpuMinWatts = Math.Round(minW, 1);
                        _status.GpuCurrentLimitWatts = Math.Round(curW, 1);
                        _status.GpuMinPercent = Math.Max(65, (int)Math.Ceiling((minW / defW) * 100.0));
                        _status.GpuPowerLimitPercent = (int)Math.Round(Math.Max(_status.GpuMinPercent, Math.Min(100, (curW / defW) * 100.0)));
                        _status.SafetyNote = string.Format(CultureInfo.InvariantCulture,
                            "GPU dedicada NVIDIA pronta via nvidia-smi oficial dentro da faixa certificada ({0:0}W – {1:0}W).",
                            minW, defW);
                    }
                }
            }
            catch { }
        }

        public PowerTuningStatus ApplyProfile(string profile, bool autoEcoOutsideGames, string reason = "manual")
        {
            _status.AutoEcoOutsideGames = autoEcoOutsideGames;
            _status.EffectiveReason = reason;
            string p = (profile ?? "stock").ToLowerInvariant();

            switch (p)
            {
                case "eco":
                    // Eco Inteligente: CPU 99% (min 5%) + Boost Eficiente + EPP 65 (Speed Shift/CPPC) + GPU 85% (-15%)
                    return ApplyCustom("eco", 99, 5, 3, 65, false, false, 85, autoEcoOutsideGames, reason);

                case "ultra_eco":
                    // Ultra Silencioso: CPU 85% (min 5%) + Boost Off + EPP 90 + Resfriamento Passivo + PCIe ASPM + GPU 75% (-25%)
                    return ApplyCustom("ultra_eco", 85, 5, 0, 90, true, true, 75, autoEcoOutsideGames, reason);

                default:
                    // Padrao de Fabrica (Stock): CPU 100% (min 5%) + Boost Agressivo + EPP 0 + Ativo + PCIe Off + GPU 100%
                    return ApplyCustom("stock", 100, 5, 2, 0, false, false, 100, autoEcoOutsideGames, reason);
            }
        }

        public PowerTuningStatus ApplyCustom(
            string profileName,
            int cpuMaxPct,
            int cpuMinPct,
            int cpuBoostMode,
            int cpuEpp,
            bool passiveCooling,
            bool pcieAspmEco,
            int gpuPowerPct,
            bool autoEcoOutsideGames,
            string reason = "manual")
        {
            cpuMaxPct = Math.Max(50, Math.Min(100, cpuMaxPct));
            cpuMinPct = Math.Max(5, Math.Min(cpuMaxPct, cpuMinPct));
            cpuBoostMode = Math.Max(0, Math.Min(3, cpuBoostMode));
            cpuEpp = Math.Max(0, Math.Min(100, cpuEpp));
            gpuPowerPct = Math.Max(_status.GpuMinPercent, Math.Min(100, gpuPowerPct));

            _status.ActiveProfile = profileName;
            _status.EffectiveReason = reason;
            _status.AutoEcoOutsideGames = autoEcoOutsideGames;
            _status.CpuMaxPercent = cpuMaxPct;
            _status.CpuMinPercent = cpuMinPct;
            _status.CpuBoostMode = cpuBoostMode;
            _status.CpuEpp = cpuEpp;
            _status.PassiveCooling = passiveCooling;
            _status.PcieAspmEco = pcieAspmEco;
            _status.GpuPowerLimitPercent = gpuPowerPct;

            var notes = new List<string>();

            // 1. CPU & Sistema via powercfg oficial (AC e DC)
            try
            {
                SetPowerCfgValue(SubProcessor, SetProcThrottleMin, cpuMinPct);
                SetPowerCfgValue(SubProcessor, SetProcThrottleMax, cpuMaxPct);
                SetPowerCfgValue(SubProcessor, SetPerfBoostMode, cpuBoostMode);
                SetPowerCfgValue(SubProcessor, SetPerfEpp, cpuEpp);
                SetPowerCfgValue(SubProcessor, SetCoolingPolicy, passiveCooling ? 0 : 1);
                SetPowerCfgValue(SubPcie, SetPcieAspm, pcieAspmEco ? 2 : 0);
                RunHidden("powercfg.exe", "/setactive SCHEME_CURRENT");

                notes.Add(string.Format("CPU {0}% (EPP {1})", cpuMaxPct, cpuEpp));
                if (pcieAspmEco) notes.Add("PCIe ASPM");
            }
            catch
            {
                notes.Add("CPU (sem permissão powercfg)");
            }

            // 2. GPU dedicada NVIDIA via nvidia-smi
            if (_status.GpuPowerLimitSupported && _status.DedicatedGpuVendor == "NVIDIA" && !string.IsNullOrEmpty(_nvidiaSmiPath) && _status.GpuDefaultWatts > 20)
            {
                try
                {
                    double targetW = _status.GpuDefaultWatts * (gpuPowerPct / 100.0);
                    targetW = Math.Max(_status.GpuMinWatts, Math.Min(_status.GpuDefaultWatts, targetW));
                    string arg = string.Format(CultureInfo.InvariantCulture, "-pl {0:0.0}", targetW);
                    if (RunHidden(_nvidiaSmiPath, arg) == 0)
                    {
                        _status.GpuCurrentLimitWatts = Math.Round(targetW, 1);
                        notes.Add(string.Format(CultureInfo.InvariantCulture, "NVIDIA {0:0}W ({1}%)", targetW, gpuPowerPct));
                    }
                }
                catch { }
            }
            // 3. GPU dedicada AMD via atiadlxx.dll oficial (OverdriveN / Overdrive6)
            else if (_status.GpuPowerLimitSupported && _status.DedicatedGpuVendor == "AMD" && _amdAdl.IsReady)
            {
                try
                {
                    if (_amdAdl.SetPowerLimitPercent(gpuPowerPct))
                    {
                        int offset = gpuPowerPct - 100;
                        notes.Add(string.Format("AMD GPU {0}% ({1}%)", gpuPowerPct, offset == 0 ? "Stock" : offset.ToString()));
                    }
                }
                catch { }
            }

            _status.LastActionMessage = "Aplicado com segurança: " + string.Join(" • ", notes);
            return GetStatus();
        }

        public void RestoreStock()
        {
            _status.IdleEcoTriggered = false;
            _status.ThermalGuardTriggered = false;
            ApplyProfile("stock", false, "manual");
        }

        private static void SetPowerCfgValue(string subGroup, string settingGuid, int value)
        {
            RunHidden("powercfg.exe", string.Format("/setacvalueindex SCHEME_CURRENT {0} {1} {2}", subGroup, settingGuid, value));
            RunHidden("powercfg.exe", string.Format("/setdcvalueindex SCHEME_CURRENT {0} {1} {2}", subGroup, settingGuid, value));
        }

        private static int RunHidden(string fileName, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) return -1;
                p.WaitForExit(3000);
                return p.ExitCode;
            }
        }

        private static string RunCommandCapture(string fileName, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) return null;
                string res = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return res;
            }
        }

        public void Dispose()
        {
            _amdAdl.Dispose();
        }

        // =========================================================================
        // AMD ADL Official P/Invoke Wrapper (atiadlxx.dll — OverdriveN & Overdrive6)
        // =========================================================================
        private class AmdAdlPowerController : IDisposable
        {
            private const int ADL_OK = 0;

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate IntPtr ADL_Main_Memory_Alloc(int size);

            [StructLayout(LayoutKind.Sequential)]
            private struct ADLODNPowerLimitSetting
            {
                public int iMode;
                public int iTDPLimit;
                public int iMaxOperatingTemperature;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct ADLOD6PowerControlInfo
            {
                public int iMinValue;
                public int iMaxValue;
                public int iStepValue;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
            private struct ADLAdapterInfo
            {
                public int iSize;
                public int iAdapterIndex;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strUDID;
                public int iBusNumber;
                public int iDeviceNumber;
                public int iFunctionNumber;
                public int iVendorID;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAdapterName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strDisplayName;
                public int iPresent;
                public int iExist;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strDriverPath;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strDriverPathExt;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strPNPString;
                public int iOSDisplayIndex;
            }

            [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
            private static extern int ADL2_Main_Control_Create(ADL_Main_Memory_Alloc callback, int iEnumConnectedAdapters, out IntPtr context);

            [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
            private static extern int ADL2_Main_Control_Destroy(IntPtr context);

            [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
            private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int numAdapters);

            [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
            private static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr info, int inputSize);

            [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
            private static extern int ADL2_Overdrive_Caps(IntPtr context, int iAdapterIndex, out int iSupported, out int iEnabled, out int iVersion);

            [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
            private static extern int ADL2_OverdriveN_PowerLimit_Get(IntPtr context, int iAdapterIndex, out ADLODNPowerLimitSetting lpODPowerLimit);

            [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
            private static extern int ADL2_OverdriveN_PowerLimit_Set(IntPtr context, int iAdapterIndex, ref ADLODNPowerLimitSetting lpODPowerLimit);

            [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
            private static extern int ADL2_Overdrive6_PowerControlInfo_Get(IntPtr context, int iAdapterIndex, out ADLOD6PowerControlInfo lpPowerControlInfo);

            [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
            private static extern int ADL2_Overdrive6_PowerControl_Get(IntPtr context, int iAdapterIndex, out int lpCurrentValue, out int lpDefaultValue);

            [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
            private static extern int ADL2_Overdrive6_PowerControl_Set(IntPtr context, int iAdapterIndex, int iValue);

            private readonly ADL_Main_Memory_Alloc _allocDelegate;
            private IntPtr _context = IntPtr.Zero;
            private int _adapterIndex = -1;
            private int _odVersion = 0;
            private int _origMaxTemp = 75;

            public bool IsReady { get { return _context != IntPtr.Zero && _adapterIndex >= 0 && (_odVersion == 7 || _odVersion == 6); } }
            public string BackendName { get; private set; }
            public int MinSafePercent { get; private set; }
            public int CurrentPercent { get; private set; }

            public AmdAdlPowerController()
            {
                _allocDelegate = new ADL_Main_Memory_Alloc(AllocMemory);
                MinSafePercent = 70;
                CurrentPercent = 100;
                BackendName = "none";
            }

            private static IntPtr AllocMemory(int size)
            {
                return Marshal.AllocCoTaskMem(size);
            }

            public bool Initialize()
            {
                try
                {
                    if (!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "atiadlxx.dll")))
                        return false;

                    if (ADL2_Main_Control_Create(_allocDelegate, 1, out _context) != ADL_OK || _context == IntPtr.Zero)
                        return false;

                    int numAdapters;
                    if (ADL2_Adapter_NumberOfAdapters_Get(_context, out numAdapters) != ADL_OK || numAdapters <= 0)
                        return false;

                    int structSize = Marshal.SizeOf(typeof(ADLAdapterInfo));
                    int totalSize = structSize * numAdapters;
                    IntPtr ptr = Marshal.AllocHGlobal(totalSize);
                    try
                    {
                        if (ADL2_Adapter_AdapterInfo_Get(_context, ptr, totalSize) != ADL_OK)
                            return false;

                        for (int i = 0; i < numAdapters; i++)
                        {
                            var info = (ADLAdapterInfo)Marshal.PtrToStructure(new IntPtr(ptr.ToInt64() + i * structSize), typeof(ADLAdapterInfo));
                            if (info.iVendorID != 1002 && info.iVendorID != 0x1002) continue;
                            if (IsIntegratedOrVirtualGpu(info.strAdapterName)) continue;

                            int supported, enabled, ver;
                            if (ADL2_Overdrive_Caps(_context, info.iAdapterIndex, out supported, out enabled, out ver) != ADL_OK)
                                continue;

                            if (ver == 7)
                            {
                                ADLODNPowerLimitSetting pl;
                                if (ADL2_OverdriveN_PowerLimit_Get(_context, info.iAdapterIndex, out pl) == ADL_OK)
                                {
                                    _adapterIndex = info.iAdapterIndex;
                                    _odVersion = 7;
                                    _origMaxTemp = pl.iMaxOperatingTemperature > 50 ? pl.iMaxOperatingTemperature : 75;
                                    MinSafePercent = 70;
                                    CurrentPercent = Math.Max(70, Math.Min(100, 100 + pl.iTDPLimit));
                                    BackendName = "AMD ADL OverdriveN";
                                    return true;
                                }
                            }
                            else if (ver == 6)
                            {
                                ADLOD6PowerControlInfo pInfo;
                                int curVal, defVal;
                                if (ADL2_Overdrive6_PowerControlInfo_Get(_context, info.iAdapterIndex, out pInfo) == ADL_OK &&
                                    ADL2_Overdrive6_PowerControl_Get(_context, info.iAdapterIndex, out curVal, out defVal) == ADL_OK)
                                {
                                    _adapterIndex = info.iAdapterIndex;
                                    _odVersion = 6;
                                    MinSafePercent = Math.Max(70, 100 + pInfo.iMinValue);
                                    CurrentPercent = Math.Max(MinSafePercent, Math.Min(100, 100 + curVal));
                                    BackendName = "AMD ADL Overdrive6";
                                    return true;
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
                catch { }
                return false;
            }

            public bool SetPowerLimitPercent(int percent)
            {
                if (!IsReady) return false;
                percent = Math.Max(MinSafePercent, Math.Min(100, percent));
                int offset = percent - 100; // -30 .. 0 (NUNCA acima de 0 / padrao de fabrica!)

                try
                {
                    if (_odVersion == 7)
                    {
                        ADLODNPowerLimitSetting cur;
                        if (ADL2_OverdriveN_PowerLimit_Get(_context, _adapterIndex, out cur) != ADL_OK)
                            cur = new ADLODNPowerLimitSetting { iMaxOperatingTemperature = _origMaxTemp };

                        cur.iMode = (offset == 0) ? 0 : 1;
                        cur.iTDPLimit = offset;
                        if (cur.iMaxOperatingTemperature < 50) cur.iMaxOperatingTemperature = _origMaxTemp;

                        int rc = ADL2_OverdriveN_PowerLimit_Set(_context, _adapterIndex, ref cur);
                        if (rc == ADL_OK)
                        {
                            CurrentPercent = percent;
                            return true;
                        }
                    }
                    else if (_odVersion == 6)
                    {
                        int rc = ADL2_Overdrive6_PowerControl_Set(_context, _adapterIndex, offset);
                        if (rc == ADL_OK)
                        {
                            CurrentPercent = percent;
                            return true;
                        }
                    }
                }
                catch { }
                return false;
            }

            public void Dispose()
            {
                try
                {
                    if (_context != IntPtr.Zero)
                    {
                        ADL2_Main_Control_Destroy(_context);
                        _context = IntPtr.Zero;
                    }
                }
                catch { }
            }
        }
    }
}
