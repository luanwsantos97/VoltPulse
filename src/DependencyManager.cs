using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PcPowerMonitor
{
    public static class DependencyManager
    {
        private const string PawnIoDownloadUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
        private const string WebView2DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

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
                           "\nO VoltPulse pode instalar tudo automaticamente agora (usando instaladores oficiais).",
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

                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;
                    string tempDir = Path.Combine(Path.GetTempPath(), "VoltPulseSetup");
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

                        SetStatus("Instalando driver PawnIO silenciosamente...");
                        int code = await RunProcessAsync(installerPath, "-install -silent");
                        if (code != 0 && code != 3010)
                        {
                            // Fallback interativo caso o silencioso retorne erro
                            SetStatus("Abrindo instalador do PawnIO...");
                            await RunProcessAsync(installerPath, "");
                        }
                    }

                    if (_needWebView2)
                    {
                        SetStatus("Baixando Microsoft Edge WebView2 Runtime...");
                        string wvInstaller = Path.Combine(tempDir, "MicrosoftEdgeWebview2Setup.exe");
                        await DownloadFileAsync(WebView2DownloadUrl, wvInstaller);

                        SetStatus("Instalando Microsoft Edge WebView2 Runtime (pode levar alguns segundos)...");
                        await RunProcessAsync(wvInstaller, "/silent /install");
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
                        wc.Headers.Add("User-Agent", "VoltPulse-Setup/2.1");
                        wc.DownloadFile(url, destPath);
                    }
                });
            }

            private static Task<int> RunProcessAsync(string fileName, string arguments)
            {
                return Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
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
