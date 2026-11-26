using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PcPowerMonitor
{
    /// <summary>
    /// Monitor de FPS 100% seguro e leve baseado em ETW oficial do Windows (Microsoft-Windows-DXGI + D3D9).
    /// - ZERO injecao de DLL ou hook no processo do jogo (100% seguro com qualquer anti-cheat).
    /// - ZERO custo de CPU/RAM fora de jogos: a sessao ETW so e aberta quando um jogo esta ativo e o Mini Widget visivel.
    /// - Ring buffer fixo sem alocacoes por frame.
    /// </summary>
    public class FpsMonitor : IDisposable
    {
        private const string SessionName = "VoltPulseFpsEtw";

        // Microsoft-Windows-DXGI (DX10 / DX11 / DX12 / Vulkan DXGI / OpenGL DXGI)
        private static readonly Guid DxgiProviderGuid = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        // Microsoft-Windows-D3D9 (jogos DX9 classicos)
        private static readonly Guid D3d9ProviderGuid = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        // Microsoft-Windows-DxgKrnl (Kernel grafico universal: DX11/DX12/Vulkan/OpenGL/MPO — usado como fallback sem dupla contagem)
        private static readonly Guid DxgKrnlProviderGuid = new Guid("802EC45A-1E99-4B83-9920-87C98277BA9D");

        private const int DxgiPresentStart = 42;
        private const int DxgiPresentMpoStart = 55;
        private const int DxgiPresent1Start = 64;
        private const int D3d9PresentStart = 1;
        private const int DxgKrnlPresentStart = 184;
        private const ulong DxgKrnlBaseKeyword = 0x0000000000000040UL;

        private const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
        private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
        private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        private const uint EVENT_TRACE_CONTROL_STOP = 1;
        private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_ALREADY_EXISTS = 183;
        private static readonly ulong INVALID_PROCESSTRACE_HANDLE = unchecked((ulong)-1);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void EventRecordCallback(IntPtr eventRecord);

        [StructLayout(LayoutKind.Sequential)]
        private struct WNODE_HEADER
        {
            public uint BufferSize;
            public uint ProviderId;
            public ulong HistoricalContext;
            public long TimeStamp;
            public Guid Guid;
            public uint ClientContext;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE_PROPERTIES
        {
            public WNODE_HEADER Wnode;
            public uint BufferSize;
            public uint MinimumBuffers;
            public uint MaximumBuffers;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint FlushTimer;
            public uint EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers;
            public uint FreeBuffers;
            public uint EventsLost;
            public uint BuffersWritten;
            public uint LogBuffersLost;
            public uint RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset;
            public uint LoggerNameOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public short wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TIME_ZONE_INFORMATION
        {
            public int Bias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string StandardName;
            public SYSTEMTIME StandardDate;
            public int StandardBias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DaylightName;
            public SYSTEMTIME DaylightDate;
            public int DaylightBias;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TRACE_LOGFILE_HEADER
        {
            public uint BufferSize;
            public uint Version;
            public uint ProviderVersion;
            public uint NumberOfProcessors;
            public long EndTime;
            public uint TimerResolution;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint BuffersWritten;
            public Guid LogInstanceGuid;
            public IntPtr LoggerName;
            public IntPtr LogFileName;
            public TIME_ZONE_INFORMATION TimeZone;
            public long BootTime;
            public long PerfFreq;
            public long StartTime;
            public uint ReservedFlags;
            public uint BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE_HEADER
        {
            public ushort Size;
            public ushort FieldTypeFlags;
            public uint Version;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp;
            public Guid Guid;
            public uint KernelTime;
            public uint UserTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE
        {
            public EVENT_TRACE_HEADER Header;
            public uint InstanceId;
            public uint ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public uint MofLength;
            public uint ClientContext;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct EVENT_TRACE_LOGFILEW
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string LogFileName;
            [MarshalAs(UnmanagedType.LPWStr)] public string LoggerName;
            public long CurrentTime;
            public uint BuffersRead;
            public uint ProcessTraceMode;
            public EVENT_TRACE CurrentEvent;
            public TRACE_LOGFILE_HEADER LogfileHeader;
            public IntPtr BufferCallback;
            public uint BufferSize;
            public uint Filled;
            public uint EventsLost;
            public IntPtr EventRecordCallback;
            public uint IsKernelTrace;
            public IntPtr Context;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint StartTraceW(out ulong sessionHandle, string sessionName, IntPtr properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ControlTraceW(ulong sessionHandle, string sessionName, IntPtr properties, uint controlCode);

        [DllImport("advapi32.dll")]
        private static extern uint EnableTraceEx2(
            ulong traceHandle,
            ref Guid providerId,
            uint controlCode,
            byte level,
            ulong matchAnyKeyword,
            ulong matchAllKeyword,
            uint timeout,
            IntPtr enableParameters);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern ulong OpenTraceW(ref EVENT_TRACE_LOGFILEW logFile);

        [DllImport("advapi32.dll")]
        private static extern uint ProcessTrace(ulong[] handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll")]
        private static extern uint CloseTrace(ulong traceHandle);

        // Estado da sessao e ring buffers fixos separados (DXGI/D3D9 primario + DxgKrnl fallback sem dupla contagem)
        private readonly object _sync = new object();
        private readonly long[] _frameTicks = new long[1024];
        private int _frameHead;
        private int _frameCount;
        private long _lastFrameTick;

        private readonly long[] _dxgkTicks = new long[1024];
        private int _dxgkHead;
        private int _dxgkCount;
        private long _lastDxgkTick;

        private volatile int _targetPid;
        private volatile bool _running;
        private ulong _sessionHandle;
        private ulong _traceHandle = INVALID_PROCESSTRACE_HANDLE;
        private Thread _workerThread;
        private EventRecordCallback _callbackPin;
        private bool _disposed;

        // Offsets estaveis do EVENT_RECORD (evntcons.h / evntprov.h) — validos em x64 e x86
        private const int OffsetProcessId = 12;
        private const int OffsetProviderId = 24;
        private const int OffsetEventId = 40;

        public bool IsRunning { get { return _running; } }
        public int TargetPid { get { return _targetPid; } }

        public void EnsureTracking(int pid)
        {
            if (_disposed) return;
            if (pid <= 4)
            {
                Stop();
                return;
            }

            if (_running)
            {
                if (_targetPid != pid)
                {
                    _targetPid = pid;
                    ResetSamples();
                }
                return;
            }

            Start(pid);
        }

        public void Start(int pid)
        {
            lock (_sync)
            {
                if (_disposed || pid <= 4) return;
                if (_running)
                {
                    if (_targetPid != pid)
                    {
                        _targetPid = pid;
                        ResetSamplesLocked();
                    }
                    return;
                }

                _targetPid = pid;
                ResetSamplesLocked();

                // Garante que nenhuma sessao orfa anterior bloqueie o inicio
                StopSessionByName();

                IntPtr propsBuf = AllocateProperties();
                try
                {
                    ulong sessHandle;
                    uint status = StartTraceW(out sessHandle, SessionName, propsBuf);
                    if (status == ERROR_ALREADY_EXISTS)
                    {
                        StopSessionByName();
                        status = StartTraceW(out sessHandle, SessionName, propsBuf);
                    }
                    if (status != ERROR_SUCCESS)
                    {
                        return; // Sem privilegios Admin ou ETW indisponivel -> falha silenciosa
                    }

                    _sessionHandle = sessHandle;

                    Guid dxgi = DxgiProviderGuid;
                    EnableTraceEx2(_sessionHandle, ref dxgi, EVENT_CONTROL_CODE_ENABLE_PROVIDER, 5, ulong.MaxValue, 0, 0, IntPtr.Zero);

                    Guid d3d9 = D3d9ProviderGuid;
                    EnableTraceEx2(_sessionHandle, ref d3d9, EVENT_CONTROL_CODE_ENABLE_PROVIDER, 5, ulong.MaxValue, 0, 0, IntPtr.Zero);

                    Guid dxgk = DxgKrnlProviderGuid;
                    EnableTraceEx2(_sessionHandle, ref dxgk, EVENT_CONTROL_CODE_ENABLE_PROVIDER, 5, DxgKrnlBaseKeyword, 0, 0, IntPtr.Zero);

                    _callbackPin = OnEventRecord;
                    var logFile = new EVENT_TRACE_LOGFILEW
                    {
                        LoggerName = SessionName,
                        ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD,
                        EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_callbackPin)
                    };

                    ulong traceHandle = OpenTraceW(ref logFile);
                    if (traceHandle == INVALID_PROCESSTRACE_HANDLE || traceHandle == 0)
                    {
                        StopSessionByName();
                        _sessionHandle = 0;
                        return;
                    }

                    _traceHandle = traceHandle;
                    _running = true;

                    _workerThread = new Thread(TraceLoop)
                    {
                        IsBackground = true,
                        Name = "VoltPulse-FpsEtw",
                        Priority = ThreadPriority.BelowNormal
                    };
                    _workerThread.Start(traceHandle);
                }
                catch
                {
                    StopSessionByName();
                    _running = false;
                }
                finally
                {
                    Marshal.FreeHGlobal(propsBuf);
                }
            }
        }

        private void TraceLoop(object state)
        {
            ulong handle = (ulong)state;
            try
            {
                ProcessTrace(new[] { handle }, 1, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
            finally
            {
                _running = false;
            }
        }

        private void OnEventRecord(IntPtr recordPtr)
        {
            if (!_running || recordPtr == IntPtr.Zero) return;

            int pid = Marshal.ReadInt32(recordPtr, OffsetProcessId);
            if (pid != _targetPid) return;

            ushort eventId = (ushort)Marshal.ReadInt16(recordPtr, OffsetEventId);
            int provFirst = Marshal.ReadInt32(recordPtr, OffsetProviderId);

            bool isPrimary =
                (provFirst == unchecked((int)0xCA11C036) && (eventId == DxgiPresentStart || eventId == DxgiPresentMpoStart || eventId == DxgiPresent1Start)) ||
                (provFirst == unchecked((int)0x783ACA0A) && eventId == D3d9PresentStart);

            bool isDxgk = !isPrimary && provFirst == unchecked((int)0x802EC45A) && eventId == DxgKrnlPresentStart;
            if (!isPrimary && !isDxgk) return;

            long nowTick = Stopwatch.GetTimestamp();
            lock (_frameTicks)
            {
                if (isPrimary)
                {
                    _frameTicks[_frameHead] = nowTick;
                    _frameHead = (_frameHead + 1) & 1023;
                    if (_frameCount < 1024) _frameCount++;
                    _lastFrameTick = nowTick;
                }
                else
                {
                    _dxgkTicks[_dxgkHead] = nowTick;
                    _dxgkHead = (_dxgkHead + 1) & 1023;
                    if (_dxgkCount < 1024) _dxgkCount++;
                    _lastDxgkTick = nowTick;
                }
            }
        }

        public int? GetCurrentFps()
        {
            if (!_running) return null;

            long now = Stopwatch.GetTimestamp();
            long freq = Stopwatch.Frequency;
            long windowTicks = freq;     // 1.0 segundo
            long staleTicks = freq * 2;  // 2.0 segundos sem frames => nulo

            lock (_frameTicks)
            {
                // Prefere DXGI/D3D9; cai automaticamente para DxgKrnl (Vulkan/OpenGL/MPO) sem dupla contagem!
                if (_frameCount > 0 && (now - _lastFrameTick) <= staleTicks)
                    return ComputeFpsFromRing(_frameTicks, _frameHead, _frameCount, now, freq, windowTicks);

                if (_dxgkCount > 0 && (now - _lastDxgkTick) <= staleTicks)
                    return ComputeFpsFromRing(_dxgkTicks, _dxgkHead, _dxgkCount, now, freq, windowTicks);

                return null;
            }
        }

        private static int? ComputeFpsFromRing(long[] ring, int head, int count, long now, long freq, long windowTicks)
        {
            int countInWindow = 0;
            long oldestInWindow = now;

            for (int i = 0; i < count; i++)
            {
                int idx = (head - 1 - i) & 1023;
                long ts = ring[idx];
                if ((now - ts) <= windowTicks)
                {
                    countInWindow++;
                    oldestInWindow = ts;
                }
                else
                {
                    break;
                }
            }

            if (countInWindow <= 0) return null;
            double spanSec = (double)(now - oldestInWindow) / freq;
            if (countInWindow >= 2 && spanSec >= 0.25 && spanSec < 0.92)
            {
                int est = (int)Math.Round((countInWindow - 1) / spanSec);
                return Math.Max(1, Math.Min(999, est));
            }
            return Math.Max(1, Math.Min(999, countInWindow));
        }

        private void ResetSamples()
        {
            lock (_frameTicks)
            {
                ResetSamplesLocked();
            }
        }

        private void ResetSamplesLocked()
        {
            _frameHead = 0;
            _frameCount = 0;
            _lastFrameTick = 0;
            _dxgkHead = 0;
            _dxgkCount = 0;
            _lastDxgkTick = 0;
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_running && _sessionHandle == 0 && _traceHandle == INVALID_PROCESSTRACE_HANDLE)
                    return;

                _running = false;
                _targetPid = 0;
                ResetSamples();

                StopSessionByName();
                _sessionHandle = 0;

                if (_traceHandle != INVALID_PROCESSTRACE_HANDLE && _traceHandle != 0)
                {
                    try { CloseTrace(_traceHandle); } catch { }
                    _traceHandle = INVALID_PROCESSTRACE_HANDLE;
                }

                if (_workerThread != null && _workerThread.IsAlive)
                {
                    try { _workerThread.Join(350); } catch { }
                }
                _workerThread = null;
            }
        }

        private static IntPtr AllocateProperties()
        {
            int structSize = Marshal.SizeOf(typeof(EVENT_TRACE_PROPERTIES));
            int nameBytes = (SessionName.Length + 1) * 2;
            int totalSize = structSize + nameBytes + 64;
            IntPtr buf = Marshal.AllocHGlobal(totalSize);

            // Zera toda a memoria
            byte[] zeros = new byte[totalSize];
            Marshal.Copy(zeros, 0, buf, totalSize);

            var props = new EVENT_TRACE_PROPERTIES();
            props.Wnode.BufferSize = (uint)totalSize;
            props.Wnode.Flags = WNODE_FLAG_TRACED_GUID;
            props.Wnode.ClientContext = 1; // QPC clock
            props.Wnode.Guid = Guid.NewGuid();
            props.BufferSize = 32;         // 32 KB (super leve!)
            props.MinimumBuffers = 2;
            props.MaximumBuffers = 8;
            props.LogFileMode = EVENT_TRACE_REAL_TIME_MODE;
            props.FlushTimer = 1;
            props.LoggerNameOffset = (uint)structSize;

            Marshal.StructureToPtr(props, buf, false);
            return buf;
        }

        private static void StopSessionByName()
        {
            IntPtr buf = AllocateProperties();
            try
            {
                ControlTraceW(0, SessionName, buf, EVENT_TRACE_CONTROL_STOP);
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
