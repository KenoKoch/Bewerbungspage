using System.Reflection;
using KenoKoch.BuildingBlocks.Modules;
using KenoKoch.Modules.Analytics;
using KenoKoch.Modules.Guestbook;
using KenoKoch.Modules.Guestbook.Contracts;
using KenoKoch.Modules.Plc;
using KenoKoch.Modules.Plc.Contracts;
using KenoKoch.Modules.Profile;
using KenoKoch.UI.Components;

namespace KenoKoch.Tests.Architecture;

/// <summary>
/// Architekturregeln des modularen Monolithen. Schlägt ein Test fehl, hat jemand eine Modulgrenze verletzt.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(ProfileModule).Assembly,
        typeof(GuestbookModule).Assembly,
        typeof(AnalyticsModule).Assembly,
        typeof(PlcModule).Assembly,
    ];

    private static readonly Assembly[] ContractAssemblies =
    [
        typeof(GuestbookEntrySubmitted).Assembly,
        typeof(PlcStarted).Assembly,
    ];

    public static TheoryData<string> Modules => new(ModuleAssemblies.Select(a => a.GetName().Name!));

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_references_only_building_blocks_ui_and_contracts(string moduleName)
    {
        var module = ModuleAssemblies.Single(a => a.GetName().Name == moduleName);

        var forbidden = KenoKochReferences(module)
            .Where(name => name != "KenoKoch.BuildingBlocks"
                           && name != "KenoKoch.UI"
                           && !name.EndsWith(".Contracts", StringComparison.Ordinal))
            .ToArray();

        Assert.True(forbidden.Length == 0, $"{moduleName} referenziert unerlaubt: {string.Join(", ", forbidden)}");
    }

    [Fact]
    public void Contracts_depend_only_on_building_blocks()
    {
        foreach (var contracts in ContractAssemblies)
        {
            var references = KenoKochReferences(contracts).ToArray();
            Assert.All(references, name => Assert.Equal("KenoKoch.BuildingBlocks", name));
        }
    }

    [Fact]
    public void Contracts_contain_only_integration_events()
    {
        foreach (var type in ContractAssemblies.SelectMany(a => a.GetExportedTypes()))
        {
            Assert.True(typeof(KenoKoch.BuildingBlocks.Events.IIntegrationEvent).IsAssignableFrom(type),
                $"{type.FullName} in einem Contracts-Projekt ist kein Integration Event.");
            Assert.True(type.IsSealed, $"{type.Name} sollte sealed sein.");
        }
    }

    [Fact]
    public void Building_blocks_and_ui_know_no_module()
    {
        var foundation = new[] { typeof(IModule).Assembly, typeof(IconSet).Assembly };

        foreach (var assembly in foundation)
        {
            Assert.DoesNotContain(KenoKochReferences(assembly), name => name.StartsWith("KenoKoch.Modules.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Each_module_assembly_contains_exactly_one_module()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var modules = assembly.GetTypes().Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract).ToArray();
            Assert.Single(modules);
        }
    }

    [Fact]
    public void Module_names_and_schemas_are_unique_and_lowercase()
    {
        IModule[] modules = [new ProfileModule(), new GuestbookModule(), new AnalyticsModule(), new PlcModule()];

        Assert.Equal(modules.Length, modules.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var schemas = modules.Select(m => m.DatabaseSchema).OfType<string>().ToArray();
        Assert.Equal(schemas.Length, schemas.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(schemas, schema => Assert.Equal(schema.ToLowerInvariant(), schema));
    }

    [Fact]
    public void Published_events_are_declared_in_contracts()
    {
        IModule[] modules = [new ProfileModule(), new GuestbookModule(), new AnalyticsModule(), new PlcModule()];

        foreach (var eventType in modules.SelectMany(m => m.PublishedEvents))
        {
            Assert.Contains(eventType.Assembly, ContractAssemblies);
        }
    }

    private static IEnumerable<string> KenoKochReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("KenoKoch.", StringComparison.Ordinal));
}
