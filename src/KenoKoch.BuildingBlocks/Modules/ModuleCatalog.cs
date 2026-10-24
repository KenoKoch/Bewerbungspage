using System.Reflection;

namespace KenoKoch.BuildingBlocks.Modules;

/// <summary>Laufzeit-Beschreibung eines registrierten Moduls (Grundlage für den Modul-Explorer).</summary>
public sealed record ModuleDescriptor(IModule Module, IReadOnlyCollection<Type> HandledEvents)
{
    public string Name => Module.Name;

    public string DisplayName => Module.DisplayName;

    public string Description => Module.Description;

    public string? DatabaseSchema => Module.DatabaseSchema;

    public IReadOnlyCollection<Type> PublishedEvents => Module.PublishedEvents;

    public Assembly Assembly => Module.GetType().Assembly;

    public string AssemblyName => Assembly.GetName().Name ?? Name;

    public string Version
    {
        get
        {
            var informational = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = informational ?? Assembly.GetName().Version?.ToString() ?? "1.0.0";
            var plus = version.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? version[..plus] : version;
        }
    }
}

/// <summary>Verzeichnis aller beim Start registrierten Module.</summary>
public sealed class ModuleCatalog
{
    private readonly List<ModuleDescriptor> _modules = [];

    public IReadOnlyList<ModuleDescriptor> Modules => _modules;

    public ModuleDescriptor? Find(string name) =>
        _modules.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    internal void Add(ModuleDescriptor descriptor)
    {
        if (Find(descriptor.Name) is not null)
        {
            throw new InvalidOperationException($"Das Modul '{descriptor.Name}' wurde mehrfach registriert.");
        }

        if (descriptor.DatabaseSchema is { } schema &&
            _modules.Any(m => string.Equals(m.DatabaseSchema, schema, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Das Datenbankschema '{schema}' wird bereits von einem anderen Modul verwendet.");
        }

        _modules.Add(descriptor);
    }
}

/// <summary>Endpoint-Metadaten, über die sich jeder API-Endpunkt seinem Modul zuordnen lässt.</summary>
public sealed record ModuleEndpointMetadata(string ModuleName);
