using System;
using System.IO;

namespace LocalServiceManager
{
    internal sealed class AppPaths
    {
        public static readonly AppPaths Current = Create();

        public string AppDirectory;
        public string AppRoot;
        public string ConfigLocalPath;
        public string ConfigExamplePath;
        public string DefaultLogsRoot;

        private static AppPaths Create()
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var appRoot = FindAppRoot(appDirectory);
            var logsRoot = Path.Combine(appRoot, "logs");
            Directory.CreateDirectory(logsRoot);

            return new AppPaths
            {
                AppDirectory = appDirectory,
                AppRoot = appRoot,
                ConfigLocalPath = Path.Combine(appRoot, "config.local.json"),
                ConfigExamplePath = Path.Combine(appRoot, "config.example.json"),
                DefaultLogsRoot = logsRoot
            };
        }

        private static string FindAppRoot(string appDirectory)
        {
            var dir = new DirectoryInfo(appDirectory);
            if (dir.Name.Equals("dist", StringComparison.OrdinalIgnoreCase) && dir.Parent != null)
            {
                return dir.Parent.FullName;
            }
            return dir.FullName;
        }
    }
}

