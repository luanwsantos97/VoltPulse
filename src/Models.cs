using System;
using System.Collections.Generic;
using System.Globalization;

namespace PcPowerMonitor
{
    public class AppSettings
    {
        public double TariffPerKwh { get; set; }
        public string CurrencySymbol { get; set; }
        public string CurrencyCode { get; set; }
        public string CountryCode { get; set; }
        public double PsuEfficiencyPercent { get; set; }
        public double BaseSystemWatts { get; set; }
        public int PollIntervalSeconds { get; set; }
        public double TempWarningC { get; set; }
        public double TempCriticalC { get; set; }
        public bool MinimizeToTrayOnClose { get; set; }
        public bool StartWithWindows { get; set; }
        public string Theme { get; set; }          // dark, midnight, cyberpunk, emerald, amber, light
        public string StartView { get; set; }      // scan, monitor
        public string Language { get; set; }
        public bool EnableAlerts { get; set; }
        public double MonthlyBudgetAlert { get; set; }
        public bool ShowOverlay { get; set; }
        public int OverlayX { get; set; }
        public int OverlayY { get; set; }
        public bool OverlayVertical { get; set; }
        public bool OverlayMicroMode { get; set; }
        public bool OverlayAutoHide { get; set; }
        public double OverlayOpacity { get; set; }

        // v4.0 new settings
        public bool AutoGameMode { get; set; }
        public bool AutoOverlayInGame { get; set; }
        public bool QuietStart { get; set; }
        public string TrayIconMode { get; set; }   // logo, cpu_temp, watts
        public bool LocalApiEnabled { get; set; }
        public int LocalApiPort { get; set; }
        public bool AutoCheckUpdates { get; set; }

        // v4.1 Safe Eco Tuning & Ultra-Low Footprint
        public string TuningProfile { get; set; }      // stock, eco, ultra_eco, custom
        public bool AutoEcoOutsideGames { get; set; }
        public bool RestoreStockOnExit { get; set; }
        public bool UltraLowBackgroundMode { get; set; }

        // v4.2 Eco Engine Pro (Idle AFK + Thermal Guard + Custom Tuning Persistence)
        public bool AutoIdleEco { get; set; }
        public int AutoIdleMinutes { get; set; }
        public bool ThermalGuardEnabled { get; set; }
        public double ThermalGuardLimitC { get; set; }
        public int CustomCpuMaxPercent { get; set; }
        public int CustomCpuBoostMode { get; set; }
        public int CustomCpuEpp { get; set; }
        public bool CustomPassiveCooling { get; set; }
        public bool CustomPcieAspmEco { get; set; }
        public int CustomGpuPowerPercent { get; set; }

        public AppSettings()
        {
            ApplySystemLocaleDefaults();
            PsuEfficiencyPercent = 85.0;
            BaseSystemWatts = 30.0;
            PollIntervalSeconds = 2;
            TempWarningC = 75.0;
            TempCriticalC = 88.0;
            MinimizeToTrayOnClose = true;
            StartWithWindows = false;
            Theme = "dark";
            StartView = "scan";
            EnableAlerts = true;
            MonthlyBudgetAlert = 0.0;
            ShowOverlay = false;
            OverlayX = -1;
            OverlayY = -1;
            OverlayVertical = false;
            OverlayMicroMode = false;
            OverlayAutoHide = false;
            OverlayOpacity = 0.84;
            AutoGameMode = true;
            AutoOverlayInGame = false;
            QuietStart = true;
            TrayIconMode = "logo";
            LocalApiEnabled = false;
            LocalApiPort = 48731;
            AutoCheckUpdates = true;
            TuningProfile = "stock";
            AutoEcoOutsideGames = false;
            RestoreStockOnExit = true;
            UltraLowBackgroundMode = true;
            AutoIdleEco = false;
            AutoIdleMinutes = 10;
            ThermalGuardEnabled = true;
            ThermalGuardLimitC = 85.0;
            CustomCpuMaxPercent = 99;
            CustomCpuBoostMode = 3;
            CustomCpuEpp = 65;
            CustomPassiveCooling = false;
            CustomPcieAspmEco = false;
            CustomGpuPowerPercent = 85;
        }

