using Microsoft.Win32;
using System;
using System.Windows.Forms;

namespace LocalServiceManager
{
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "LocalServiceManager";
        private const string LegacyValueName = "LocalServiceManagerLegacy";

        public static string ExecutablePath { get { return Application.ExecutablePath; } }

        public static bool IsInstalled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                var value = key == null ? null : key.GetValue(ValueName) as string;
                return !string.IsNullOrWhiteSpace(value)
                    && value.IndexOf(ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public static void Install()
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                key.DeleteValue(LegacyValueName, false);
                key.SetValue(ValueName, Quote(ExecutablePath), RegistryValueKind.String);
            }
        }

        public static void Remove()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key != null)
                {
                    key.DeleteValue(ValueName, false);
                    key.DeleteValue(LegacyValueName, false);
                }
            }
        }

        private static string Quote(string path)
        {
            return "\"" + path + "\"";
        }
    }

    internal static class ServiceAutoStartManager
    {
        private const string AutoStartKey = @"Software\LocalServiceManager\ServiceAutoStart";

        public static bool IsEnabled(string serviceId)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(AutoStartKey, false))
            {
                if (key == null) return false;
                var value = key.GetValue(serviceId);
                if (value is int) return (int)value != 0;
                return string.Equals(Convert.ToString(value), "1", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void SetEnabled(string serviceId, bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(AutoStartKey))
            {
                if (enabled)
                {
                    key.SetValue(serviceId, 1, RegistryValueKind.DWord);
                }
                else
                {
                    key.DeleteValue(serviceId, false);
                }
            }
        }
    }
}

