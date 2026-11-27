using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Security.Principal;
using DiskInfoToolkit;
using DiskInfoToolkit.Enums.Interop;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace PcPowerMonitor
{
    public class HardwareMonitorService : IDisposable
    {
        private readonly Computer _computer;
        private readonly UpdateVisitor _visitor = new UpdateVisitor();
        private readonly GameModeUpdateVisitor _gameVisitor = new GameModeUpdateVisitor();
        private readonly bool _isAdmin;
        private bool _disposed;
        private string _initError;

        private DateTime _lastDiskRefreshUtc = DateTime.MinValue;
        private List<DiskHealthInfo> _cachedDisks = new List<DiskHealthInfo>();
        private readonly ProcessMonitor _processMonitor = new ProcessMonitor();

        // Fallback WMI universal (garante nomes exatos de CPU, GPU, Placa-mãe e RAM em qualquer PC)
        private string _wmiCpuName;
        private string _wmiGpuName;
        private bool _wmiGpuIsIntegrated;
        private string _wmiSecondaryGpuName;
        private string _wmiMotherboardName;
        private double? _wmiTotalRamGb;
        private double? _wmiBatteryDesignMwh;
        private double? _wmiBatteryFullMwh;

        public HardwareMonitorService(Action<int, string> onProgress = null)
        {
            _isAdmin = CheckIsAdmin();
            if (onProgress != null) onProgress(30, "Identificando CPU, GPU, Placa-mãe e RAM...");
            LoadWmiHardwareFallback();

            if (onProgress != null) onProgress(55, "Abrindo sensores de hardware (LibreHardwareMonitor)...");
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsMemoryEnabled = true,
                IsPsuEnabled = false,
                IsControllerEnabled = false,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
                IsBatteryEnabled = true
            };

            try
            {
                _computer.Open();
            }
            catch (Exception ex)
            {
                _initError = ex.Message;
            }

            if (onProgress != null) onProgress(85, "Mapeando saúde S.M.A.R.T. dos SSDs e discos...");
        }

        private void LoadWmiHardwareFallback()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        var n = Convert.ToString(o["Name"]);
                        if (!string.IsNullOrWhiteSpace(n)) { _wmiCpuName = n.Trim(); break; }
                    }
                }
            }
            catch { }

            try
            {
                var gpus = new List<string>();
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        var n = Convert.ToString(o["Name"]);
                        if (!string.IsNullOrWhiteSpace(n)) gpus.Add(n.Trim());
                    }
                }
                if (gpus.Count > 0)
                {
                    string primaryName, secondaryName;
                    bool primaryIsIntegrated;
                    SelectPrimaryAndSecondaryGpuNames(gpus, out primaryName, out primaryIsIntegrated, out secondaryName);
                    _wmiGpuName = primaryName;
                    _wmiGpuIsIntegrated = primaryIsIntegrated;
                    _wmiSecondaryGpuName = secondaryName;
                }
            }
            catch { }

            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        var mfg = (Convert.ToString(o["Manufacturer"]) ?? "").Trim();
                        var prod = (Convert.ToString(o["Product"]) ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(prod))
                        {
                            _wmiMotherboardName = string.IsNullOrWhiteSpace(mfg) || prod.StartsWith(mfg, StringComparison.OrdinalIgnoreCase)
                                ? prod : (mfg + " " + prod);
                            break;
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (var s = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        ulong bytes = Convert.ToUInt64(o["TotalPhysicalMemory"]);
                        if (bytes > 0) { _wmiTotalRamGb = Math.Round(bytes / 1073741824.0, 1); break; }
                    }
                }
            }
            catch { }

            // Capacidade de fabrica vs atual da bateria (notebooks)
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        double cap = Convert.ToDouble(o["DesignedCapacity"]);
                        if (cap > 0) { _wmiBatteryDesignMwh = cap; break; }
                    }
                }
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        double cap = Convert.ToDouble(o["FullChargedCapacity"]);
                        if (cap > 0) { _wmiBatteryFullMwh = cap; break; }
                    }
                }
            }
            catch { }
        }

        public bool IsAdmin { get { return _isAdmin; } }
        public bool PawnIoInstalled { get { return CheckPawnIoInstalled(); } }

        public static bool CheckIsAdmin()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool CheckPawnIoInstalled()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO"))
                {
                    return key != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public TelemetrySnapshot Capture(AppSettings settings, bool paused, bool backgroundMode = false, bool gameMode = false)
        {
            var now = DateTime.Now;
            var snap = new TelemetrySnapshot
            {
                TimestampIso = now.ToString("o"),
                TimeLabel = now.ToString("HH:mm:ss"),
                IsAdmin = _isAdmin,
                PawnIoInstalled = CheckPawnIoInstalled(),
                Paused = paused,
                CpuName = _wmiCpuName ?? "CPU",
                GpuName = _wmiGpuName ?? "GPU",
                GpuIsIntegrated = _wmiGpuIsIntegrated,
                SecondaryGpuName = _wmiSecondaryGpuName ?? "",
                MotherboardName = _wmiMotherboardName ?? "Motherboard"
            };

            if (!string.IsNullOrEmpty(_initError))
            {
                snap.StatusNote = "Erro ao abrir sensores: " + _initError;
            }

            try
            {
                // Durante jogos em segundo plano, atualiza apenas CPU/GPU/RAM (evita polling LPC/SMBus/SuperIO e latencia DPC/USB)
                if (gameMode)
                    _computer.Accept(_gameVisitor);
                else
                    _computer.Accept(_visitor);
            }
            catch (Exception ex)
            {
                snap.StatusNote = "Erro na leitura: " + ex.Message;
            }

            var cpuPower = new List<SensorReading>();
            var gpuPower = new List<SensorReading>();
            var otherPower = new List<SensorReading>();

            var cpuTemps = new List<SensorReading>();
            var gpuTemps = new List<SensorReading>();
            var storageTemps = new List<SensorReading>();

            var cpuClocks = new List<SensorReading>();
            var gpuClocks = new List<SensorReading>();
            var cpuLoads = new List<SensorReading>();
            var gpuLoads = new List<SensorReading>();

            var lhmStorageSensors = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);

            double? ramUsed = null;
            double? ramAvail = null;
            double? vramUsed = null;
            double? vramTotal = null;

            // Seleciona a GPU principal (prefere dedicada em PCs/notebooks hibridos; ignora virtuais)
            IHardware primaryGpuHw = null;
            IHardware secondaryGpuHw = null;
            foreach (var hw in _computer.Hardware)
            {
                if (!IsGpu(hw.HardwareType)) continue;
                var kind = ClassifyGpu(hw.Name, hw.HardwareType);
                if (kind == GpuKind.Virtual) continue;

                if (kind == GpuKind.Dedicated)
                {
                    if (primaryGpuHw == null || ClassifyGpu(primaryGpuHw.Name, primaryGpuHw.HardwareType) != GpuKind.Dedicated)
                    {
                        if (primaryGpuHw != null && secondaryGpuHw == null) secondaryGpuHw = primaryGpuHw;
                        primaryGpuHw = hw;
                    }
                }
                else // Integrated
                {
                    if (primaryGpuHw == null)
                        primaryGpuHw = hw;
                    else if (secondaryGpuHw == null)
                        secondaryGpuHw = hw;
                }
            }

            if (primaryGpuHw != null)
            {
                snap.GpuName = primaryGpuHw.Name;
                snap.GpuIsIntegrated = ClassifyGpu(primaryGpuHw.Name, primaryGpuHw.HardwareType) == GpuKind.Integrated;
                if (secondaryGpuHw != null && !string.Equals(secondaryGpuHw.Name, primaryGpuHw.Name, StringComparison.OrdinalIgnoreCase))
                    snap.SecondaryGpuName = secondaryGpuHw.Name;
                else if (snap.GpuIsIntegrated)
                    snap.SecondaryGpuName = "";
            }

            foreach (var hw in _computer.Hardware)
            {
                if (hw.HardwareType == HardwareType.Cpu)
                    snap.CpuName = hw.Name;
                else if (hw.HardwareType == HardwareType.Motherboard)
                    snap.MotherboardName = hw.Name;
                else if (hw.HardwareType == HardwareType.Storage && !string.IsNullOrWhiteSpace(hw.Name))
                {
                    snap.StorageNames.Add(hw.Name);
                    var dict = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                    foreach (var s in hw.Sensors)
                    {
                        if (s.Value.HasValue && !float.IsNaN(s.Value.Value))
                            dict[s.SensorType + ":" + s.Name] = s.Value.Value;
                    }
                    lhmStorageSensors[hw.Name] = dict;
                }
                else if (hw.HardwareType == HardwareType.Network)
                {
                    CollectNetwork(hw, snap);
                    continue;
                }

                bool isPrimaryGpu = IsGpu(hw.HardwareType) && (primaryGpuHw == null || object.ReferenceEquals(hw, primaryGpuHw));
                CollectHardware(
                    hw, hw.Name, hw.HardwareType, isPrimaryGpu, snap,
                    cpuPower, gpuPower, otherPower,
                    cpuTemps, gpuTemps, storageTemps,
                    cpuClocks, gpuClocks, cpuLoads, gpuLoads,
                    ref ramUsed, ref ramAvail, ref vramUsed, ref vramTotal);
            }

            // Rede ativa (soma ou maior tráfego)
            if (snap.Networks.Count > 0)
            {
                snap.NetDownloadKBps = Math.Round(snap.Networks.Sum(n => n.DownloadKBps), 1);
                snap.NetUploadKBps = Math.Round(snap.Networks.Sum(n => n.UploadKBps), 1);
                var activeNet = snap.Networks
                    .OrderByDescending(n => n.DownloadKBps + n.UploadKBps)
                    .ThenByDescending(n => (n.DataDownloadedGb ?? 0) + (n.DataUploadedGb ?? 0))
                    .First();
                snap.ActiveNetworkName = activeNet.AdapterName;
            }

            // Saúde e vida útil dos SSDs / Discos — pausado durante jogos; 300s na bandeja ou 60s com janela aberta
            int diskRefreshSec = backgroundMode ? 300 : 60;
            if (!gameMode && ((DateTime.UtcNow - _lastDiskRefreshUtc).TotalSeconds >= diskRefreshSec || _cachedDisks.Count == 0))
            {
                _cachedDisks = CaptureDiskHealth(lhmStorageSensors);
                _lastDiskRefreshUtc = DateTime.UtcNow;
            }
            snap.Disks = _cachedDisks;

            if (snap.StorageNames.Count == 0 && snap.Disks.Count > 0)
            {
                snap.StorageNames = snap.Disks.Select(d => d.Model).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            }

            var healthyVals = snap.Disks.Where(d => d.HealthPercent.HasValue).Select(d => d.HealthPercent.Value).ToList();
            if (healthyVals.Count > 0)
                snap.MinDiskHealthPercent = healthyVals.Min();

            // Potência (evitando dupla contagem — inclusive em iGPUs ja somadas dentro de CPU Package)
            snap.CpuWatts = SelectPrimaryCpuWatts(cpuPower);
            snap.GpuWatts = SelectPrimaryGpuWatts(gpuPower);
            snap.OtherWatts = SelectOtherWatts(otherPower);
            snap.ComponentsTotalWatts = ComputeComponentsTotalWatts(snap.CpuWatts, snap.GpuWatts, snap.OtherWatts, snap.GpuIsIntegrated);

            double eff = Math.Max(50.0, Math.Min(99.0, settings.PsuEfficiencyPercent)) / 100.0;
            double baseWatts = Math.Max(0.0, settings.BaseSystemWatts);
            snap.EstimatedWallWatts = (snap.ComponentsTotalWatts + baseWatts) / eff;

            // Temperaturas chave
            snap.CpuTempC = SelectPrimaryCpuTemp(cpuTemps);
            snap.GpuTempC = SelectPrimaryGpuTemp(gpuTemps);
            snap.GpuHotspotC = SelectGpuHotspot(gpuTemps);
            if (storageTemps.Count > 0)
                snap.MaxStorageTempC = storageTemps.Max(s => (double)s.Value);
            else
            {
                var diskTemps = snap.Disks.Where(d => d.TemperatureC.HasValue).Select(d => d.TemperatureC.Value).ToList();
                if (diskTemps.Count > 0) snap.MaxStorageTempC = diskTemps.Max();
            }

            // Uso %
            snap.CpuLoadPercent = SelectPrimaryLoad(cpuLoads, "CPU Total", "Total");
            snap.GpuLoadPercent = SelectPrimaryLoad(gpuLoads, "GPU Core", "D3D 3D");

            // RAM
            if (ramUsed.HasValue && ramAvail.HasValue)
            {
                double total = ramUsed.Value + ramAvail.Value;
                if (total > 0.1)
                {
                    snap.RamUsedGb = Math.Round(ramUsed.Value, 2);
                    snap.RamTotalGb = Math.Round(total, 1);
                    snap.RamLoadPercent = Math.Round((ramUsed.Value / total) * 100.0, 1);
                }
            }
            if (!snap.RamLoadPercent.HasValue)
            {
                var memLoad = snap.Loads.FirstOrDefault(l => l.HardwareType == "Memória" && l.SensorName.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0 && l.SensorName.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) < 0);
                if (memLoad != null) snap.RamLoadPercent = Math.Round(memLoad.Value, 1);
            }

            if (!snap.RamTotalGb.HasValue && _wmiTotalRamGb.HasValue)
            {
                snap.RamTotalGb = _wmiTotalRamGb;
            }

            // VRAM
            if (vramUsed.HasValue) snap.GpuVramUsedMb = Math.Round(vramUsed.Value, 0);
            if (vramTotal.HasValue) snap.GpuVramTotalMb = Math.Round(vramTotal.Value, 0);

            // Clocks chave
            if (cpuClocks.Count > 0)
                snap.CpuClockMhz = Math.Round(cpuClocks.Max(c => (double)c.Value), 0);
            if (gpuClocks.Count > 0)
            {
                var gCore = gpuClocks.FirstOrDefault(c => c.SensorName.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0);
                snap.GpuClockMhz = Math.Round((double)(gCore != null ? gCore.Value : gpuClocks.Max(c => c.Value)), 0);
            }

            // Fans chave
            var rpmFans = snap.Fans.Where(f => f.Unit == "RPM").ToList();
            if (rpmFans.Count > 0)
            {
                snap.MaxFanRpm = Math.Round(rpmFans.Max(f => (double)f.Value), 0);
                var gFan = rpmFans.FirstOrDefault(f => f.HardwareType == "GPU");
                if (gFan != null) snap.GpuFanRpm = Math.Round((double)gFan.Value, 0);
            }

            // Verifica tolerância ATX (±5%) nas voltagens da placa-mãe/PSU
            EvaluateAtxVoltages(snap);

            // Saúde da bateria (apenas se o PC for notebook com bateria)
            snap.Battery = CaptureBatteryHealth();

            // Top processos consumindo CPU/RAM agora (pulado em segundo plano na bandeja para economizar ~80% de CPU!)
            if (!backgroundMode)
            {
                snap.TopProcesses = _processMonitor.CaptureTop(8);
            }

            snap.Voltages = SortReadings(snap.Voltages);
            snap.PowerSensors = SortReadings(snap.PowerSensors);
            snap.Temperatures = SortReadings(snap.Temperatures);
            snap.Fans = SortReadings(snap.Fans);
            snap.Clocks = SortReadings(snap.Clocks);
            snap.Loads = SortReadings(snap.Loads);

            return snap;
        }

        private BatteryHealthInfo CaptureBatteryHealth()
        {
            try
            {
                var ps = System.Windows.Forms.SystemInformation.PowerStatus;
                if ((ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) != 0)
                    return null;

                var b = new BatteryHealthInfo
                {
                    Present = true,
                    ChargePercent = Math.Max(0, Math.Min(100, (int)Math.Round(ps.BatteryLifePercent * 100.0))),
                    IsCharging = (ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.Charging) != 0,
                    OnAcPower = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online
                };

                if (ps.BatteryLifeRemaining > 0)
                    b.RemainingMinutes = ps.BatteryLifeRemaining / 60;

                b.StatusText = b.IsCharging ? "Carregando" : (b.OnAcPower ? "Na Tomada (100%)" : "Na Bateria");

                // Complementa com sensores LHM de bateria se disponiveis
                foreach (var hw in _computer.Hardware)
                {
                    if (hw.HardwareType != HardwareType.Battery) continue;
                    foreach (var s in hw.Sensors)
                    {
                        if (!s.Value.HasValue || float.IsNaN(s.Value.Value)) continue;
                        if (s.SensorType == SensorType.Power && s.Value.Value > 0)
                            b.ChargeRateWatts = Math.Round(s.Value.Value, 1);
                        else if (s.SensorType == SensorType.Voltage && s.Value.Value > 0)
                            b.VoltageV = Math.Round(s.Value.Value, 2);
                        else if (s.SensorType == SensorType.Energy)
                        {
                            if (s.Name.IndexOf("Designed", StringComparison.OrdinalIgnoreCase) >= 0)
                                b.DesignCapacityMwh = Math.Round(s.Value.Value, 0);
                            else if (s.Name.IndexOf("Full", StringComparison.OrdinalIgnoreCase) >= 0)
                                b.FullChargeCapacityMwh = Math.Round(s.Value.Value, 0);
                        }
                    }
                }

                if (!b.DesignCapacityMwh.HasValue && _wmiBatteryDesignMwh.HasValue)
                    b.DesignCapacityMwh = Math.Round(_wmiBatteryDesignMwh.Value, 0);
                if (!b.FullChargeCapacityMwh.HasValue && _wmiBatteryFullMwh.HasValue)
                    b.FullChargeCapacityMwh = Math.Round(_wmiBatteryFullMwh.Value, 0);

                if (b.DesignCapacityMwh.HasValue && b.DesignCapacityMwh.Value > 0 && b.FullChargeCapacityMwh.HasValue)
                {
                    double health = Math.Max(0, Math.Min(100, (b.FullChargeCapacityMwh.Value / b.DesignCapacityMwh.Value) * 100.0));
                    b.HealthPercent = Math.Round(health, 1);
                    b.WearPercent = Math.Round(Math.Max(0, 100.0 - health), 1);
                }

                return b;
            }
            catch
            {
                return null;
            }
        }

        private static void CollectNetwork(IHardware hw, TelemetrySnapshot snap)
        {
            double dlKB = 0, ulKB = 0;
            double? dlGb = null, ulGb = null;

            foreach (var s in hw.Sensors)
            {
                if (!s.Value.HasValue || float.IsNaN(s.Value.Value)) continue;
                float val = s.Value.Value;
                if (s.SensorType == SensorType.Throughput)
                {
                    // LibreHardwareMonitor reporta Throughput em Bytes/s
                    if (s.Name.IndexOf("Download", StringComparison.OrdinalIgnoreCase) >= 0)
                        dlKB = Math.Max(0, val / 1024.0);
                    else if (s.Name.IndexOf("Upload", StringComparison.OrdinalIgnoreCase) >= 0)
                        ulKB = Math.Max(0, val / 1024.0);
                }
                else if (s.SensorType == SensorType.Data)
                {
                    if (s.Name.IndexOf("Downloaded", StringComparison.OrdinalIgnoreCase) >= 0)
                        dlGb = Math.Round(val, 2);
                    else if (s.Name.IndexOf("Uploaded", StringComparison.OrdinalIgnoreCase) >= 0)
                        ulGb = Math.Round(val, 2);
                }
            }

            snap.Networks.Add(new NetworkReading
            {
                AdapterName = hw.Name,
                DownloadKBps = Math.Round(dlKB, 1),
                UploadKBps = Math.Round(ulKB, 1),
                DataDownloadedGb = dlGb,
                DataUploadedGb = ulGb
            });
        }

        private static void EvaluateAtxVoltages(TelemetrySnapshot snap)
        {
            foreach (var v in snap.Voltages)
            {
                if (v.HardwareType != "Placa-mãe" && v.HardwareType != "Fonte (PSU)") continue;
                string name = (v.SensorName ?? "").ToLowerInvariant();
                double nominal = 0;
                if (name.Contains("+12v") || name == "12v") nominal = 12.0;
                else if (name.Contains("+5v") || name == "5v") nominal = 5.0;
                else if (name.Contains("+3.3v") || name.Contains("3.3v") || name == "3vcc" || name == "avcc") nominal = 3.3;

                if (nominal > 0)
                {
                    // Ignora sensores desconectados / ruído muito longe do nominal (< 50% do nominal)
                    if (v.Value < nominal * 0.5) continue;

                    double minOk = nominal * 0.95;
                    double maxOk = nominal * 1.05;
                    if (v.Value >= minOk && v.Value <= maxOk)
                    {
                        v.AtxStatus = "ok";
                    }
                    else
                    {
                        v.AtxStatus = "warn";
                        snap.AtxWarnings.Add(string.Format("{0}: {1:0.000}V (ATX ±5%: {2:0.00}V–{3:0.00}V)",
                            v.SensorName, v.Value, minOk, maxOk));
                    }
                }
            }
        }

        private List<DiskHealthInfo> CaptureDiskHealth(Dictionary<string, Dictionary<string, float>> lhmStorageSensors)
        {
            var result = new List<DiskHealthInfo>();
            try
            {
                var storages = StorageManager.Storages;
                if (storages == null || storages.Count == 0)
                {
                    StorageManager.ReloadStorages();
                    storages = StorageManager.Storages;
                }

                if (storages != null)
                {
                    foreach (var st in storages)
                    {
                        if (st == null) continue;
                        try
                        {
                            try { st.Update(); } catch { }
                            var info = new DiskHealthInfo
                            {
                                DriveNumber = st.DriveNumber,
                                Model = string.IsNullOrWhiteSpace(st.Model) ? ("Disco #" + st.DriveNumber) : st.Model.Trim(),
                                IsSsd = st.IsSSD,
                                IsNvme = st.IsNVMe,
                                TrimSupported = st.IsTrimSupported,
                                InterfaceType = st.IsNVMe ? "NVMe" : st.BusType.ToString(),
                                MediaType = st.IsNVMe ? "SSD NVMe" : (st.IsSSD ? "SSD SATA" : "HDD"),
                                Firmware = !string.IsNullOrWhiteSpace(st.FirmwareRev) ? st.FirmwareRev.Trim() : (st.Firmware ?? "").Trim(),
                                SerialNumber = (st.SerialNumber ?? "").Trim(),
                                TotalSizeGb = Math.Round(st.TotalSize / 1073741824.0, 1)
                            };

                            if (st.TotalFreeSize.HasValue && st.TotalSize > 0)
                            {
                                info.FreeSizeGb = Math.Round(st.TotalFreeSize.Value / 1073741824.0, 1);
                                double used = Math.Max(0, (double)st.TotalSize - (double)st.TotalFreeSize.Value);
                                info.UsedPercent = Math.Round((used / (double)st.TotalSize) * 100.0, 1);
                            }

                            if (st.Partitions != null && st.Partitions.Count > 0)
                            {
                                var letters = st.Partitions
                                    .Where(p => p != null && p.DriveLetter.HasValue && char.IsLetter(p.DriveLetter.Value))
                                    .Select(p => char.ToUpperInvariant(p.DriveLetter.Value) + ":")
                                    .Distinct()
                                    .ToList();
                                info.DriveLetters = string.Join(", ", letters);
                            }

                            var smart = st.Smart;
                            if (smart != null)
                            {
                                if (smart.Temperature.HasValue && smart.Temperature.Value > -10 && smart.Temperature.Value < 130)
                                    info.TemperatureC = smart.Temperature.Value;

                                if (smart.Life.HasValue && smart.Life.Value >= 0 && smart.Life.Value <= 100)
                                    info.HealthPercent = smart.Life.Value;

                                if (smart.HostWrites.HasValue && smart.HostWrites.Value > 0)
                                    info.TotalWrittenGb = smart.HostWrites.Value;

                                if (smart.HostReads.HasValue && smart.HostReads.Value > 0)
                                    info.TotalReadGb = smart.HostReads.Value;

                                info.PowerOnHours = smart.DetectedPowerOnHours > 0 ? smart.DetectedPowerOnHours : smart.MeasuredPowerOnHours;
                                info.PowerOnCount = smart.PowerOnCount;

                                switch (smart.DiskStatus)
                                {
                                    case DiskStatus.Good:
                                        info.HealthStatus = "Saudável";
                                        info.HealthCode = "good";
                                        break;
                                    case DiskStatus.Caution:
                                        info.HealthStatus = "Atenção";
                                        info.HealthCode = "caution";
                                        break;
                                    case DiskStatus.Bad:
                                        info.HealthStatus = "Crítico";
                                        info.HealthCode = "bad";
                                        break;
                                    default:
                                        info.HealthStatus = "Desconhecido";
                                        info.HealthCode = "unknown";
                                        break;
                                }

                                if (info.HealthCode == "unknown" && info.HealthPercent.HasValue)
                                {
                                    if (info.HealthPercent.Value >= 70) { info.HealthStatus = "Saudável"; info.HealthCode = "good"; }
                                    else if (info.HealthPercent.Value >= 30) { info.HealthStatus = "Atenção"; info.HealthCode = "caution"; }
                                    else { info.HealthStatus = "Crítico"; info.HealthCode = "bad"; }
                                }

                                Dictionary<string, float> lhmMap;
                                if (lhmStorageSensors.TryGetValue(info.Model, out lhmMap))
                                {
                                    if (!info.HealthPercent.HasValue)
                                    {
                                        float lifeVal;
                                        if (lhmMap.TryGetValue("Level:Remaining Life", out lifeVal) ||
                                            lhmMap.TryGetValue("Level:Available Spare", out lifeVal))
                                            info.HealthPercent = (int)Math.Round(Math.Max(0, Math.Min(100, lifeVal)));
                                        else if (lhmMap.TryGetValue("Level:Percentage Used", out lifeVal))
                                            info.HealthPercent = (int)Math.Round(Math.Max(0, Math.Min(100, 100f - lifeVal)));
                                    }
                                    if (!info.TemperatureC.HasValue)
                                    {
                                        float tVal;
                                        if (lhmMap.TryGetValue("Temperature:Temperature", out tVal))
                                            info.TemperatureC = Math.Round(tVal, 1);
                                    }
                                }

                                if (smart.SmartAttributes != null)
                                {
                                    foreach (var attr in smart.SmartAttributes)
                                    {
                                        if (attr == null) continue;
                                        byte id = attr.Info != null ? attr.Info.ID : attr.Attribute.ID;
                                        string attrName = (attr.Info != null && !string.IsNullOrWhiteSpace(attr.Info.Name))
                                            ? attr.Info.Name
                                            : ("Attr 0x" + id.ToString("X2"));
                                        info.SmartAttributes.Add(new DiskSmartItem
                                        {
                                            IdHex = string.Format("0x{0:X2}", id),
                                            Name = attrName,
                                            Current = attr.Attribute.CurrentValue,
                                            Worst = attr.Attribute.WorstValue,
                                            Threshold = attr.Attribute.Threshold,
                                            RawValue = attr.Attribute.RawValueULong.ToString()
                                        });
                                    }
                                }
                            }

                            result.Add(info);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return result.OrderBy(d => d.DriveNumber).ToList();
        }

        private void CollectHardware(
            IHardware hw,
            string parentName,
            HardwareType rootType,
            bool isPrimaryGpu,
            TelemetrySnapshot snap,
            List<SensorReading> cpuPower,
            List<SensorReading> gpuPower,
            List<SensorReading> otherPower,
            List<SensorReading> cpuTemps,
            List<SensorReading> gpuTemps,
            List<SensorReading> storageTemps,
            List<SensorReading> cpuClocks,
            List<SensorReading> gpuClocks,
            List<SensorReading> cpuLoads,
            List<SensorReading> gpuLoads,
            ref double? ramUsed,
            ref double? ramAvail,
            ref double? vramUsed,
            ref double? vramTotal)
        {
            string displayHw = hw.Name;
            if (!string.Equals(hw.Name, parentName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(parentName))
            {
                displayHw = parentName + " / " + hw.Name;
            }

            string hwCategory = FriendlyHardwareType(rootType);

            foreach (var sensor in hw.Sensors)
            {
                if (!sensor.Value.HasValue) continue;
                float val = sensor.Value.Value;
                if (float.IsNaN(val) || float.IsInfinity(val)) continue;

                switch (sensor.SensorType)
                {
                    case SensorType.Voltage:
                        snap.Voltages.Add(MakeReading(displayHw, hwCategory, sensor, "Voltagem", "V", val));
                        break;

                    case SensorType.Power:
                        if (val < 0 || val > 2500) break;
                        var pRead = MakeReading(displayHw, hwCategory, sensor, "Potência", "W", val);
                        snap.PowerSensors.Add(pRead);
                        if (rootType == HardwareType.Cpu) cpuPower.Add(pRead);
                        else if (IsGpu(rootType)) { if (isPrimaryGpu) gpuPower.Add(pRead); }
                        else if (rootType != HardwareType.Psu) otherPower.Add(pRead);
                        break;

                    case SensorType.Temperature:
                        if (val <= -20 || val >= 150) break;
                        // Ignora sensores de metadados que NAO sao temperatura atual (ex: Distance to TjMax, Warning/Critical Temperature)
                        if (sensor.Name.IndexOf("Distance to TjMax", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            sensor.Name.IndexOf("Warning Temperature", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            sensor.Name.IndexOf("Critical Temperature", StringComparison.OrdinalIgnoreCase) >= 0)
                            break;
                        var tRead = MakeReading(displayHw, hwCategory, sensor, "Temperatura", "°C", val);
                        snap.Temperatures.Add(tRead);
                        if (rootType == HardwareType.Cpu) cpuTemps.Add(tRead);
                        else if (IsGpu(rootType)) { if (isPrimaryGpu) gpuTemps.Add(tRead); }
                        else if (rootType == HardwareType.Storage) storageTemps.Add(tRead);
                        break;

                    case SensorType.Fan:
                        if (val < 0 || val > 20000) break;
                        snap.Fans.Add(MakeReading(displayHw, hwCategory, sensor, "Ventoinha", "RPM", val));
                        break;

                    case SensorType.Control:
                        if (val < 0 || val > 100) break;
                        snap.Fans.Add(MakeReading(displayHw, hwCategory, sensor, "Controle Fan", "%", val));
                        break;

                    case SensorType.Clock:
                        if (val <= 0 || val > 15000) break;
                        var cRead = MakeReading(displayHw, hwCategory, sensor, "Clock", "MHz", val);
                        snap.Clocks.Add(cRead);
                        if (rootType == HardwareType.Cpu && sensor.Name.IndexOf("Bus", StringComparison.OrdinalIgnoreCase) < 0)
                            cpuClocks.Add(cRead);
                        else if (IsGpu(rootType) && isPrimaryGpu)
                            gpuClocks.Add(cRead);
                        break;

                    case SensorType.Load:
                        if (val < 0 || val > 100.5f) break;
                        var lRead = MakeReading(displayHw, hwCategory, sensor, "Uso", "%", val);
                        if (rootType == HardwareType.Cpu)
                        {
                            cpuLoads.Add(lRead);
                            if (sensor.Name.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                sensor.Name.IndexOf("Max", StringComparison.OrdinalIgnoreCase) >= 0)
                                snap.Loads.Add(lRead);
                        }
                        else if (IsGpu(rootType))
                        {
                            if (isPrimaryGpu) gpuLoads.Add(lRead);
                            snap.Loads.Add(lRead);
                        }
                        else
                        {
                            snap.Loads.Add(lRead);
                        }
                        break;

                    case SensorType.Data:
                        if (rootType == HardwareType.Memory)
                        {
                            if (string.Equals(sensor.Name, "Memory Used", StringComparison.OrdinalIgnoreCase))
                                ramUsed = val;
                            else if (string.Equals(sensor.Name, "Memory Available", StringComparison.OrdinalIgnoreCase))
                                ramAvail = val;
                        }
                        break;

                    case SensorType.SmallData:
                        if (IsGpu(rootType) && isPrimaryGpu)
                        {
                            if (sensor.Name.IndexOf("Memory Used", StringComparison.OrdinalIgnoreCase) >= 0)
                                vramUsed = val;
                            else if (sensor.Name.IndexOf("Memory Total", StringComparison.OrdinalIgnoreCase) >= 0)
                                vramTotal = val;
                        }
                        break;
                }
            }

            foreach (var sub in hw.SubHardware)
            {
                CollectHardware(
                    sub, parentName, rootType, isPrimaryGpu, snap,
                    cpuPower, gpuPower, otherPower,
                    cpuTemps, gpuTemps, storageTemps,
                    cpuClocks, gpuClocks, cpuLoads, gpuLoads,
                    ref ramUsed, ref ramAvail, ref vramUsed, ref vramTotal);
            }
        }

        private static SensorReading MakeReading(string hwName, string hwCat, ISensor sensor, string typeLabel, string unit, float val)
        {
            return new SensorReading
            {
                HardwareName = hwName,
                HardwareType = hwCat,
                SensorName = sensor.Name,
                SensorType = typeLabel,
                Value = val,
                Min = sensor.Min ?? val,
                Max = sensor.Max ?? val,
                Unit = unit,
                AtxStatus = ""
            };
        }

        public enum GpuKind
        {
            Virtual = 0,
            Integrated = 1,
            Dedicated = 2
        }

        private static bool IsGpu(HardwareType type)
        {
            return type == HardwareType.GpuAmd || type == HardwareType.GpuNvidia || type == HardwareType.GpuIntel;
        }

        public static GpuKind ClassifyGpu(string name, HardwareType hwType = HardwareType.Motherboard)
        {
            if (string.IsNullOrWhiteSpace(name)) return GpuKind.Virtual;
            string n = name.ToLowerInvariant();

            // Adaptadores virtuais / remotos
            if (n.Contains("virtual") || n.Contains("basic display") || n.Contains("remote") ||
                n.Contains("parsec") || n.Contains("mumu") || n.Contains("vmware") ||
                n.Contains("vbox") || n.Contains("citrix") || n.Contains("spacedesk") ||
                n.Contains("idd") || n.Contains("rustdesk") || n.Contains("displaylink") ||
                n.Contains("miracast") || n.Contains("luminoncore"))
                return GpuKind.Virtual;

            // NVIDIA dedicada
            if (hwType == HardwareType.GpuNvidia ||
                n.Contains("nvidia") || n.Contains("geforce") || n.Contains("rtx") ||
                n.Contains("gtx") || n.Contains("quadro") || n.Contains("tesla") ||
                n.Contains(" gt ") || n.Contains("mx1") || n.Contains("mx2") ||
                n.Contains("mx3") || n.Contains("mx4") || n.Contains("mx5"))
                return GpuKind.Dedicated;

            // Intel (distingue Intel Arc dedicada de UHD/Iris/HD/Arc iGPU integrada)
            if (hwType == HardwareType.GpuIntel || n.Contains("intel"))
            {
                bool isDedicatedArc =
                    n.Contains("arc(tm) a") || n.Contains("arc a") ||
                    n.Contains("arc(tm) b") || n.Contains("arc b") ||
                    n.Contains("arc pro") || n.Contains("dg1") || n.Contains("dg2");
                return isDedicatedArc ? GpuKind.Dedicated : GpuKind.Integrated;
            }

            // AMD / Radeon (distingue dedicada RX/R9/R7/Pro/Fury/Vega 56/64/VII de Ryzen G / 680M / 780M / Vega 8 integrada)
            if (hwType == HardwareType.GpuAmd || n.Contains("amd") || n.Contains("radeon"))
            {
                bool isDedicatedAmd =
                    n.Contains(" rx ") || n.Contains("rx ") ||
                    n.Contains(" r9 ") || n.Contains(" r7 ") || n.Contains(" r5 ") ||
                    n.Contains("hd 7") || n.Contains("hd 8") || n.Contains("hd 6") || n.Contains("hd 5") ||
                    n.Contains("pro w") || n.Contains("pro wx") || n.Contains("firepro") ||
                    n.Contains("fury") || n.Contains("vega 56") || n.Contains("vega 64") ||
                    n.Contains("radeon vii") || n.Contains("instinct");
                return isDedicatedAmd ? GpuKind.Dedicated : GpuKind.Integrated;
            }

            return GpuKind.Integrated;
        }

        public static void SelectPrimaryAndSecondaryGpuNames(
            IEnumerable<string> gpuNames,
            out string primaryName,
            out bool primaryIsIntegrated,
            out string secondaryName)
        {
            primaryName = "GPU";
            primaryIsIntegrated = false;
            secondaryName = "";

            string dedicated = null;
            string integrated = null;
            string fallback = null;

            foreach (var raw in gpuNames)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string g = raw.Trim();
                if (fallback == null) fallback = g;
                var kind = ClassifyGpu(g);
                if (kind == GpuKind.Virtual) continue;
                if (kind == GpuKind.Dedicated && dedicated == null) dedicated = g;
                else if (kind == GpuKind.Integrated && integrated == null) integrated = g;
            }

            if (dedicated != null)
            {
                primaryName = dedicated;
                primaryIsIntegrated = false;
                if (integrated != null) secondaryName = integrated;
            }
            else if (integrated != null)
            {
                primaryName = integrated;
                primaryIsIntegrated = true;
            }
            else if (fallback != null)
            {
                primaryName = fallback;
                primaryIsIntegrated = false;
            }
        }

        public static double ComputeComponentsTotalWatts(double cpuWatts, double gpuWatts, double otherWatts, bool gpuIsIntegrated)
        {
            // Em GPUs integradas (Intel UHD/Iris, AMD Ryzen G), o consumo grafico ja esta embutido no CPU Package.
            // Evita dupla contagem quando CPU Package esta sendo lido (> 0.5W).
            double effectiveGpuWatts = (gpuIsIntegrated && cpuWatts > 0.5) ? 0.0 : gpuWatts;
            return cpuWatts + effectiveGpuWatts + otherWatts;
        }

        private static double SelectPrimaryCpuWatts(List<SensorReading> sensors)
        {
            if (sensors.Count == 0) return 0.0;
            var pkg = sensors.FirstOrDefault(s => s.SensorName.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0);
            if (pkg != null) return pkg.Value;
            var total = sensors.FirstOrDefault(s => s.SensorName.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0);
            if (total != null) return total.Value;
            var cores = sensors.FirstOrDefault(s => s.SensorName.IndexOf("Cores", StringComparison.OrdinalIgnoreCase) >= 0);
            if (cores != null) return cores.Value;
            return sensors.Max(s => s.Value);
        }

        private static double SelectPrimaryGpuWatts(List<SensorReading> sensors)
        {
            if (sensors.Count == 0) return 0.0;
            var pkg = sensors.FirstOrDefault(s =>
                s.SensorName.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.SensorName.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.SensorName.IndexOf("Board", StringComparison.OrdinalIgnoreCase) >= 0);
            if (pkg != null) return pkg.Value;
            var core = sensors.FirstOrDefault(s => s.SensorName.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0);
            if (core != null) return core.Value;
            return sensors.Max(s => s.Value);
        }

        private static double SelectOtherWatts(List<SensorReading> sensors)
        {
            if (sensors.Count == 0) return 0.0;
            return sensors
                .Where(s => s.SensorName.IndexOf("Platform", StringComparison.OrdinalIgnoreCase) < 0)
                .Sum(s => (double)s.Value);
        }

        private static double? SelectPrimaryCpuTemp(List<SensorReading> sensors)
        {
            if (sensors.Count == 0) return null;
            var preferred = sensors.FirstOrDefault(s =>
                s.SensorName.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.SensorName.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.SensorName.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0);
            if (preferred != null) return Math.Round(preferred.Value, 1);

            var avg = sensors.FirstOrDefault(s => s.SensorName.IndexOf("Average", StringComparison.OrdinalIgnoreCase) >= 0);
            if (avg != null) return Math.Round(avg.Value, 1);

            return Math.Round(sensors.Max(s => (double)s.Value), 1);
        }

        private static double? SelectPrimaryGpuTemp(List<SensorReading> sensors)
        {
            if (sensors.Count == 0) return null;
            var core = sensors.FirstOrDefault(s =>
                s.SensorName.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.SensorName.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) >= 0);
            if (core != null) return Math.Round(core.Value, 1);
            return Math.Round(sensors.First().Value, 1);
        }

        private static double? SelectGpuHotspot(List<SensorReading> sensors)
        {
            var hs = sensors.FirstOrDefault(s => s.SensorName.IndexOf("Hot Spot", StringComparison.OrdinalIgnoreCase) >= 0);
            return hs != null ? (double?)Math.Round(hs.Value, 1) : null;
        }

        private static double? SelectPrimaryLoad(List<SensorReading> sensors, string pref1, string pref2)
        {
            if (sensors.Count == 0) return null;
            var p1 = sensors.FirstOrDefault(s => s.SensorName.IndexOf(pref1, StringComparison.OrdinalIgnoreCase) >= 0);
            if (p1 != null) return Math.Round(p1.Value, 1);
            var p2 = sensors.FirstOrDefault(s => s.SensorName.IndexOf(pref2, StringComparison.OrdinalIgnoreCase) >= 0);
            if (p2 != null) return Math.Round(p2.Value, 1);
            return Math.Round(sensors.First().Value, 1);
        }

        private static List<SensorReading> SortReadings(List<SensorReading> list)
        {
            return list
                .OrderBy(r => GetHardwareSortOrder(r.HardwareType))
                .ThenBy(r => r.HardwareName)
                .ThenBy(r => r.SensorName)
                .ToList();
        }

        private static string FriendlyHardwareType(HardwareType type)
        {
            switch (type)
            {
                case HardwareType.Cpu: return "CPU";
                case HardwareType.GpuAmd:
                case HardwareType.GpuNvidia:
                case HardwareType.GpuIntel: return "GPU";
                case HardwareType.Motherboard:
                case HardwareType.SuperIO: return "Placa-mãe";
                case HardwareType.Memory: return "Memória";
                case HardwareType.Storage: return "Armazenamento";
                case HardwareType.Psu: return "Fonte (PSU)";
                default: return type.ToString();
            }
        }

        private static int GetHardwareSortOrder(string hwType)
        {
            switch (hwType)
            {
                case "CPU": return 0;
                case "GPU": return 1;
                case "Placa-mãe": return 2;
                case "Memória": return 3;
                case "Armazenamento": return 4;
                case "Fonte (PSU)": return 5;
                default: return 6;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _computer.Close(); } catch { }
        }

        private class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer)
            {
                computer.Traverse(this);
            }

            public void VisitHardware(IHardware hardware)
            {
                try
                {
                    hardware.Update();
                    foreach (IHardware subHardware in hardware.SubHardware)
                    {
                        subHardware.Accept(this);
                    }
                }
                catch { }
            }

            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }

        private class GameModeUpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer)
            {
                computer.Traverse(this);
            }

            public void VisitHardware(IHardware hardware)
            {
                try
                {
                    if (hardware.HardwareType != HardwareType.Cpu &&
                        hardware.HardwareType != HardwareType.GpuNvidia &&
                        hardware.HardwareType != HardwareType.GpuAmd &&
                        hardware.HardwareType != HardwareType.GpuIntel &&
                        hardware.HardwareType != HardwareType.Memory)
                    {
                        return;
                    }
                    hardware.Update();
                    foreach (IHardware subHardware in hardware.SubHardware)
                    {
                        subHardware.Accept(this);
                    }
                }
                catch { }
            }

            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }
    }
}