        public void ApplySystemLocaleDefaults()
        {
            Language = DetectSystemLanguage();
            try
            {
                var region = RegionInfo.CurrentRegion;
                CountryCode = region.TwoLetterISORegionName;
                CurrencyCode = region.ISOCurrencySymbol;
                CurrencySymbol = string.IsNullOrWhiteSpace(region.CurrencySymbol) ? region.ISOCurrencySymbol : region.CurrencySymbol;
                TariffPerKwh = GetDefaultTariffForCurrency(CurrencyCode);
            }
            catch
            {
                CountryCode = "US";
                CurrencyCode = "USD";
                CurrencySymbol = "$";
                TariffPerKwh = 0.17;
            }
        }

        public static string DetectSystemLanguage()
        {
            string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            switch (two)
            {
                case "pt": return "pt-BR";
                case "es": return "es-ES";
                case "fr": return "fr-FR";
                case "de": return "de-DE";
                case "it": return "it-IT";
                case "ja": return "ja-JP";
                case "zh": return "zh-CN";
                default: return "en-US";
            }
        }

        public static string NormalizeLanguage(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return DetectSystemLanguage();
            string[] supported = new[] { "pt-BR", "en-US", "es-ES", "fr-FR", "de-DE", "it-IT", "ja-JP", "zh-CN" };
            foreach (var s in supported)
            {
                if (string.Equals(s, lang.Trim(), StringComparison.OrdinalIgnoreCase)) return s;
            }
            return DetectSystemLanguage();
        }

        public static string NormalizeTheme(string theme)
        {
            if (string.IsNullOrWhiteSpace(theme)) return "dark";
            string[] supported = new[] { "dark", "midnight", "cyberpunk", "emerald", "amber", "light" };
            foreach (var t in supported)
            {
                if (string.Equals(t, theme.Trim(), StringComparison.OrdinalIgnoreCase)) return t;
            }
            return "dark";
        }

        public static string NormalizeTrayMode(string mode)
        {
            if (string.Equals(mode, "cpu_temp", StringComparison.OrdinalIgnoreCase)) return "cpu_temp";
            if (string.Equals(mode, "watts", StringComparison.OrdinalIgnoreCase)) return "watts";
            return "logo";
        }

        public static string NormalizeStartView(string view)
        {
            if (string.Equals(view, "monitor", StringComparison.OrdinalIgnoreCase)) return "monitor";
            return "scan";
        }

        public static double GetDefaultTariffForCurrency(string isoCurrency)
        {
            if (string.IsNullOrWhiteSpace(isoCurrency)) return 0.20;
            switch (isoCurrency.ToUpperInvariant())
            {
                case "BRL": return 0.85;
                case "USD": return 0.17;
                case "EUR": return 0.30;
                case "GBP": return 0.26;
                case "CAD": return 0.15;
                case "AUD": return 0.32;
                case "CHF": return 0.32;
                case "JPY": return 31.0;
                case "CNY": return 0.55;
                case "INR": return 7.50;
                case "MXN": return 2.20;
                case "ARS": return 120.0;
                case "CLP": return 180.0;
                case "COP": return 850.0;
                case "PEN": return 0.75;
                case "KRW": return 160.0;
                case "PLN": return 1.10;
                case "TRY": return 2.60;
                case "RUB": return 6.00;
                case "ZAR": return 3.20;
                default: return 0.20;
            }
        }
    }

    public class SensorReading
    {
        public string HardwareName { get; set; }
        public string HardwareType { get; set; }
        public string SensorName { get; set; }
        public string SensorType { get; set; }
        public float Value { get; set; }
        public float Min { get; set; }
        public float Max { get; set; }
        public string Unit { get; set; }
        public string AtxStatus { get; set; }
    }

    public class NetworkReading
    {
        public string AdapterName { get; set; }
        public double DownloadKBps { get; set; }
        public double UploadKBps { get; set; }
        public double? DataDownloadedGb { get; set; }
        public double? DataUploadedGb { get; set; }
    }

