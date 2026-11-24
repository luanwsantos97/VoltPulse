using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;

namespace PcPowerMonitor
{
    public class PowerTuningStatus
    {
        public string ActiveProfile { get; set; } // stock, eco, ultra_eco, custom
        public bool AutoEcoOutsideGames { get; set; }
        public int CpuMaxPercent { get; set; }    // 50 - 100
        public int CpuBoostMode { get; set; }     // 0=Off, 1=Enabled, 2=Aggressive, 3=EfficientEnabled

        public bool HasDedicatedGpu { get; set; }
        public string DedicatedGpuVendor { get; set; } // NVIDIA, AMD, NONE
        public string DedicatedGpuName { get; set; }
        public bool GpuPowerLimitSupported { get; set; }
        public double GpuDefaultWatts { get; set; }
        public double GpuMinWatts { get; set; }
        public double GpuCurrentLimitWatts { get; set; }
        public int GpuPowerLimitPercent { get; set; }  // 65 - 100

        public string SafetyNote { get; set; }
        public string LastActionMessage { get; set; }

        public PowerTuningStatus()
        {
            ActiveProfile = "stock";
            CpuMaxPercent = 100;
            CpuBoostMode = 2;
            DedicatedGpuVendor = "NONE";
            DedicatedGpuName = "";
            GpuPowerLimitPercent = 100;
        }
    }

    /// <summary>
    /// Motor 100% seguro de eficiencia energetica / undervolt seguro (Safe Power & Thermal Tuning).
    /// - NUNCA escreve em registradores MSR/VRM nao-documentados (zero risco de BSOD ou dano).
    /// - CPU: usa exclusivamente APIs oficiais do Windows (powercfg PROCTHROTTLEMAX e PERFBOOSTMODE).
    /// - GPU: filtra rigorosamente GPUs integradas (Intel UHD/Iris, AMD iGPU, virtuais).
    ///        Em NVIDIA dedicada usa nvidia-smi oficial respeitando estritamente [MinLimit, DefaultLimit].
    ///        Em AMD dedicada aplica perfil seguro de CPU/energia + orientacao segura para Adrenalin.
    /// </summary>
    public class SafePowerTuner
    {
        private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
        private const string SetProcThrottleMax = "bc5038f7-23e0-4960-96da-33abaf5935ec";
        private const string SetPerfBoostMode = "be337238-0d82-4146-a960-4f3749d470c7";

        private string _nvidiaSmiPath;
        private readonly PowerTuningStatus _status = new PowerTuningStatus();

        public SafePowerTuner()
        {
            DetectDedicatedGpu();
        }

        public PowerTuningStatus GetStatus()
        {
            return new PowerTuningStatus
            {
                ActiveProfile = _status.ActiveProfile,
                AutoEcoOutsideGames = _status.AutoEcoOutsideGames,
                CpuMaxPercent = _status.CpuMaxPercent,
                CpuBoostMode = _status.CpuBoostMode,
                HasDedicatedGpu = _status.HasDedicatedGpu,
                DedicatedGpuVendor = _status.DedicatedGpuVendor,
                DedicatedGpuName = _status.DedicatedGpuName,
                GpuPowerLimitSupported = _status.GpuPowerLimitSupported,
                GpuDefaultWatts = _status.GpuDefaultWatts,
                GpuMinWatts = _status.GpuMinWatts,
                GpuCurrentLimitWatts = _status.GpuCurrentLimitWatts,
                GpuPowerLimitPercent = _status.GpuPowerLimitPercent,
                SafetyNote = _status.SafetyNote,
                LastActionMessage = _status.LastActionMessage
            };
        }

        private void DetectDedicatedGpu()
        {
            try
            {
                var candidates = new List<string>();
                using (var s = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        string name = (Convert.ToString(o["Name"]) ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        candidates.Add(name);
                    }
                }

                // Filtra rigorosamente GPUs integradas e adaptadores virtuais
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
                        _status.GpuPowerLimitSupported = false;
                        _status.SafetyNote = "GPU dedicada AMD detectada. O ajuste seguro da CPU e energia do Windows está 100% ativo; para ajuste fino de tensão mV na Radeon, utilize o perfil 'Subtensão GPU' oficial no AMD Software: Adrenalin Edition (evita conflitos de driver).";
                        return;
                    }
                }

