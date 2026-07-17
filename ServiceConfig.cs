using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace LocalServiceManager
{
    internal sealed class LocalServiceConfig
    {
        private AppPaths _paths;

        public string appTitle { get; set; }
        public List<ServiceTabConfig> tabs { get; set; }
        public AppLinksConfig links { get; set; }
        public Dictionary<string, string> variables { get; set; }
        public List<ServiceDefinition> services { get; set; }
        public string SourcePath { get; private set; }

        public string Title
        {
            get { return string.IsNullOrWhiteSpace(appTitle) ? "本地服务管理器" : appTitle; }
        }

        public static LocalServiceConfig Load(AppPaths paths)
        {
            var configPath = File.Exists(paths.ConfigLocalPath) ? paths.ConfigLocalPath : paths.ConfigExamplePath;
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("Missing config.local.json or config.example.json", configPath);
            }

            var json = File.ReadAllText(configPath);
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
            var config = serializer.Deserialize<LocalServiceConfig>(json) ?? new LocalServiceConfig();
            config._paths = paths;
            config.SourcePath = configPath;
            config.Normalize();
            Directory.CreateDirectory(config.GetLogsPath());
            return config;
        }

        public string Expand(string value)
        {
            if (value == null) return null;
            var result = value;
            for (var pass = 0; pass < 6; pass++)
            {
                result = ReplaceBuiltIns(result);
                if (variables != null)
                {
                    foreach (var pair in variables)
                    {
                        var raw = pair.Value ?? "";
                        var variableValue = ReplaceBuiltIns(raw);
                        result = result.Replace("{{" + pair.Key + "}}", variableValue);
                    }
                }
            }
            return Environment.ExpandEnvironmentVariables(result);
        }

        public string GetLogsPath()
        {
            var value = links == null ? null : links.logsPath;
            var expanded = Expand(string.IsNullOrWhiteSpace(value) ? "{{LogRoot}}" : value);
            if (string.IsNullOrWhiteSpace(expanded)) return _paths.DefaultLogsRoot;
            try
            {
                var root = Path.GetPathRoot(expanded);
                if (!string.IsNullOrWhiteSpace(root) && !Directory.Exists(root))
                {
                    return _paths.DefaultLogsRoot;
                }
            }
            catch
            {
                return _paths.DefaultLogsRoot;
            }
            return expanded;
        }

        private string ReplaceBuiltIns(string value)
        {
            if (value == null) return null;
            return value
                .Replace("{{AppRoot}}", _paths.AppRoot)
                .Replace("{{AppDir}}", _paths.AppDirectory)
                .Replace("{{LogRoot}}", _paths.DefaultLogsRoot);
        }

        private void Normalize()
        {
            if (tabs == null || tabs.Count == 0)
            {
                tabs = new List<ServiceTabConfig>
                {
                    new ServiceTabConfig { label = "全部", tag = "" }
                };
            }
            if (links == null) links = new AppLinksConfig();
            if (variables == null) variables = new Dictionary<string, string>();
            if (services == null) services = new List<ServiceDefinition>();
            foreach (var service in services)
            {
                service.Normalize();
            }
        }
    }

    internal sealed class ServiceTabConfig
    {
        public string label { get; set; }
        public string tag { get; set; }
    }

    internal sealed class AppLinksConfig
    {
        public string localUrl { get; set; }
        public string remoteUrl { get; set; }
        public string logsPath { get; set; }
    }

    internal sealed class ServiceDefinition
    {
        public string id { get; set; }
        public string name { get; set; }
        public string endpoint { get; set; }
        public List<string> tags { get; set; }
        public ServiceHealthConfig health { get; set; }
        public ServiceActionConfig start { get; set; }
        public ServiceActionConfig stop { get; set; }

        public void Normalize()
        {
            if (tags == null) tags = new List<string>();
            if (health == null) health = new ServiceHealthConfig();
        }
    }

    internal sealed class ServiceHealthConfig
    {
        public string type { get; set; }
        public string url { get; set; }
        public string tlsCaFile { get; set; }
        public string processName { get; set; }
        public int timeoutMs { get; set; }
        public int okStatusMax { get; set; }
        public List<string> requiredText { get; set; }
        public List<string> commandLineContainsAny { get; set; }
    }

    internal sealed class ServiceActionConfig
    {
        public string type { get; set; }
        public string file { get; set; }
        public string workingDirectory { get; set; }
        public string script { get; set; }
        public List<string> args { get; set; }
        public int timeoutSeconds { get; set; }
        public bool detached { get; set; }
    }
}

