using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PcPowerMonitor
{
    public static class UpdateChecker
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/luanwsantos97/VoltPulse/releases/latest";

        public static bool IsAllowedReleaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;

            string host = (uri.Host ?? "").ToLowerInvariant();
            if (host == "github.com")
            {
                return uri.AbsolutePath.StartsWith("/luanwsantos97/VoltPulse/releases/", StringComparison.OrdinalIgnoreCase);
            }
            if (host == "objects.githubusercontent.com" || host == "release-assets.githubusercontent.com")
            {
                return true;
            }
            return false;
        }

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
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
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
                        {
                            string pageUrl = Convert.ToString(dict["html_url"]);
                            if (IsAllowedReleaseUrl(pageUrl)) info.ReleasePageUrl = pageUrl;
                        }

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
                                string dlUrl = asset.ContainsKey("browser_download_url") ? Convert.ToString(asset["browser_download_url"]) : "";
                                if (!IsAllowedReleaseUrl(dlUrl)) continue;

                                if (string.IsNullOrEmpty(info.DownloadUrl) &&
                                    name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    info.DownloadUrl = dlUrl;
                                    info.AssetFileName = name;
                                }
                                else if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                                {
                                    info.Sha256SumsUrl = dlUrl;
                                }
                            }
                        }
                    }
                }
                catch { }

                return info;
            });
        }

        public static Task<bool> DownloadAndRunInstallerAsync(UpdateInfo info)
        {
            return Task.Run(() =>
            {
                if (info == null || !IsAllowedReleaseUrl(info.DownloadUrl)) return false;

                string isolatedDir = Path.Combine(Path.GetTempPath(), "VoltPulseUpd_" + Guid.NewGuid().ToString("N"));
                string tmpExe = Path.Combine(isolatedDir, "VoltPulse-Setup.exe");
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    Directory.CreateDirectory(isolatedDir);

                    string expectedHash = null;
                    if (!string.IsNullOrWhiteSpace(info.Sha256SumsUrl) && IsAllowedReleaseUrl(info.Sha256SumsUrl))
                    {
                        using (var wcSum = new WebClient())
                        {
                            wcSum.Headers.Add("User-Agent", "VoltPulse-Updater");
                            string sumsContent = wcSum.DownloadString(info.Sha256SumsUrl);
                            expectedHash = ExtractExpectedSha256(sumsContent, info.AssetFileName);
                        }
                    }

                    using (var wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "VoltPulse-Updater");
                        wc.DownloadFile(info.DownloadUrl, tmpExe);
                    }

                    // Verifica integridade SHA-256 obrigatoria quando SHA256SUMS.txt esta publicado na release
                    if (!string.IsNullOrEmpty(expectedHash))
                    {
                        string actualHash = ComputeFileSha256(tmpExe);
                        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                        {
                            try { Directory.Delete(isolatedDir, true); } catch { }
                            return false;
                        }
                    }

                    Process.Start(new ProcessStartInfo(tmpExe)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = isolatedDir
                    });
                    return true;
                }
                catch
                {
                    try { if (Directory.Exists(isolatedDir)) Directory.Delete(isolatedDir, true); } catch { }
                    return false;
                }
            });
        }

        private static string ExtractExpectedSha256(string content, string assetFileName)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(assetFileName)) return null;
            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length < 66) continue;
                int sep = line.IndexOf(' ');
                if (sep < 64) sep = line.IndexOf('\t');
                if (sep < 64) continue;

                string hash = line.Substring(0, sep).Trim();
                string file = line.Substring(sep).Trim().TrimStart('*');
                if (hash.Length == 64 && string.Equals(Path.GetFileName(file), assetFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return hash.ToLowerInvariant();
                }
            }
            return null;
        }

        private static string ComputeFileSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(filePath))
            {
                byte[] bytes = sha.ComputeHash(fs);
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
