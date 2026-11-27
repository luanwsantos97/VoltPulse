using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PcPowerMonitor
{
    public static class DependencyManager
    {
        private const string PawnIoDownloadUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
        private const string WebView2DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        // WinVerifyTrust (Authenticode) — somente System32
        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public string pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

        public static bool VerifyAuthenticodeSignature(string filePath, string requiredSubjectSubstring = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;

            IntPtr fileInfoPtr = IntPtr.Zero;
            try
            {
                var fileInfo = new WINTRUST_FILE_INFO
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                    pcwszFilePath = filePath,
                    hFile = IntPtr.Zero,
                    pgKnownSubject = IntPtr.Zero
                };

                fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = 2,          // WTD_UI_NONE
                    fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                    dwUnionChoice = 1,       // WTD_CHOICE_FILE
                    pFile = fileInfoPtr,
                    dwStateAction = 1,       // WTD_STATEACTION_VERIFY
                    dwProvFlags = 0x00000010 // WTD_REVOCATION_CHECK_NONE
                };

                uint status = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);

                // Fecha handle de estado do WinVerifyTrust
                data.dwStateAction = 2; // WTD_STATEACTION_CLOSE
                WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);

                if (status != 0) return false;

                if (!string.IsNullOrEmpty(requiredSubjectSubstring))
                {
                    using (var cert = X509Certificate.CreateFromSignedFile(filePath))
                    {
                        string subject = cert != null ? (cert.Subject ?? "") : "";
                        if (subject.IndexOf(requiredSubjectSubstring, StringComparison.OrdinalIgnoreCase) < 0)
                            return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (fileInfoPtr != IntPtr.Zero)
                {
                    try { Marshal.DestroyStructure(fileInfoPtr, typeof(WINTRUST_FILE_INFO)); } catch { }
                    Marshal.FreeCoTaskMem(fileInfoPtr);
                }
            }
        }

        public static bool IsPawnIoInstalled()
        {
            return HardwareMonitorService.CheckPawnIoInstalled();
        }

        public static bool IsWebView2Installed()
        {
            string[] paths = new[]
            {
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
            };

            foreach (var p in paths)
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(p))
                    {
                        if (k != null && !string.IsNullOrEmpty(k.GetValue("pv") as string)) return true;
                    }
                }
                catch { }

                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(p))
                    {
                        if (k != null && !string.IsNullOrEmpty(k.GetValue("pv") as string)) return true;
                    }
                }
                catch { }
            }
            return false;
        }

        /// <summary>
        /// Verifica WebView2 e PawnIO na inicialização. Se tudo já estiver instalado, retorna imediatamente.
        /// Caso falte algo, abre o assistente visual para instalar automaticamente antes de iniciar a dashboard.
        /// </summary>
        public static void EnsureDependenciesOnStartup(bool silentMode)
        {
            bool needWebView2 = !IsWebView2Installed();
            bool needPawnIo = !IsPawnIoInstalled();

            if (!needWebView2 && !needPawnIo) return;
            if (silentMode) return;

            using (var dlg = new DependencySetupForm(needWebView2, needPawnIo))
            {
                dlg.ShowDialog();
            }
        }

        private class DependencySetupForm : Form
        {
            private readonly bool _needWebView2;
            private readonly bool _needPawnIo;
            private readonly Label _lblStatus;
            private readonly ProgressBar _progress;
            private readonly Button _btnInstall;
            private readonly Button _btnSkip;

            [DllImport("dwmapi.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

            public DependencySetupForm(bool needWebView2, bool needPawnIo)
            {
                _needWebView2 = needWebView2;
                _needPawnIo = needPawnIo;

                Text = "VoltPulse — Preparando Dependências";
                Size = new Size(520, 320);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                BackColor = Color.FromArgb(15, 23, 42);
                ForeColor = Color.FromArgb(241, 245, 249);
                Font = new Font("Segoe UI", 9.5f);

                try
                {
                    int dark = 1;
                    DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
                }
                catch { }

                var lblTitle = new Label
                {
                    Text = "⚡ Configuração Inicial do VoltPulse",
                    Font = new Font("Segoe UI Semibold", 13.5f),
                    ForeColor = Color.FromArgb(56, 189, 248),
                    AutoSize = true,
                    Location = new Point(24, 20)
                };

                string itemsText = "";
                if (_needPawnIo)
                    itemsText += "• Driver PawnIO — necessário no Windows 10/11 para ler sensores de CPU, placa-mãe e SSD.\n";
                if (_needWebView2)
                    itemsText += "• Microsoft Edge WebView2 Runtime — necessário para renderizar a dashboard.\n";

                var lblDesc = new Label
                {
                    Text = "Detectamos que os seguintes componentes ainda não estão instalados neste PC:\n\n" +
                           itemsText +
                           "\nO VoltPulse pode instalar tudo automaticamente agora (usando instaladores oficiais assinados).",
                    ForeColor = Color.FromArgb(203, 213, 225),
                    Size = new Size(460, 120),
                    Location = new Point(24, 56)
                };

                _lblStatus = new Label
                {
                    Text = "Pronto para instalar.",
                    ForeColor = Color.FromArgb(148, 163, 184),
                    AutoSize = false,
                    Size = new Size(460, 22),
                    Location = new Point(24, 184)
                };

                _progress = new ProgressBar
                {
                    Style = ProgressBarStyle.Continuous,
                    Size = new Size(460, 14),
                    Location = new Point(24, 210),
                    Value = 0
                };

                _btnInstall = new Button
                {
                    Text = "Instalar Automaticamente",
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(16, 185, 129),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold", 9.5f),
                    Size = new Size(210, 36),
                    Location = new Point(24, 236),
                    Cursor = Cursors.Hand
                };
                _btnInstall.FlatAppearance.BorderSize = 0;
                _btnInstall.Click += async (s, e) => await RunInstallAsync();

                _btnSkip = new Button
                {
                    Text = "Continuar sem instalar",
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(30, 41, 59),
                    ForeColor = Color.FromArgb(148, 163, 184),
                    Size = new Size(180, 36),
                    Location = new Point(304, 236),
                    Cursor = Cursors.Hand
                };
                _btnSkip.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
                _btnSkip.Click += (s, e) => Close();

                Controls.AddRange(new Control[] { lblTitle, lblDesc, _lblStatus, _progress, _btnInstall, _btnSkip });
            }

            private async Task RunInstallAsync()
            {
                _btnInstall.Enabled = false;
                _btnSkip.Enabled = false;
                _progress.Style = ProgressBarStyle.Marquee;

                string tempDir = Path.Combine(Path.GetTempPath(), "VoltPulseDeps_" + Guid.NewGuid().ToString("N"));
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    Directory.CreateDirectory(tempDir);

                    string exeDir = AppDomain.CurrentDomain.BaseDirectory;

                    if (_needPawnIo)
                    {
                        SetStatus("Preparando instalador oficial do PawnIO...");
                        string localPawn = Path.Combine(exeDir, "deps", "PawnIO_setup.exe");
                        string installerPath = localPawn;

                        if (!File.Exists(installerPath))
                        {
                            installerPath = Path.Combine(tempDir, "PawnIO_setup.exe");
                            SetStatus("Baixando PawnIO oficial do GitHub...");
                            await DownloadFileAsync(PawnIoDownloadUrl, installerPath);
                        }

                        SetStatus("Verificando assinatura digital do PawnIO...");
                        if (!VerifyAuthenticodeSignature(installerPath))
                        {
                            throw new InvalidOperationException("Falha na verificação da assinatura digital do instalador PawnIO.");
                        }

                        SetStatus("Instalando driver PawnIO silenciosamente...");
                        int code = await RunProcessAsync(installerPath, "-install -silent", Path.GetDirectoryName(installerPath));
                        if (code != 0 && code != 3010)
                        {
                            SetStatus("Abrindo instalador do PawnIO...");
                            await RunProcessAsync(installerPath, "", Path.GetDirectoryName(installerPath));
                        }
                    }

                    if (_needWebView2)
                    {
                        SetStatus("Baixando Microsoft Edge WebView2 Runtime...");
                        string wvInstaller = Path.Combine(tempDir, "MicrosoftEdgeWebview2Setup.exe");
                        await DownloadFileAsync(WebView2DownloadUrl, wvInstaller);

                        SetStatus("Verificando assinatura digital Microsoft do WebView2...");
                        if (!VerifyAuthenticodeSignature(wvInstaller, "Microsoft"))
                        {
                            throw new InvalidOperationException("Falha na verificação da assinatura digital Microsoft do WebView2.");
                        }

                        SetStatus("Instalando Microsoft Edge WebView2 Runtime (pode levar alguns segundos)...");
                        await RunProcessAsync(wvInstaller, "/silent /install", tempDir);
                    }

                    _progress.Style = ProgressBarStyle.Continuous;
                    _progress.Value = 100;
                    SetStatus("✓ Dependências instaladas! Abrindo VoltPulse...");
                    await Task.Delay(700);
                    Close();
                }
                catch (Exception ex)
                {
                    _progress.Style = ProgressBarStyle.Continuous;
                    _progress.Value = 0;
                    SetStatus("Erro na instalação: " + ex.Message);
                    _btnInstall.Enabled = true;
                    _btnSkip.Enabled = true;
                    MessageBox.Show(
                        "Não foi possível concluir a instalação automática:\n" + ex.Message +
                        "\n\nVocê ainda pode continuar e instalar manualmente depois.",
                        "VoltPulse Setup",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                finally
                {
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                }
            }

            private void SetStatus(string text)
            {
                if (InvokeRequired) Invoke(new Action<string>(SetStatus), text);
                else _lblStatus.Text = text;
            }

            private static Task DownloadFileAsync(string url, string destPath)
            {
                return Task.Run(() =>
                {
                    using (var wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "VoltPulse-Setup/1.0");
                        wc.DownloadFile(url, destPath);
                    }
                });
            }

            private static Task<int> RunProcessAsync(string fileName, string arguments, string workDir)
            {
                return Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        WorkingDirectory = string.IsNullOrEmpty(workDir) ? Path.GetDirectoryName(fileName) : workDir,
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = string.IsNullOrEmpty(arguments) ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden
                    };
                    using (var proc = Process.Start(psi))
                    {
                        if (proc == null) return -1;
                        proc.WaitForExit();
                        return proc.ExitCode;
                    }
                });
            }
        }
    }
}
