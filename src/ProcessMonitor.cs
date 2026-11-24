using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PcPowerMonitor
{
    public class ProcessMonitor
    {
        private readonly Dictionary<int, TimeSpan> _prevCpuTimes = new Dictionary<int, TimeSpan>();
        private DateTime _lastSampleUtc = DateTime.MinValue;
        private List<ProcessUsageInfo> _cached = new List<ProcessUsageInfo>();

        public List<ProcessUsageInfo> CaptureTop(int topCount = 8)
        {
            DateTime now = DateTime.UtcNow;
            double elapsedMs = (now - _lastSampleUtc).TotalMilliseconds;

            // Atualiza no maximo a cada 2.5s para manter CPU baixissima
            if (_lastSampleUtc != DateTime.MinValue && elapsedMs < 2400 && _cached.Count > 0)
                return _cached;

            var nextTimes = new Dictionary<int, TimeSpan>();
            var list = new List<ProcessUsageInfo>();
            int cores = Math.Max(1, Environment.ProcessorCount);

            Process[] procs = null;
            try { procs = Process.GetProcesses(); } catch { return _cached; }

            foreach (var p in procs)
            {
                try
                {
                    if (p.Id == 0) continue;
                    TimeSpan curCpu = p.TotalProcessorTime;
                    nextTimes[p.Id] = curCpu;

                    double cpuPct = 0.0;
                    TimeSpan prevCpu;
                    if (_lastSampleUtc != DateTime.MinValue && elapsedMs > 100 && _prevCpuTimes.TryGetValue(p.Id, out prevCpu))
                    {
                        double deltaMs = (curCpu - prevCpu).TotalMilliseconds;
                        if (deltaMs > 0)
                        {
                            cpuPct = Math.Min(100.0, (deltaMs / (elapsedMs * cores)) * 100.0);
                        }
                    }

                    double ramMb = p.WorkingSet64 / 1048576.0;
                    list.Add(new ProcessUsageInfo
                    {
                        Pid = p.Id,
                        Name = p.ProcessName,
                        CpuPercent = Math.Round(cpuPct, 1),
                        RamMb = Math.Round(ramMb, 0)
                    });
                }
                catch { }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }

            _prevCpuTimes.Clear();
            foreach (var kv in nextTimes) _prevCpuTimes[kv.Key] = kv.Value;
            _lastSampleUtc = now;

            // Agrupa processos multi-instancia (ex: chrome, msedge) para uma visao limpa
            var grouped = list
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ProcessUsageInfo
                {
                    Pid = g.First().Pid,
                    Name = g.Key + (g.Count() > 1 ? " (" + g.Count() + ")" : ""),
                    CpuPercent = Math.Round(Math.Min(100.0, g.Sum(x => x.CpuPercent)), 1),
                    RamMb = Math.Round(g.Sum(x => x.RamMb), 0)
                })
                .OrderByDescending(x => x.CpuPercent >= 1.0 ? x.CpuPercent * 10.0 : 0.0)
                .ThenByDescending(x => x.RamMb)
                .Take(topCount)
                .ToList();

            _cached = grouped;
            return _cached;
        }
    }
}
