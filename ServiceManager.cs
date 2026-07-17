using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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
            var result = TryHttp(url, timeoutMs, _config.Expand(health.tlsCaFile));
            if (result.StatusCode <= 0)
            {
                var state = result.Unavailable ? "未运行" : "异常";
                return new ManagedServiceStatus(service, false, state, result.Error);
            }
            if (result.StatusCode > maxStatus)
            {
                return new ManagedServiceStatus(service, false, "异常", result.Error);
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
            var needles = new List<string>();
            if (health.commandLineContainsAny != null)
            {
                foreach (var value in health.commandLineContainsAny)
                {
                    needles.Add(_config.Expand(value));
                }
            }
            var result = ProcessHealthChecker.Check(_config.Expand(health.processName ?? ""), needles);
            if (result.Found) return new ManagedServiceStatus(service, true, "运行中", TrimLog(result.Detail));
            return new ManagedServiceStatus(service, false, result.InspectionFailed ? "异常" : "未运行", result.Detail);
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

        private static HttpResult TryHttp(string url, int timeoutMs, string tlsCaFile)
        {
            X509Certificate2 trustedCa = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(tlsCaFile))
                {
                    Uri uri;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("tlsCaFile requires an HTTPS health URL");
                    }
                    if (!File.Exists(tlsCaFile)) throw new FileNotFoundException("TLS CA file not found", tlsCaFile);
                    trustedCa = new X509Certificate2(tlsCaFile);
                }
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.AllowAutoRedirect = true;
                request.UserAgent = "LocalServiceManager/1.0";
                if (IsLocalNetworkUrl(url)) request.Proxy = null;
                if (trustedCa != null)
                {
                    request.ServerCertificateValidationCallback = delegate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
                    {
                        return ValidateServerCertificate(certificate, errors, trustedCa);
                    };
                }
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    return new HttpResult((int)response.StatusCode, ReadBody(response), "HTTP " + (int)response.StatusCode, false, false);
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    using (response)
                    {
                        return new HttpResult((int)response.StatusCode, ReadBody(response), ex.Message, false, false);
                    }
                }
                var timedOut = ex.Status == WebExceptionStatus.Timeout;
                var unavailable = ex.Status == WebExceptionStatus.ConnectFailure
                    || ex.Status == WebExceptionStatus.NameResolutionFailure
                    || ex.Status == WebExceptionStatus.ProxyNameResolutionFailure;
                var error = timedOut ? "HTTP health check timed out after " + timeoutMs + "ms" : ex.Message;
                return new HttpResult(0, "", error, timedOut, unavailable);
            }
            catch (Exception ex)
            {
                return new HttpResult(0, "", ex.Message, false, false);
            }
            finally
            {
                if (trustedCa != null) trustedCa.Dispose();
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

        private static bool ValidateServerCertificate(X509Certificate certificate, SslPolicyErrors errors, X509Certificate2 trustedCa)
        {
            if (certificate == null || trustedCa == null) return false;
            if ((errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
            using (var serverCertificate = new X509Certificate2(certificate))
            using (var validationChain = new X509Chain())
            {
                validationChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                validationChain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                validationChain.ChainPolicy.ExtraStore.Add(trustedCa);
                if (!validationChain.Build(serverCertificate)) return false;
                var elements = validationChain.ChainElements;
                if (elements.Count == 0) return false;
                var root = elements[elements.Count - 1].Certificate;
                return string.Equals(root.Thumbprint, trustedCa.Thumbprint, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool IsLocalNetworkUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            IPAddress address;
            if (!IPAddress.TryParse(uri.Host, out address)) return false;
            if (IPAddress.IsLoopback(address)) return true;
            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4) return false;
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
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
            public readonly bool Unavailable;

            public HttpResult(int statusCode, string body, string error, bool timedOut, bool unavailable)
            {
                StatusCode = statusCode;
                Body = body;
                Error = error;
                TimedOut = timedOut;
                Unavailable = unavailable;
            }
        }
    }
}

