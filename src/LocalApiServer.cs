using System;
using System.Net;
using System.Text;
using System.Threading;

namespace PcPowerMonitor
{
    /// <summary>
    /// Servidor HTTP 100% local (127.0.0.1) opcional para integracao com Home Assistant,
    /// Elgato Stream Deck, Rainmeter, OBS ou scripts locais. Desligado por padrao.
    /// </summary>
    public class LocalApiServer : IDisposable
    {
        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private volatile string _latestJson = "{}";
        private int _port;

        public bool IsRunning { get { return _running; } }
        public int Port { get { return _port; } }

        public void UpdatePayloadJson(string json)
        {
            if (!string.IsNullOrEmpty(json))
                _latestJson = json;
        }

        public bool Start(int port)
        {
            if (_running && _port == port) return true;
            Stop();

            _port = port;
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", port));
                _listener.Start();
                _running = true;

                _thread = new Thread(ListenLoop) { IsBackground = true, Name = "VoltPulse-LocalApi" };
                _thread.Start();
                return true;
            }
            catch
            {
                Stop();
                return false;
            }
        }

        private void ListenLoop()
        {
            while (_running && _listener != null && _listener.IsListening)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    var req = ctx.Request;
                    var res = ctx.Response;

                    res.AddHeader("Access-Control-Allow-Origin", "*");
                    res.ContentType = "application/json; charset=utf-8";

                    string path = (req.Url != null ? req.Url.AbsolutePath : "/").ToLowerInvariant();
                    string body;
                    if (path == "/api/telemetry" || path == "/telemetry" || path == "/")
                    {
                        body = _latestJson;
                    }
                    else if (path == "/api/health")
                    {
                        body = "{\"status\":\"ok\",\"app\":\"VoltPulse\"}";
                    }
                    else
                    {
                        res.StatusCode = 404;
                        body = "{\"error\":\"not_found\",\"endpoints\":[\"/api/telemetry\",\"/api/health\"]}";
                    }

                    byte[] buf = Encoding.UTF8.GetBytes(body);
                    res.ContentLength64 = buf.Length;
                    res.OutputStream.Write(buf, 0, buf.Length);
                    res.OutputStream.Close();
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        public void Stop()
        {
            _running = false;
            try
            {
                if (_listener != null)
                {
                    _listener.Stop();
                    _listener.Close();
                }
            }
            catch { }
            _listener = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
