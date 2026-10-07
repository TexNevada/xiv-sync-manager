using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;

namespace XivSyncManager;

internal static class IntegrationDiagnostics
{
    // Required roles and optional callers were inspected in the supported installed plugins.
    // Only advertise optional callers actually exposed by this version's IpcManager.
    private sealed record Definition(string Property, string Name, string InternalName, string Feature,
        bool Required = false, string? SearchTerm = null);

    private static readonly Definition[] Definitions =
    [
        new("Penumbra", "Penumbra", "Penumbra", "Loads shared character mods.", true),
        new("Glamourer", "Glamourer", "Glamourer", "Applies shared appearance and equipment.", true),
        new("Heels", "Simple Heels", "SimpleHeels", "Shares character height and position offsets.", SearchTerm: "Simple Heels"),
        new("CustomizePlus", "Customize+", "CustomizePlus", "Shares body scaling and customization."),
        new("Honorific", "Honorific", "Honorific", "Shares custom character titles."),
        new("Moodles", "Moodles", "Moodles", "Shares custom status effects."),
        new("PetNames", "Pet Nicknames", "PetRenamer", "Shares custom pet names.", SearchTerm: "Pet"),
        new("Brio", "Brio", "Brio", "Adds character spawning and posing for GPose features."),
        new("Pulsar", "Pulsar", "Pulsar", "Adds shared Pulsar character data."),
        new("LivePose", "LivePose (Simple Heels)", "SimpleHeels", "Adds live pose sharing through Simple Heels.", SearchTerm: "Simple Heels"),
        new("Lifestream", "Lifestream", "Lifestream", "Adds travel and housing shortcuts."),
        new("Stagehand", "Stagehand", "Stagehand", "Adds shared scene objects and layouts."),
        new("Intoner", "Intoner", "Intoner", "Adds Intoner features supported by this sync."),
        new("Loci", "Loci", "Loci", "Shares custom status effects."),
    ];

    internal static IntegrationReport Read(object plugin, SyncProvider provider, IReadOnlyList<IExposedPlugin> installed)
    {
        try
        {
            var services = ReflectionAccess.GetServices(plugin);
            var assembly = plugin.GetType().Assembly;
            var prefixes = provider switch
            {
                SyncProvider.Lightless => new[] { "LightlessSync" },
                SyncProvider.Snowcloak => ["Snowcloak"],
                _ => ["MareSynchronos", "PlayerSync"],
            };
            var type = prefixes.Select(p => assembly.GetType($"{p}.Interop.Ipc.IpcManager")).FirstOrDefault(t => t != null);
            var manager = type == null ? null : services.GetService(type);
            if (manager == null)
                return new([], "This sync's plugin checks are unavailable. Check its own settings for plugin requirements.");

            var results = new List<PluginIntegration>();
            foreach (var definition in Definitions)
            {
                var property = manager.GetType().GetProperty(definition.Property, ReflectionAccess.Members);
                var intoner = definition.Property == "Intoner" && provider == SyncProvider.Lightless
                    ? ReflectionAccess.Method(manager, "FindService", 1) : null;
                if (property == null && intoner == null && !definition.Required) continue;
                try
                {
                    var caller = property != null ? property.GetValue(manager)
                        : intoner?.Invoke(manager, ["Intoner"]);
                    results.Add(ReadCaller(caller, definition, installed));
                }
                catch
                {
                    // A changed diagnostics contract must not interrupt pair or connection management.
                    results.Add(Unknown(definition));
                }
            }

            if (provider == SyncProvider.PlayerSync) CombineStatusEffects(results);
            return new(results);
        }
        catch
        {
            return new([], "Plugin checks are unavailable right now. Check this sync's own settings or try again after it finishes loading.");
        }
    }

    private static PluginIntegration ReadCaller(object? caller, Definition definition, IReadOnlyList<IExposedPlugin> installed)
    {
        if (caller == null) return Unknown(definition);
        var native = ReflectionAccess.Read(caller, "Status");
        var descriptor = ReflectionAccess.Read(caller, "Descriptor");
        var internalName = ReflectionAccess.Text(ReflectionAccess.Read(descriptor, "InternalName"));
        if (string.IsNullOrEmpty(internalName)) internalName = definition.InternalName;
        var exposed = installed.FirstOrDefault(p => p.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));
        var available = ReflectionAccess.Read(caller, "APIAvailable") ?? ReflectionAccess.Read(native, "IsAvailable");
        // A native successful check takes precedence: some plugins support alternative backends.
        if (available is true)
            return Result(IntegrationState.Ready, "This sync reports the integration is ready.");
        if (available is not false) return Unknown(definition);

