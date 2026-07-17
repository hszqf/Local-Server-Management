using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LocalServiceManager
{
    internal sealed class ProcessHealthResult
    {
        public readonly bool Found;
        public readonly bool InspectionFailed;
        public readonly string Detail;

        public ProcessHealthResult(bool found, bool inspectionFailed, string detail)
        {
            Found = found;
            InspectionFailed = inspectionFailed;
            Detail = detail;
        }
    }

    internal static class ProcessHealthChecker
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int ProcessCommandLineInformation = 60;

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            IntPtr processInformation,
            int processInformationLength,
            out int returnLength);

        public static ProcessHealthResult Check(string processName, IList<string> commandLineContainsAny)
        {
            var needles = NormalizeNeedles(commandLineContainsAny);
            var candidates = GetCandidates(processName);
            var matches = new List<string>();
            var inspectionFailures = 0;

            foreach (var process in candidates)
            {
                try
                {
                    if (needles.Count > 0)
                    {
                        string commandLine;
                        if (!TryGetCommandLine(process.Id, out commandLine))
                        {
                            inspectionFailures++;
                            continue;
                        }
                        if (!ContainsAny(commandLine, needles)) continue;
                    }

                    if (matches.Count < 5) matches.Add(process.Id + " " + SafeProcessName(process));
                }
                catch
                {
                    inspectionFailures++;
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (matches.Count > 0)
            {
                return new ProcessHealthResult(true, false, string.Join(Environment.NewLine, matches.ToArray()));
            }
            if (needles.Count > 0 && inspectionFailures > 0)
            {
                return new ProcessHealthResult(
                    false,
                    true,
                    "process command line unavailable for " + inspectionFailures + " candidate(s)");
            }
            return new ProcessHealthResult(false, false, "process not found");
        }

        private static Process[] GetCandidates(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return Process.GetProcesses();
            return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName.Trim()));
        }

        private static List<string> NormalizeNeedles(IList<string> values)
        {
            var result = new List<string>();
            if (values == null) return result;
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) result.Add(value.Trim());
            }
            return result;
        }

        private static bool ContainsAny(string commandLine, IList<string> needles)
        {
            if (string.IsNullOrEmpty(commandLine)) return false;
            foreach (var needle in needles)
            {
                if (commandLine.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static string SafeProcessName(Process process)
        {
            try { return process.ProcessName + ".exe"; }
            catch { return "process"; }
        }

        private static bool TryGetCommandLine(int processId, out string commandLine)
        {
            commandLine = null;
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero) return false;

            try
            {
                int requiredLength;
                NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, out requiredLength);
                if (requiredLength <= Marshal.SizeOf(typeof(UnicodeString))) return false;

                var buffer = Marshal.AllocHGlobal(requiredLength);
                try
                {
                    int returnedLength;
                    var status = NtQueryInformationProcess(
                        handle,
                        ProcessCommandLineInformation,
                        buffer,
                        requiredLength,
                        out returnedLength);
                    if (status < 0) return false;

                    var value = (UnicodeString)Marshal.PtrToStructure(buffer, typeof(UnicodeString));
                    if (value.Length == 0)
                    {
                        commandLine = string.Empty;
                        return true;
                    }
                    if (value.Buffer == IntPtr.Zero || value.Length % 2 != 0) return false;

                    var bufferStart = buffer.ToInt64();
                    var bufferEnd = bufferStart + requiredLength;
                    var textStart = value.Buffer.ToInt64();
                    var textEnd = textStart + value.Length;
                    if (textStart < bufferStart || textEnd > bufferEnd) return false;

                    commandLine = Marshal.PtrToStringUni(value.Buffer, value.Length / 2);
                    return commandLine != null;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
