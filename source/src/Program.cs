using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace KF2Tweaker
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Native.MakeDpiAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Crash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);

            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = Math.Max(1f, g.DpiX / 96f);
            Theme.InitFonts();
            Theme.Apply(true);

            string saved = AppSettings.Get("configDir", null);
            string dir = ConfigStore.FindConfigDir(saved);

            bool created;
            using (new Mutex(true, "KFTool26.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("KF TOOL 26 is already open.", "KF TOOL 26", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new MainForm(dir));
            }
        }

        static void Crash(Exception ex)
        {
            if (ex == null) return;
            string log = Path.Combine(ConfigStore.AppDataDir, "crash.log");
            try { File.AppendAllText(log, DateTime.Now + "\r\n" + ex + "\r\n\r\n"); } catch { }
            MessageBox.Show("Something went wrong:\n\n" + ex.Message + "\n\nDetails were saved to " + log +
                "\n\nYour config files are only written when you press Apply, and a backup is made first.",
                "KF TOOL 26", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