    public class ProcessUsageInfo
    {
        public int Pid { get; set; }
        public string Name { get; set; }
        public double CpuPercent { get; set; }
        public double RamMb { get; set; }
    }

    public class BatteryHealthInfo
    {
        public bool Present { get; set; }
        public int ChargePercent { get; set; }
        public bool IsCharging { get; set; }
        public bool OnAcPower { get; set; }
        public string StatusText { get; set; }
        public int? RemainingMinutes { get; set; }
        public double? ChargeRateWatts { get; set; }
        public double? VoltageV { get; set; }
        public double? DesignCapacityMwh { get; set; }
        public double? FullChargeCapacityMwh { get; set; }
        public double? HealthPercent { get; set; }
        public double? WearPercent { get; set; }
    }

    public class GameModeStatus
    {
        public bool Active { get; set; }
        public int ProcessId { get; set; }
        public string GameName { get; set; }
        public string ProcessName { get; set; }
        public bool QuietActive { get; set; }
    }

    public class UpdateInfo
    {
        public bool Checked { get; set; }
        public bool UpdateAvailable { get; set; }
        public string LatestVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string AssetFileName { get; set; }
        public string Sha256SumsUrl { get; set; }
        public string ReleasePageUrl { get; set; }
    }

    public class DiskSmartItem
    {
        public string IdHex { get; set; }
        public string Name { get; set; }
        public int Current { get; set; }
        public int Worst { get; set; }
        public int Threshold { get; set; }
        public string RawValue { get; set; }
    }

    public class DiskHealthInfo
    {
        public int DriveNumber { get; set; }
        public string Model { get; set; }
        public string DriveLetters { get; set; }
        public string MediaType { get; set; }
        public string InterfaceType { get; set; }
        public string Firmware { get; set; }
        public string SerialNumber { get; set; }
        public bool IsSsd { get; set; }
        public bool IsNvme { get; set; }
        public bool TrimSupported { get; set; }

        public double TotalSizeGb { get; set; }
        public double? FreeSizeGb { get; set; }
        public double? UsedPercent { get; set; }

        public int? HealthPercent { get; set; }
        public string HealthStatus { get; set; }
        public string HealthCode { get; set; }
        public double? TemperatureC { get; set; }

        public double? TotalWrittenGb { get; set; }
        public double? TotalReadGb { get; set; }
        public ulong PowerOnHours { get; set; }
        public ulong PowerOnCount { get; set; }

        public List<DiskSmartItem> SmartAttributes { get; set; }

        public DiskHealthInfo()
        {
            SmartAttributes = new List<DiskSmartItem>();
        }
    }

    public class SessionStats
    {
        public bool Active { get; set; }
        public bool HasLastSession { get; set; }
        public bool AutoStarted { get; set; }
        public string Name { get; set; }
        public string StartedAt { get; set; }
        public double ElapsedSeconds { get; set; }
        public double EnergyWh { get; set; }
        public double PeakWatts { get; set; }
        public double PeakCpuTempC { get; set; }
        public double PeakGpuTempC { get; set; }

        public double EnergyKwh { get { return EnergyWh / 1000.0; } }
        public double AverageWatts { get { return ElapsedSeconds > 0 ? (EnergyWh * 3600.0) / ElapsedSeconds : 0.0; } }
    }

    public class TelemetrySnapshot
    {
        public string TimestampIso { get; set; }
        public string TimeLabel { get; set; }
        public bool IsAdmin { get; set; }
        public bool PawnIoInstalled { get; set; }
        public bool Paused { get; set; }

        public string CpuName { get; set; }
        public string GpuName { get; set; }
        public bool GpuIsIntegrated { get; set; }
        public string SecondaryGpuName { get; set; }
        public string MotherboardName { get; set; }
        public List<string> StorageNames { get; set; }

        public double CpuWatts { get; set; }
        public double GpuWatts { get; set; }
        public double OtherWatts { get; set; }
        public double ComponentsTotalWatts { get; set; }
        public double EstimatedWallWatts { get; set; }

        public double? CpuTempC { get; set; }
        public double? GpuTempC { get; set; }
        public double? GpuHotspotC { get; set; }
        public double? MaxStorageTempC { get; set; }
        public int? MinDiskHealthPercent { get; set; }

