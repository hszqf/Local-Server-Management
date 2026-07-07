using System;
using System.Threading;
using System.Windows.Forms;

namespace LocalServiceManager
{
    internal static class Program
    {
        private const string MutexName = "LocalServiceManager.Singleton";

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew) return;

                if (HasArg(args, "--install-startup"))
                {
                    StartupManager.Install();
                    return;
                }

                if (HasArg(args, "--remove-startup"))
                {
                    StartupManager.Remove();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(new ServiceManager(AppPaths.Current)));
            }
        }

        private static bool HasArg(string[] args, string value)
        {
            foreach (var arg in args)
            {
                if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}

