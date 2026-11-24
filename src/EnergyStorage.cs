using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace PcPowerMonitor
{
    public class EnergyStorage
    {
        private readonly string _dataDir;
        private readonly string _settingsPath;
        private readonly string _historyPath;
        private readonly string _hourlyPath;
        private readonly Dictionary<string, DailyEnergyRecord> _records = new Dictionary<string, DailyEnergyRecord>();
        private readonly object _lock = new object();

        private string _hourlyDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private double[] _hourlyWh = new double[24];

        // Medidor de sessão / jogo
        private SessionStats _session = new SessionStats();

        private DateTime _lastFlush = DateTime.MinValue;
        private bool _dirty;

        public string DataDirectory { get { return _dataDir; } }
        public string HistoryFilePath { get { return _historyPath; } }

        public EnergyStorage()
        {
            _dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PcPowerMonitor");
            Directory.CreateDirectory(_dataDir);
            _settingsPath = Path.Combine(_dataDir, "settings.json");
            _historyPath = Path.Combine(_dataDir, "energy_history.csv");
            _hourlyPath = Path.Combine(_dataDir, "hourly_today.json");

            LoadHistory();
            LoadHourlyToday();
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                    var ser = new JavaScriptSerializer();
                    var loaded = ser.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        loaded.TariffPerKwh = Math.Max(0.01, loaded.TariffPerKwh);
                        loaded.CurrencySymbol = string.IsNullOrWhiteSpace(loaded.CurrencySymbol) ? "R$" : loaded.CurrencySymbol.Trim();
                        loaded.PsuEfficiencyPercent = Math.Max(50.0, Math.Min(99.0, loaded.PsuEfficiencyPercent));
                        loaded.BaseSystemWatts = Math.Max(0.0, loaded.BaseSystemWatts);
                        loaded.PollIntervalSeconds = Math.Max(1, Math.Min(60, loaded.PollIntervalSeconds));
                        loaded.TempWarningC = loaded.TempWarningC <= 0 ? 75.0 : Math.Max(40.0, Math.Min(100.0, loaded.TempWarningC));
                        loaded.TempCriticalC = loaded.TempCriticalC <= 0 ? 88.0 : Math.Max(loaded.TempWarningC + 5.0, Math.Min(110.0, loaded.TempCriticalC));
                        loaded.Theme = string.Equals(loaded.Theme, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
                        if (string.IsNullOrWhiteSpace(loaded.Language) || (loaded.Language != "pt-BR" && loaded.Language != "en-US"))
                        {
                            loaded.Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase)
                                ? "pt-BR" : "en-US";
                        }
                        loaded.MonthlyBudgetAlert = Math.Max(0.0, loaded.MonthlyBudgetAlert);
                        return loaded;
                    }
                }
            }
            catch { }

            return new AppSettings();
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var ser = new JavaScriptSerializer();
                string json = ser.Serialize(settings);
                string tmp = _settingsPath + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                if (File.Exists(_settingsPath))
                    File.Delete(_settingsPath);
                File.Move(tmp, _settingsPath);
            }
            catch { }

            ApplyStartWithWindows(settings.StartWithWindows);
        }

        /// <summary>
        /// Usa o Agendador de Tarefas do Windows (RunLevel HIGHEST) para iniciar sem pop-up de UAC no boot!
        /// </summary>
        public static void ApplyStartWithWindows(bool enable)
        {
            // Limpa entradas antigas no Registro Run (que barravam no UAC)
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        key.DeleteValue("PcPowerMonitor", false);
                        key.DeleteValue("VoltPulsePowerMonitor", false);
                    }
                }
            }
            catch { }

            const string taskName = "VoltPulsePowerMonitor";
            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string args = enable
                    ? string.Format("/Create /TN \"{0}\" /TR \"\\\"{1}\\\" --minimized\" /SC ONLOGON /RL HIGHEST /F", taskName, exePath)
                    : string.Format("/Delete /TN \"{0}\" /F", taskName);

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc != null) proc.WaitForExit(4000);
                }
            }
            catch { }
        }

        public void StartSession(string name)
        {
            lock (_lock)
            {
                _session = new SessionStats
                {
                    Active = true,
                    HasLastSession = true,
                    Name = string.IsNullOrWhiteSpace(name) ? "Sessão" : name.Trim(),
                    StartedAt = DateTime.Now.ToString("HH:mm:ss"),
                    ElapsedSeconds = 0,
                    EnergyWh = 0,
                    PeakWatts = 0,
                    PeakCpuTempC = 0,
                    PeakGpuTempC = 0
                };
            }
        }

        public void StopSession()
        {
            lock (_lock)
            {
                _session.Active = false;
            }
        }

        public SessionStats GetSessionSnapshot()
        {
            lock (_lock)
            {
                return new SessionStats
                {
                    Active = _session.Active,
                    HasLastSession = _session.HasLastSession,
                    Name = _session.Name,
                    StartedAt = _session.StartedAt,
                    ElapsedSeconds = _session.ElapsedSeconds,
                    EnergyWh = _session.EnergyWh,
                    PeakWatts = _session.PeakWatts,
                    PeakCpuTempC = _session.PeakCpuTempC,
                    PeakGpuTempC = _session.PeakGpuTempC
                };
            }
        }

        public void RecordSample(DateTime timestamp, double wallWatts, double elapsedSeconds, double? cpuTempC, double? gpuTempC)
        {
            if (elapsedSeconds <= 0) return;
            if (elapsedSeconds > 15.0) elapsedSeconds = 2.0;

            string dayKey = timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            double deltaWh = wallWatts * (elapsedSeconds / 3600.0);

            lock (_lock)
            {
                DailyEnergyRecord rec;
                if (!_records.TryGetValue(dayKey, out rec))
                {
                    rec = new DailyEnergyRecord { Date = dayKey };
                    _records[dayKey] = rec;
                }

                rec.EnergyWh += deltaWh;
                rec.ActiveSeconds += elapsedSeconds;
                if (wallWatts > rec.PeakWatts)
                    rec.PeakWatts = wallWatts;
                rec.SampleCount++;

                // Consumo por hora de hoje
                if (_hourlyDate != dayKey)
                {
                    _hourlyDate = dayKey;
                    _hourlyWh = new double[24];
                }
                int hr = Math.Max(0, Math.Min(23, timestamp.Hour));
                _hourlyWh[hr] += deltaWh;

                // Sessão ativa
                if (_session.Active)
                {
                    _session.ElapsedSeconds += elapsedSeconds;
                    _session.EnergyWh += deltaWh;
                    if (wallWatts > _session.PeakWatts) _session.PeakWatts = wallWatts;
                    if (cpuTempC.HasValue && cpuTempC.Value > _session.PeakCpuTempC) _session.PeakCpuTempC = cpuTempC.Value;
                    if (gpuTempC.HasValue && gpuTempC.Value > _session.PeakGpuTempC) _session.PeakGpuTempC = gpuTempC.Value;
                }

                _dirty = true;
            }

            if ((DateTime.UtcNow - _lastFlush).TotalSeconds >= 20)
            {
                Flush();
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                if (!_dirty && File.Exists(_historyPath)) return;
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Date,EnergyWh,ActiveSeconds,PeakWatts,SampleCount");
                    foreach (var rec in _records.Values.OrderBy(r => r.Date))
                    {
                        sb.AppendFormat(
                            CultureInfo.InvariantCulture,
                            "{0},{1:F4},{2:F2},{3:F2},{4}\r\n",
                            rec.Date, rec.EnergyWh, rec.ActiveSeconds, rec.PeakWatts, rec.SampleCount);
                    }

                    string tmp = _historyPath + ".tmp";
                    File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                    if (File.Exists(_historyPath))
                        File.Delete(_historyPath);
                    File.Move(tmp, _historyPath);

                    // Salvar consumo por hora de hoje
                    var ser = new JavaScriptSerializer();
                    var hObj = new Dictionary<string, object>
                    {
                        { "date", _hourlyDate },
                        { "hours", _hourlyWh }
                    };
                    string hTmp = _hourlyPath + ".tmp";
                    File.WriteAllText(hTmp, ser.Serialize(hObj), Encoding.UTF8);
                    if (File.Exists(_hourlyPath))
                        File.Delete(_hourlyPath);
                    File.Move(hTmp, _hourlyPath);

                    _dirty = false;
                    _lastFlush = DateTime.UtcNow;
                }
                catch { }
            }
        }

        public void ClearHistory()
        {
            lock (_lock)
            {
                _records.Clear();
                _hourlyWh = new double[24];
                _dirty = true;
            }
            Flush();
        }

        public EnergySummary GetSummary(double tariffPerKwh)
        {
            var summary = new EnergySummary();
            DateTime today = DateTime.Today;
            string todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string monthPrefix = today.ToString("yyyy-MM-", CultureInfo.InvariantCulture);

            lock (_lock)
            {
                if (_hourlyDate == todayKey)
                {
                    Array.Copy(_hourlyWh, summary.HourlyWhToday, 24);
                }

                DailyEnergyRecord todayRec;
                if (_records.TryGetValue(todayKey, out todayRec))
                {
                    summary.TodayKwh = todayRec.EnergyKwh;
                    summary.TodayCost = summary.TodayKwh * tariffPerKwh;
                    summary.TodayActiveHours = todayRec.ActiveSeconds / 3600.0;
                    summary.TodayPeakWatts = todayRec.PeakWatts;
                    summary.TodayAvgWatts = todayRec.AverageWatts;
                }

                for (int i = 13; i >= 0; i--)
                {
                    DateTime d = today.AddDays(-i);
                    string k = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    DailyEnergyRecord rec;
                    if (_records.TryGetValue(k, out rec))
                    {
                        summary.Last14Days.Add(new DailyEnergyRecord
                        {
                            Date = rec.Date,
                            EnergyWh = rec.EnergyWh,
                            ActiveSeconds = rec.ActiveSeconds,
                            PeakWatts = rec.PeakWatts,
                            SampleCount = rec.SampleCount
                        });
                        if (i < 7)
                        {
                            summary.WeekKwh += rec.EnergyKwh;
                            if (rec.ActiveSeconds > 0) summary.WeekDaysRecorded++;
                        }
                    }
                    else
                    {
                        summary.Last14Days.Add(new DailyEnergyRecord { Date = k });
                    }
                }

                summary.WeekCost = summary.WeekKwh * tariffPerKwh;
                summary.WeekAvgDailyKwh = summary.WeekDaysRecorded > 0
                    ? summary.WeekKwh / summary.WeekDaysRecorded
                    : 0.0;

                foreach (var rec in _records.Values.Where(r => r.Date.StartsWith(monthPrefix)))
                {
                    summary.MonthKwh += rec.EnergyKwh;
                    if (rec.ActiveSeconds > 0) summary.MonthDaysRecorded++;
                }

                summary.MonthCost = summary.MonthKwh * tariffPerKwh;

                int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
                if (summary.MonthDaysRecorded > 0)
                {
                    double avgDailyMonth = summary.MonthKwh / summary.MonthDaysRecorded;
                    summary.ProjectedMonthKwh = avgDailyMonth * daysInMonth;
                    summary.ProjectedMonthCost = summary.ProjectedMonthKwh * tariffPerKwh;
                }

                summary.AllDays = _records.Values
                    .OrderByDescending(r => r.Date)
                    .Select(r => new DailyEnergyRecord
                    {
                        Date = r.Date,
                        EnergyWh = r.EnergyWh,
                        ActiveSeconds = r.ActiveSeconds,
                        PeakWatts = r.PeakWatts,
                        SampleCount = r.SampleCount
                    })
                    .ToList();
            }

            return summary;
        }

        private void LoadHistory()
        {
            if (!File.Exists(_historyPath)) return;
            try
            {
                var lines = File.ReadAllLines(_historyPath, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var parts = lines[i].Split(',');
                    if (parts.Length < 5) continue;

                    double wh, sec, peak;
                    long samples;
                    if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out wh) &&
                        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out sec) &&
                        double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out peak) &&
                        long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out samples))
                    {
                        _records[parts[0]] = new DailyEnergyRecord
                        {
                            Date = parts[0],
                            EnergyWh = wh,
                            ActiveSeconds = sec,
                            PeakWatts = peak,
                            SampleCount = samples
                        };
                    }
                }
            }
            catch { }
        }

        private void LoadHourlyToday()
        {
            if (!File.Exists(_hourlyPath)) return;
            try
            {
                string json = File.ReadAllText(_hourlyPath, Encoding.UTF8);
                var ser = new JavaScriptSerializer();
                var dict = ser.Deserialize<Dictionary<string, object>>(json);
                string todayKey = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (dict != null && dict.ContainsKey("date") && Convert.ToString(dict["date"]) == todayKey && dict.ContainsKey("hours"))
                {
                    var arr = dict["hours"] as System.Collections.ArrayList;
                    if (arr != null && arr.Count == 24)
                    {
                        _hourlyDate = todayKey;
                        for (int i = 0; i < 24; i++)
                        {
                            _hourlyWh[i] = Convert.ToDouble(arr[i], CultureInfo.InvariantCulture);
                        }
                    }
                }
            }
            catch { }
        }
    }
}