        var state = ReflectionAccess.Text(ReflectionAccess.Read(native, "State") ?? ReflectionAccess.Read(caller, "State"));
        var minimum = ReflectionAccess.Text(ReflectionAccess.Read(native, "RequiredVersion")
            ?? ReflectionAccess.Read(descriptor, "MinimumVersion"));
        return state switch
        {
            "MissingPlugin" or "Missing" => Result(IntegrationState.Missing, "Install this plugin to use this feature."),
            "PluginDisabled" or "Disabled" => Result(IntegrationState.Disabled, "Enable this plugin in the Dalamud plugin installer."),
            "VersionMismatch" => Result(IntegrationState.Incompatible, "Update this plugin to a version supported by the sync."
                + (string.IsNullOrWhiteSpace(minimum) ? string.Empty : $" Required: {minimum}.")),
            "NotReady" => Result(IntegrationState.NotReady, "The plugin is still loading or its integration is disabled. Check its settings."),
            "Error" => Result(IntegrationState.NotReady, "This sync could not use the plugin. Check the plugin's settings and any update or error messages."),
            "Unknown" => Unknown(definition),
            _ when exposed == null => Result(IntegrationState.Missing, "Install this plugin to use this feature."),
            _ when !exposed.IsLoaded => Result(IntegrationState.Disabled, "The plugin is installed but not loaded. Enable it or resolve its load error in the Dalamud plugin installer."),
            _ => Result(IntegrationState.NotReady, "The plugin is loaded, but this sync cannot use it yet. Check for updates and enable its integration in the plugin's settings."),
        };

        PluginIntegration Result(IntegrationState value, string explanation) => new(definition.Name, definition.Required,
            definition.Feature, value, explanation, exposed?.Name ?? definition.SearchTerm ?? definition.Name);
    }

    private static PluginIntegration Unknown(Definition definition) => new(definition.Name, definition.Required,
        definition.Feature, IntegrationState.Unknown, "Its status could not be read. Check the sync plugin's own settings.",
        definition.SearchTerm ?? definition.Name);

    private static void CombineStatusEffects(List<PluginIntegration> plugins)
    {
        var moodles = plugins.FirstOrDefault(p => p.Name == "Moodles");
        var loci = plugins.FirstOrDefault(p => p.Name == "Loci");
        if (moodles == null || loci == null) return;
        var ready = new[] { moodles, loci }.Count(p => p.State == IntegrationState.Ready);
        var unknown = moodles.State == IntegrationState.Unknown || loci.State == IntegrationState.Unknown;
        var result = unknown
            ? new PluginIntegration("Moodles / Loci", false, moodles.Feature, IntegrationState.Unknown,
                $"You only need one. Moodles: {moodles.StatusLabel}. Loci: {loci.StatusLabel}. Check PlayerSync's own settings to confirm compatibility.", "")
            : ready == 2
            ? new PluginIntegration("Moodles / Loci", false, moodles.Feature, IntegrationState.Conflict,
                "PlayerSync supports one at a time. Disable either Moodles or Loci in the Dalamud plugin installer.", "")
            : ready == 1
                ? new PluginIntegration("Moodles / Loci", false, moodles.Feature, IntegrationState.Ready,
                    $"{(moodles.State == IntegrationState.Ready ? "Moodles" : "Loci")} is ready. You only need one.", "")
                : new PluginIntegration("Moodles / Loci", false, moodles.Feature,
                    moodles.State == loci.State ? moodles.State : IntegrationState.NotReady,
                    $"You only need one. Moodles: {moodles.StatusLabel}. Loci: {loci.StatusLabel}. Check or install your preferred one.", "");
        var index = plugins.IndexOf(moodles);
        plugins.Remove(loci);
        plugins[index] = result;
    }
}
