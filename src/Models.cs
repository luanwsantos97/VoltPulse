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
        public string Theme { get; set; }
        public string Language { get; set; }
        public bool EnableAlerts { get; set; }
        public double MonthlyBudgetAlert { get; set; }
        public bool ShowOverlay { get; set; }
        public int OverlayX { get; set; }
        public int OverlayY { get; set; }

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
            EnableAlerts = true;
            MonthlyBudgetAlert = 0.0;
            ShowOverlay = false;
            OverlayX = -1;
            OverlayY = -1;
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
        public string AtxStatus { get; set; } // "", "ok", "warn"
    }

    public class NetworkReading
    {
        public string AdapterName { get; set; }
        public double DownloadKBps { get; set; }
        public double UploadKBps { get; set; }
        public double? DataDownloadedGb { get; set; }
        public double? DataUploadedGb { get; set; }
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

        // Hardware Identity
        public string CpuName { get; set; }
        public string GpuName { get; set; }
        public string MotherboardName { get; set; }
        public List<string> StorageNames { get; set; }

        // Power (W)
        public double CpuWatts { get; set; }
        public double GpuWatts { get; set; }
        public double OtherWatts { get; set; }
        public double ComponentsTotalWatts { get; set; }
        public double EstimatedWallWatts { get; set; }

        // Thermal / Load / Clock / Fan / Network
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

        public double NetDownloadKBps { get; set; }
        public double NetUploadKBps { get; set; }
        public string ActiveNetworkName { get; set; }
        public List<NetworkReading> Networks { get; set; }

        // ATX Voltage health warnings
        public List<string> AtxWarnings { get; set; }

        // Detailed sensor tables
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
        public AppSettings Settings { get; set; }
        public string DataDirectory { get; set; }
        public string AppVersion { get; set; }
    }
}
