using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace PcPowerMonitor
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => LogCrash("UI Thread", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash("AppDomain", e.ExceptionObject as Exception);

            bool createdNew;
            using (var mutex = new Mutex(true, "Local\\VoltPulse_PowerMonitor_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "O VoltPulse já está em execução (verifique o ícone na bandeja do sistema).",
                        "VoltPulse",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool startMinimized = args != null && args.Any(a =>
                    string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

                // Verifica e instala automaticamente PawnIO / WebView2 na primeira abertura
                DependencyManager.EnsureDependenciesOnStartup(startMinimized);

                Application.Run(new MainForm(startMinimized));
            }
        }

        private static void LogCrash(string source, Exception ex)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PcPowerMonitor");
                Directory.CreateDirectory(dir);
                string logPath = Path.Combine(dir, "crash.log");
                File.AppendAllText(logPath,
                    string.Format("[{0:yyyy-MM-dd HH:mm:ss}] ({1}) {2}\r\n\r\n", DateTime.Now, source, ex));
            }
            catch { }
        }
    }
}
