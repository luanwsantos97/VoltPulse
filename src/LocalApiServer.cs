using System;
using System.Net;
using System.Text;
using System.Threading;

namespace PcPowerMonitor
{
    /// <summary>
    /// Servidor HTTP 100% local (127.0.0.1) opcional para integracao com Home Assistant,
    /// Elgato Stream Deck, Rainmeter, OBS ou scripts locais. Desligado por padrao.
    /// Hardening: valida Host (anti-DNS Rebinding), remove CORS aberto (*) e restringe a GET/HEAD.
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

        private bool IsAllowedHost(string hostHeader)
        {
            if (string.IsNullOrWhiteSpace(hostHeader)) return false;
            string h = hostHeader.Trim().ToLowerInvariant();
            return h == "127.0.0.1" ||
                   h == "localhost" ||
                   h == ("127.0.0.1:" + _port) ||
                   h == ("localhost:" + _port);
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

                    res.ContentType = "application/json; charset=utf-8";
                    res.AddHeader("X-Content-Type-Options", "nosniff");
                    res.AddHeader("Cache-Control", "no-store");

                    string body;
                    if (!IsAllowedHost(req.UserHostName))
                    {
                        res.StatusCode = 403;
                        body = "{\"error\":\"forbidden_host\"}";
                    }
                    else if (!string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(req.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        res.StatusCode = 405;
                        res.AddHeader("Allow", "GET, HEAD");
                        body = "{\"error\":\"method_not_allowed\"}";
                    }
                    else
                    {
                        string path = (req.Url != null ? req.Url.AbsolutePath : "/").ToLowerInvariant();
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
                    }

                    byte[] buf = Encoding.UTF8.GetBytes(body);
                    res.ContentLength64 = buf.Length;
                    if (!string.Equals(req.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        res.OutputStream.Write(buf, 0, buf.Length);
                    }
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
