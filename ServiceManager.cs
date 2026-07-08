using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace LocalServiceManager
{
    internal sealed class ServiceManager
    {
        private readonly AppPaths _paths;
        private LocalServiceConfig _config;
        private List<ManagedService> _services;
        private string _configPath;
        private DateTime _configLastWriteTimeUtc;

        public ServiceManager(AppPaths paths)
        {
            _paths = paths;
            ReloadConfig();
        }

        public LocalServiceConfig Config { get { return _config; } }
        public IList<ManagedService> Services { get { return _services; } }

        public void ReloadConfig()
        {
            var config = LocalServiceConfig.Load(_paths);
            var services = new List<ManagedService>();
            foreach (var definition in config.services)
            {
                if (string.IsNullOrWhiteSpace(definition.id)) continue;
                services.Add(new ManagedService(definition, config.Expand(definition.endpoint)));
            }
            _config = config;
            _services = services;
            _configPath = config.SourcePath;
            _configLastWriteTimeUtc = File.GetLastWriteTimeUtc(config.SourcePath);
        }

        public bool ReloadConfigIfChanged()
        {
            var configPath = CurrentConfigPath();
            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(configPath);
            if (_config == null
                || !string.Equals(configPath, _configPath, StringComparison.OrdinalIgnoreCase)
                || lastWriteTimeUtc != _configLastWriteTimeUtc)
            {
                ReloadConfig();
                return true;
            }
            return false;
        }

        public Task<IList<ManagedServiceStatus>> GetStatusesAsync()
        {
            var services = new List<ManagedService>(_services);
            return Task.Factory.StartNew<IList<ManagedServiceStatus>>(delegate
            {
                var list = new List<ManagedServiceStatus>();
                foreach (var service in services)
                {
                    list.Add(CheckService(service));
                }
                return list;
            });
        }

        public Task<string> StartServiceAsync(string serviceId)
        {
            var service = FindService(serviceId);
            return service == null ? UnknownServiceAsync(serviceId) : ExecuteActionAsync(service, service.Definition.start, "启动");
        }

        public Task<string> StopServiceAsync(string serviceId)
        {
            var service = FindService(serviceId);
            return service == null ? UnknownServiceAsync(serviceId) : ExecuteActionAsync(service, service.Definition.stop, "停止");
        }

        public Task<string> StartAllAsync()
        {
            return Task.Factory.StartNew(delegate
            {
                var output = new StringBuilder();
                foreach (var service in _services)
                {
                    var status = CheckService(service);
                    if (status.Running)
                    {
                        AppendOutput(output, service.Name + " 已运行，跳过启动");
                        continue;
                    }
                    AppendOutput(output, ExecuteActionAsync(service, service.Definition.start, "启动").Result);
                }
                return output.ToString().Trim();
            });
        }

        public Task<string> StopAllAsync()
        {
            return Task.Factory.StartNew(delegate
            {
                var output = new StringBuilder();
                for (var i = _services.Count - 1; i >= 0; i--)
                {
                    var service = _services[i];
                    AppendOutput(output, ExecuteActionAsync(service, service.Definition.stop, "停止").Result);
                }
                return output.ToString().Trim();
            });
        }

        public void OpenLocalConfig()
        {
            EnsureLocalConfigExists();
            OpenUrl(_paths.ConfigLocalPath);
        }

        public void OpenInstanceConfig()
        {
            OpenUrl(_paths.ConfigExamplePath);
        }

        public void OpenEndpoint(string endpoint)
        {
            OpenUrl(endpoint);
        }

        public void OpenLogs()
        {
            OpenUrl(_config.GetLogsPath());
        }

        private void EnsureLocalConfigExists()
        {
            if (File.Exists(_paths.ConfigLocalPath)) return;
            if (!File.Exists(_paths.ConfigExamplePath)) throw new FileNotFoundException("Missing config.example.json", _paths.ConfigExamplePath);
            File.Copy(_paths.ConfigExamplePath, _paths.ConfigLocalPath);
        }

        private string CurrentConfigPath()
        {
            if (File.Exists(_paths.ConfigLocalPath)) return _paths.ConfigLocalPath;
            if (File.Exists(_paths.ConfigExamplePath)) return _paths.ConfigExamplePath;
            throw new FileNotFoundException("Missing config.local.json or config.example.json", _paths.ConfigExamplePath);
        }

        private ManagedService FindService(string serviceId)
        {
            foreach (var service in _services)
            {
                if (string.Equals(service.Id, serviceId, StringComparison.OrdinalIgnoreCase)) return service;
            }
            return null;
        }

        private ManagedServiceStatus CheckService(ManagedService service)
        {
            var health = service.Definition.health;
            var type = health == null ? "" : (health.type ?? "").Trim().ToLowerInvariant();
            if (type == "http") return CheckHttp(service, health);
            if (type == "process") return CheckProcess(service, health);
            return new ManagedServiceStatus(service, false, "异常", "未配置健康检查");
        }

        private ManagedServiceStatus CheckHttp(ManagedService service, ServiceHealthConfig health)
        {
            var url = _config.Expand(string.IsNullOrWhiteSpace(health.url) ? service.Endpoint : health.url);
            var timeoutMs = health.timeoutMs > 0 ? health.timeoutMs : 3000;
            var maxStatus = health.okStatusMax > 0 ? health.okStatusMax : 399;
            var result = TryHttp(url, timeoutMs);
            if (result.StatusCode <= 0)
            {
                return new ManagedServiceStatus(service, false, result.TimedOut ? "异常" : "未运行", result.Error);
            }
            if (result.StatusCode > maxStatus)
            {
                return new ManagedServiceStatus(service, false, "异常", result.Error);
            }
            if (health.requiredText != null)
            {
                foreach (var required in health.requiredText)
                {
                    var value = _config.Expand(required);
                    if (!Contains(result.Body, value))
                    {
                        return new ManagedServiceStatus(service, false, "异常", "HTTP " + result.StatusCode + " but missing text: " + value);
                    }
                }
            }
            return new ManagedServiceStatus(service, true, "运行中", "HTTP " + result.StatusCode);
        }

        private ManagedServiceStatus CheckProcess(ManagedService service, ServiceHealthConfig health)
        {
            var output = RunPowerShellAsync(BuildProcessHealthScript(health), TimeSpan.FromMilliseconds(health.timeoutMs > 0 ? health.timeoutMs : 5000)).Result;
            if (string.IsNullOrWhiteSpace(output))
            {
                return new ManagedServiceStatus(service, false, "未运行", "process not found");
            }
            return new ManagedServiceStatus(service, true, "运行中", TrimLog(output));
        }

        private string BuildProcessHealthScript(ServiceHealthConfig health)
        {
            var sb = new StringBuilder();
            sb.Append("$name=").Append(PsSingle(_config.Expand(health.processName))).AppendLine();
            sb.Append("$needles=@(");
            if (health.commandLineContainsAny != null)
            {
                for (var i = 0; i < health.commandLineContainsAny.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(PsSingle(_config.Expand(health.commandLineContainsAny[i])));
                }
            }
            sb.AppendLine(")");
            sb.AppendLine("$procs = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue");
            sb.AppendLine("if($name){ $procs = $procs | Where-Object { $_.Name -ieq $name } }");
            sb.AppendLine("if($needles.Count -gt 0){ $procs = $procs | Where-Object { $cmd=$_.CommandLine; $cmd -and ($needles | Where-Object { $cmd -like ('*'+$_+'*') }) } }");
            sb.AppendLine("$procs | Select-Object -First 5 | ForEach-Object { [string]$_.ProcessId + ' ' + $_.Name }");
            return sb.ToString();
        }

        private Task<string> ExecuteActionAsync(ManagedService service, ServiceActionConfig action, string verb)
        {
            if (action == null)
            {
                return Task.Factory.StartNew(delegate { return service.Name + " 未配置" + verb + "动作"; });
            }

            var type = (action.type ?? "").Trim().ToLowerInvariant();
            var timeout = TimeSpan.FromSeconds(action.timeoutSeconds > 0 ? action.timeoutSeconds : 45);
            if (type == "powershell")
            {
                return RunPowerShellAsync(_config.Expand(action.script ?? ""), timeout);
            }
            if (type == "process")
            {
                return RunProcessAsync(
                    _config.Expand(action.file),
                    ExpandArgs(action.args),
                    _config.Expand(string.IsNullOrWhiteSpace(action.workingDirectory) ? _paths.AppRoot : action.workingDirectory),
                    timeout,
                    action.detached);
            }
            return Task.Factory.StartNew(delegate { return service.Name + " 的" + verb + "动作类型无效: " + action.type; });
        }

        private string[] ExpandArgs(List<string> args)
        {
            if (args == null || args.Count == 0) return new string[0];
            var result = new string[args.Count];
            for (var i = 0; i < args.Count; i++) result[i] = _config.Expand(args[i]);
            return result;
        }

        private Task<string> RunPowerShellAsync(string command, TimeSpan timeout)
        {
            var utf8Command = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $OutputEncoding = [Console]::OutputEncoding; " + command;
            return RunProcessAsync("powershell", new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", utf8Command }, _paths.AppRoot, timeout, false);
        }

        private static Task<string> RunProcessAsync(string fileName, string[] args, string workingDirectory, TimeSpan timeout, bool detached)
        {
            return Task.Factory.StartNew(delegate
            {
                if (string.IsNullOrWhiteSpace(fileName)) return "missing process file name";
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var arg in args) startInfo.Arguments += QuoteArg(arg) + " ";

                if (detached)
                {
                    using (Process.Start(startInfo)) { }
                    return "started detached: " + fileName;
                }

                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;

                using (var process = new Process())
                {
                    process.StartInfo = startInfo;
                    var output = new StringBuilder();
                    process.Start();
                    if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                    {
                        try { process.Kill(); } catch { }
                        output.AppendLine("Timed out after " + timeout.TotalSeconds + "s");
                    }
                    output.Append(process.StandardOutput.ReadToEnd());
                    output.Append(process.StandardError.ReadToEnd());
                    return output.ToString().Trim();
                }
            });
        }

        private static HttpResult TryHttp(string url, int timeoutMs)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.AllowAutoRedirect = true;
                request.UserAgent = "LocalServiceManager/1.0";
                if (IsLoopbackUrl(url)) request.Proxy = null;
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    return new HttpResult((int)response.StatusCode, ReadBody(response), "HTTP " + (int)response.StatusCode, false);
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    using (response)
                    {
                        return new HttpResult((int)response.StatusCode, ReadBody(response), ex.Message, false);
                    }
                }
                var timedOut = ex.Status == WebExceptionStatus.Timeout;
                var error = timedOut ? "HTTP health check timed out after " + timeoutMs + "ms" : ex.Message;
                return new HttpResult(0, "", error, timedOut);
            }
            catch (Exception ex)
            {
                return new HttpResult(0, "", ex.Message, false);
            }
        }

        private static string ReadBody(HttpWebResponse response)
        {
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static bool IsLoopbackUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            IPAddress address;
            return IPAddress.TryParse(uri.Host, out address) && IPAddress.IsLoopback(address);
        }

        private static Task<string> UnknownServiceAsync(string serviceId)
        {
            return Task.Factory.StartNew(delegate { return "Unknown service: " + serviceId; });
        }

        private static void AppendOutput(StringBuilder output, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (output.Length > 0) output.AppendLine();
            output.Append(value.Trim());
        }

        private static string QuoteArg(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string PsSingle(string value)
        {
            return "'" + (value ?? "").Replace("'", "''") + "'";
        }

        private static string TrimLog(string value)
        {
            value = (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length <= 180 ? value : value.Substring(0, 180) + "...";
        }

        private static bool Contains(string source, string value)
        {
            return source != null && value != null && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }

        private sealed class HttpResult
        {
            public readonly int StatusCode;
            public readonly string Body;
            public readonly string Error;
            public readonly bool TimedOut;

            public HttpResult(int statusCode, string body, string error, bool timedOut)
            {
                StatusCode = statusCode;
                Body = body;
                Error = error;
                TimedOut = timedOut;
            }
        }
    }
}