        public double? CpuLoadPercent { get; set; }
        public double? GpuLoadPercent { get; set; }
        public double? RamLoadPercent { get; set; }
        public double? RamUsedGb { get; set; }
        public double? RamTotalGb { get; set; }
        public double? GpuVramUsedMb { get; set; }
        public double? GpuVramTotalMb { get; set; }

        public double? CpuClockMhz { get; set; }
        public double? GpuClockMhz { get; set; }
        public double? GpuFanRpm { get; set; }
        public double? MaxFanRpm { get; set; }
        public int? CurrentFps { get; set; }

        public double NetDownloadKBps { get; set; }
        public double NetUploadKBps { get; set; }
        public string ActiveNetworkName { get; set; }
        public List<NetworkReading> Networks { get; set; }

        public List<string> AtxWarnings { get; set; }
        public BatteryHealthInfo Battery { get; set; }
        public List<ProcessUsageInfo> TopProcesses { get; set; }

        public List<SensorReading> PowerSensors { get; set; }
        public List<SensorReading> Voltages { get; set; }
        public List<SensorReading> Temperatures { get; set; }
        public List<SensorReading> Fans { get; set; }
        public List<SensorReading> Clocks { get; set; }
        public List<SensorReading> Loads { get; set; }
        public List<DiskHealthInfo> Disks { get; set; }

        public string StatusNote { get; set; }

        public TelemetrySnapshot()
        {
            StorageNames = new List<string>();
            Networks = new List<NetworkReading>();
            AtxWarnings = new List<string>();
            TopProcesses = new List<ProcessUsageInfo>();
            PowerSensors = new List<SensorReading>();
            Voltages = new List<SensorReading>();
            Temperatures = new List<SensorReading>();
            Fans = new List<SensorReading>();
            Clocks = new List<SensorReading>();
            Loads = new List<SensorReading>();
            Disks = new List<DiskHealthInfo>();
        }
    }

    public class DailyEnergyRecord
    {
        public string Date { get; set; }
        public double EnergyWh { get; set; }
        public double ActiveSeconds { get; set; }
        public double PeakWatts { get; set; }
        public long SampleCount { get; set; }

        public double EnergyKwh { get { return EnergyWh / 1000.0; } }
        public double AverageWatts { get { return ActiveSeconds > 0 ? (EnergyWh * 3600.0) / ActiveSeconds : 0.0; } }
    }

    public class EnergySummary
    {
        public double TodayKwh { get; set; }
        public double TodayCost { get; set; }
        public double TodayActiveHours { get; set; }
        public double TodayPeakWatts { get; set; }
        public double TodayAvgWatts { get; set; }

        public double WeekKwh { get; set; }
        public double WeekCost { get; set; }
        public double WeekAvgDailyKwh { get; set; }
        public int WeekDaysRecorded { get; set; }

        public double MonthKwh { get; set; }
        public double MonthCost { get; set; }
        public double ProjectedMonthKwh { get; set; }
        public double ProjectedMonthCost { get; set; }
        public int MonthDaysRecorded { get; set; }

        public double[] HourlyWhToday { get; set; }
        public List<DailyEnergyRecord> Last14Days { get; set; }
        public List<DailyEnergyRecord> AllDays { get; set; }

        public EnergySummary()
        {
            HourlyWhToday = new double[24];
            Last14Days = new List<DailyEnergyRecord>();
            AllDays = new List<DailyEnergyRecord>();
        }
    }

    public class DashboardPayload
    {
        public string Type { get; set; }
        public TelemetrySnapshot Telemetry { get; set; }
        public EnergySummary Summary { get; set; }
        public SessionStats Session { get; set; }
        public GameModeStatus GameMode { get; set; }
        public PowerTuningStatus Tuning { get; set; }
        public UpdateInfo Update { get; set; }
        public AppSettings Settings { get; set; }
        public double AppMemoryMb { get; set; }
        public bool IsMaximized { get; set; }
        public bool LocalApiRunning { get; set; }
        public string DataDirectory { get; set; }
        public string AppVersion { get; set; }
    }
}
