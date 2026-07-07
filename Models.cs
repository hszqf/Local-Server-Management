namespace LocalServiceManager
{
    internal sealed class ManagedService
    {
        public readonly ServiceDefinition Definition;
        public readonly string Id;
        public readonly string Name;
        public readonly string Endpoint;
        public readonly string[] Tags;

        public ManagedService(ServiceDefinition definition, string endpoint)
        {
            Definition = definition;
            Id = definition.id ?? "";
            Name = definition.name ?? Id;
            Endpoint = endpoint ?? "";
            Tags = definition.tags == null ? new string[0] : definition.tags.ToArray();
        }

        public bool HasTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return true;
            foreach (var item in Tags)
            {
                if (string.Equals(item, tag, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }

    internal sealed class ManagedServiceStatus
    {
        public ManagedService Service;
        public bool Running;
        public string State;
        public string Detail;

        public ManagedServiceStatus(ManagedService service, bool running, string state, string detail)
        {
            Service = service;
            Running = running;
            State = state;
            Detail = detail;
        }
    }
}

