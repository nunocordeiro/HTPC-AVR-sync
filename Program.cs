using System;
using System.Windows.Forms;

namespace HTPCAVRVolume
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Logger.Init();

            // Catch unhandled exceptions on the UI thread
            Application.ThreadException += (s, e) =>
            {
                Logger.LogException("Application.ThreadException", e.Exception);
                MessageBox.Show(
                    $"Unexpected error: {e.Exception.Message}\n\nSee HTPCAVRVolume.log for details.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            // Catch unhandled exceptions on background / COM threads
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    Logger.LogException("AppDomain.UnhandledException", ex);
                else
                    Logger.Log($"AppDomain.UnhandledException (non-Exception): {e.ExceptionObject}");
            };

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new HTPCAVRVolume());

            Logger.Log("=== App exited cleanly ===");
        }
    }
}