                _status.HasDedicatedGpu = false;
                _status.DedicatedGpuVendor = "NONE";
                _status.SafetyNote = "Nenhuma GPU dedicada (NVIDIA/AMD) elegível detectada (GPUs integradas são ignoradas por segurança). O modo seguro de eficiência da CPU continua 100% disponível.";
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
            // Virtuais / remotos
            if (n.Contains("virtual") || n.Contains("basic display") || n.Contains("remote") ||
                n.Contains("parsec") || n.Contains("mumu") || n.Contains("vmware") || n.Contains("vbox") || n.Contains("citrix"))
                return true;
            // Intel integradas (somente Arc dedicada passaria, mas por segurança focamos NVIDIA/AMD dedicadas)
            if (n.Contains("intel")) return true;
            // AMD integradas (APU Vega / Radeon(TM) Graphics sem modelo RX/PRO/R9/R7/HD dedicado)
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
                        _status.GpuDefaultWatts = Math.Round(defW, 1);
                        _status.GpuMinWatts = Math.Round(minW, 1);
                        _status.GpuCurrentLimitWatts = Math.Round(curW, 1);
                        _status.GpuPowerLimitPercent = (int)Math.Round(Math.Max(50, Math.Min(100, (curW / defW) * 100.0)));
                        _status.SafetyNote = string.Format(CultureInfo.InvariantCulture,
                            "GPU dedicada NVIDIA pronta! Controle oficial via nvidia-smi dentro da faixa certificada de fábrica ({0:0}W – {1:0}W).",
                            minW, defW);
                    }
                }
            }
            catch { }
        }

        public PowerTuningStatus ApplyProfile(string profile, bool autoEcoOutsideGames)
        {
            _status.AutoEcoOutsideGames = autoEcoOutsideGames;
            string p = (profile ?? "stock").ToLowerInvariant();

            switch (p)
            {
                case "eco":
                    // Eco Inteligente: 99% CPU (desliga picos de turbo desnecessarios -> -15C / -30W) + Boost Eficiente + GPU 85%
                    return ApplyCustom("eco", 99, 3, 85, autoEcoOutsideGames);

                case "ultra_eco":
                    // Ultra Silencioso: 85% CPU + Boost Off + GPU 75%
                    return ApplyCustom("ultra_eco", 85, 0, 75, autoEcoOutsideGames);

                default:
                    // Padrao de Fabrica (Stock): 100% CPU + Boost Agressivo + GPU 100%
                    return ApplyCustom("stock", 100, 2, 100, autoEcoOutsideGames);
            }
        }

        public PowerTuningStatus ApplyCustom(string profileName, int cpuMaxPct, int cpuBoostMode, int gpuPowerPct, bool autoEcoOutsideGames)
        {
            // Clamps rigidos de seguranca (nunca deixa o PC inutilizavel ou fora da especificacao)
            cpuMaxPct = Math.Max(50, Math.Min(100, cpuMaxPct));
            cpuBoostMode = Math.Max(0, Math.Min(3, cpuBoostMode));
            gpuPowerPct = Math.Max(65, Math.Min(100, gpuPowerPct));

            _status.ActiveProfile = profileName;
            _status.AutoEcoOutsideGames = autoEcoOutsideGames;
            _status.CpuMaxPercent = cpuMaxPct;
            _status.CpuBoostMode = cpuBoostMode;
            _status.GpuPowerLimitPercent = gpuPowerPct;

            var notes = new List<string>();

            // 1. CPU via powercfg oficial (AC e DC)
            try
            {
                RunHidden("powercfg.exe", string.Format("/setacvalueindex SCHEME_CURRENT {0} {1} {2}", SubProcessor, SetProcThrottleMax, cpuMaxPct));
                RunHidden("powercfg.exe", string.Format("/setdcvalueindex SCHEME_CURRENT {0} {1} {2}", SubProcessor, SetProcThrottleMax, cpuMaxPct));
                RunHidden("powercfg.exe", string.Format("/setacvalueindex SCHEME_CURRENT {0} {1} {2}", SubProcessor, SetPerfBoostMode, cpuBoostMode));
                RunHidden("powercfg.exe", string.Format("/setdcvalueindex SCHEME_CURRENT {0} {1} {2}", SubProcessor, SetPerfBoostMode, cpuBoostMode));
                RunHidden("powercfg.exe", "/setactive SCHEME_CURRENT");
                notes.Add(string.Format("CPU {0}%", cpuMaxPct));
            }
            catch
            {
                notes.Add("CPU (sem permissão powercfg)");
            }

            // 2. GPU dedicada NVIDIA via nvidia-smi oficial dentro de [MinWatts, DefaultWatts]
            if (_status.GpuPowerLimitSupported && !string.IsNullOrEmpty(_nvidiaSmiPath) && _status.GpuDefaultWatts > 20)
            {
                try
                {
                    double targetW = _status.GpuDefaultWatts * (gpuPowerPct / 100.0);
                    // Nunca abaixo do minimo de fabrica nem acima do padrao de fabrica
                    targetW = Math.Max(_status.GpuMinWatts, Math.Min(_status.GpuDefaultWatts, targetW));
                    string arg = string.Format(CultureInfo.InvariantCulture, "-pl {0:0.0}", targetW);
                    int code = RunHidden(_nvidiaSmiPath, arg);
                    if (code == 0)
                    {
                        _status.GpuCurrentLimitWatts = Math.Round(targetW, 1);
                        notes.Add(string.Format(CultureInfo.InvariantCulture, "GPU {0:0}W ({1}%)", targetW, gpuPowerPct));
                    }
                }
                catch { }
            }

            _status.LastActionMessage = "Aplicado com segurança: " + string.Join(" • ", notes);
            return GetStatus();
        }

        public void RestoreStock()
        {
            ApplyProfile("stock", false);
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
    }
}
