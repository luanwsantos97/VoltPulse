using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PcPowerMonitor
{
    public static class UpdateChecker
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/luanwsantos97/VoltPulse/releases/latest";

        public static Task<UpdateInfo> CheckLatestAsync(string currentVersion)
        {
            return Task.Run(() =>
            {
                var info = new UpdateInfo
                {
                    Checked = true,
                    UpdateAvailable = false,
                    LatestVersion = currentVersion,
                    ReleasePageUrl = "https://github.com/luanwsantos97/VoltPulse/releases/latest"
                };

                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;
                    using (var wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "VoltPulse/" + currentVersion);
                        wc.Headers.Add("Accept", "application/vnd.github+json");
                        string raw = wc.DownloadString(LatestReleaseApi);

                        var ser = new JavaScriptSerializer();
                        var dict = ser.Deserialize<Dictionary<string, object>>(raw);
                        if (dict == null || !dict.ContainsKey("tag_name")) return info;

                        string tag = Convert.ToString(dict["tag_name"]).Trim().TrimStart('v', 'V');
                        info.LatestVersion = tag;
                        if (dict.ContainsKey("html_url"))
                            info.ReleasePageUrl = Convert.ToString(dict["html_url"]);

                        Version vCur, vLat;
                        if (Version.TryParse(currentVersion, out vCur) && Version.TryParse(tag, out vLat) && vLat > vCur)
                        {
                            info.UpdateAvailable = true;
                        }

                        if (dict.ContainsKey("assets") && dict["assets"] is ArrayList)
                        {
                            foreach (var item in (ArrayList)dict["assets"])
                            {
                                var asset = item as Dictionary<string, object>;
                                if (asset == null) continue;
                                string name = asset.ContainsKey("name") ? Convert.ToString(asset["name"]) : "";
                                if (name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    info.DownloadUrl = Convert.ToString(asset["browser_download_url"]);
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }

                return info;
            });
        }

        public static Task<bool> DownloadAndRunInstallerAsync(string downloadUrl)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(downloadUrl)) return false;
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;
                    string tmp = Path.Combine(Path.GetTempPath(), "VoltPulse-Update-Setup.exe");
                    using (var wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "VoltPulse-Updater");
                        wc.DownloadFile(downloadUrl, tmp);
                    }
                    Process.Start(new ProcessStartInfo(tmp) { UseShellExecute = true });
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
