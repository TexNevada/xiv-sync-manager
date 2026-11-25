using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Dalamud.Plugin;

namespace XivSyncManager;

// Contracts traced from the native menus; see SYNCSHELLS.md. No character or pair operations.
internal sealed class SyncshellIntegration(SyncProvider provider, object plugin, IServiceProvider services, string prefix, object manager, object api)
{
    private const string PlayerPause = "PlayerSync's native shell pause changes per-player pause reasons. Use its own menu if you intend to do that.";
    private const string SnowProfile = "Snowcloak does not expose a syncshell profile viewer or editor.";
    private static readonly ConditionalWeakTable<Type, PermissionAccess[]> PermissionMembers = new();
    private readonly object controller = api;

    internal static SyncshellProviderView Read(SyncProvider provider, IReadOnlyList<IExposedPlugin> installed)
    {
        try
        {
            var native = Find(provider, installed);
            if (native == null) return new(false, "Plugin not loaded.", []);
            var connection = native.Connection();
            var groups = native.Groups();
            var result = new List<SyncshellRow>(groups.Count);
            var profile = groups.Count == 0 ? default : native.ProfileAccess(connection);
            foreach (var (gid, dto) in groups)
            {
                var permissions = ReflectionAccess.Read(dto, "GroupUserPermissions");
                var flags = Permissions(permissions);
                var supportedLookup = provider != SyncProvider.Lightless || native.LightlessGroupLookup() != null;
                var supportedRequest = supportedLookup && native.PermissionRequest(dto, permissions) != null
                    && (!native.NeedsFreshGroupRead || native.FreshGroupEndpoint() != null);
                var profileSetting = profile.Setting;
                if (profile.Endpoint is { } viewer && (!supportedLookup || viewer.Message.GetConstructor([native.ProfilePayload(dto).GetType()]) == null))
                    profileSetting = Unsupported("This version does not expose the native shell profile target.");
                var settings = new SyncshellSetting[Syncshells.Controls.Length];
                foreach (var control in Syncshells.Controls)
                {
                    settings[(int)control] = control == SyncshellControl.Profile
                        ? profileSetting : native.PermissionSetting(control, permissions, flags, supportedRequest, connection);
                }
                result.Add(new(new(provider, gid), Name(dto, gid), connection, settings));
            }
            if (!connection.SameSnapshot(native.Connection()))
                return new(false, "Sync connection is changing; waiting for its joined-shell list.", []);
            if (!connection.Connected && result.Count == 0)
                return new(false, "Disconnected; no joined-shell list is available.", []);
            return new(true, connection.Connected ? null : "Disconnected; showing the plugin's existing list (read-only).", result.ToArray());
        }
        catch (Exception exception)
        {
            return new(false, $"Joined-shell list unavailable: {ReflectionAccess.Unwrap(exception).Message}", []);
        }
    }

    internal static (Task Request, Func<bool>? ProfileOpened) Start(SyncshellRow row, SyncshellControl control, IEnumerable<IExposedPlugin> installed)
    {
        var native = Find(row.Key.Provider, installed);
        var connection = native?.Connection();
        if (native == null || connection == null || !row.Connection.Matches(connection))
            throw new InvalidOperationException("The sync connection changed. Refresh before trying again.");
        var dto = native.FindGroup(row.Key.Gid) ?? throw new InvalidOperationException("This shell is no longer joined.");
        if (control == SyncshellControl.Profile)
            return native.OpenProfile(dto, row.Key.Gid, connection);
        if (control == SyncshellControl.Pause && row.Key.Provider == SyncProvider.PlayerSync)
            throw new NotSupportedException(PlayerPause);

        return (native.NeedsFreshGroupRead
            ? native.SendWithFreshGroup(row, control, connection)
            : native.SendPermission(row, control, connection, dto), null);
    }

    private bool NeedsFreshGroupRead => provider == SyncProvider.PlayerSync
        || (provider == SyncProvider.Snowcloak && ReflectionAccess.Read(controller, "ChatPairStateReady") is not bool);

    private MethodInfo? FreshGroupEndpoint() => ReflectionAccess.Method(controller, "GroupsGetAll", 0) is { } method
        && method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
        && typeof(System.Collections.IEnumerable).IsAssignableFrom(method.ReturnType.GetGenericArguments()[0]) ? method : null;

    // These clients can retain the old group list after the new transport is connected.
    // Read through the installed provider only on click; never store this snapshot or replay a write.
    private async Task SendWithFreshGroup(SyncshellRow row, SyncshellControl control, SyncshellConnection connection)
    {
        var endpoint = FreshGroupEndpoint() ?? throw new NotSupportedException("Fresh native shell permissions are unavailable in this version.");
        var read = (Task)endpoint.Invoke(controller, null)!;
        _ = read.ContinueWith(static task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        await read.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        if (!connection.Matches(Connection()))
            throw new InvalidOperationException("The sync connection changed. The request was not sent.");
        var dto = ReflectionAccess.Values(ReflectionAccess.Read(read, "Result")!)
            .SingleOrDefault(group => GroupId(group) == row.Key.Gid)
            ?? throw new InvalidOperationException("This shell is no longer joined.");
        await SendPermission(row, control, connection, dto).ConfigureAwait(false);
    }

    private Task SendPermission(SyncshellRow row, SyncshellControl control, SyncshellConnection connection, object dto)
    {
        var permissions = ReflectionAccess.Read(dto, "GroupUserPermissions");
        var flag = Permissions(permissions)[(int)control];
        var request = PermissionRequest(dto, permissions)
            ?? throw new NotSupportedException("The provider's shell permission request is unsupported in this version.");
        if (permissions == null || flag.Read == null || flag.Write == null)
            throw new NotSupportedException("The provider's shell permission flag is unsupported in this version.");
        var current = (bool)flag.Read.Invoke(null, [permissions])!;
        if ((control == SyncshellControl.Pause ? current : !current) != row[control].Value)
            throw new InvalidOperationException("This setting changed in the provider. Refresh before trying again.");

        // Native shell setters preserve all other bits. Never apply individual-pair Sticky semantics here.
        object?[] change = [permissions, !current];
        flag.Write.Invoke(null, change);
        object?[] userArgs = [.. request.User.GetParameters().Select(p => p.DefaultValue)];
        userArgs[0] = connection.Uid;
        var group = ReflectionAccess.Read(dto, "Group");
        var payload = request.Dto.Invoke([group, request.User.Invoke(userArgs), change[0]]);
        if (!connection.Matches(Connection()))
            throw new InvalidOperationException("The sync connection changed. The request was not sent.");
        return request.Method.Invoke(controller, [payload]) as Task
            ?? throw new InvalidOperationException("The provider did not return a shell request to confirm.");
    }

    private static SyncshellIntegration? Find(SyncProvider provider, IEnumerable<IExposedPlugin> installed)
    {
        var exposed = installed.FirstOrDefault(p => p.IsLoaded && (provider switch
        {
            SyncProvider.Lightless => p.InternalName.Equals("LightlessSync", StringComparison.OrdinalIgnoreCase),
            SyncProvider.Snowcloak => p.InternalName.Equals("Snowcloak", StringComparison.OrdinalIgnoreCase),
            _ => p.InternalName.Equals("MareSempiterne", StringComparison.OrdinalIgnoreCase)
                || p.InternalName.Equals("PlayerSync", StringComparison.OrdinalIgnoreCase),
        }));
        if (exposed == null) return null;
        var plugin = ReflectionAccess.GetPluginInstance(exposed);
        var services = ReflectionAccess.GetServices(plugin);
        var assembly = plugin.GetType().Assembly;
        var prefix = provider switch
        {
            SyncProvider.Lightless => "LightlessSync",
            SyncProvider.Snowcloak => "Snowcloak",
            _ => assembly.GetType("MareSynchronos.PlayerData.Pairs.PairManager") != null ? "MareSynchronos" : "PlayerSync",
        };
        object Get(string name)
        {
            var type = assembly.GetType($"{prefix}.{name}")
                ?? throw new NotSupportedException("This plugin version's syncshell integration is unsupported.");
            return services.GetService(type) ?? throw new InvalidOperationException("The plugin's shell services are not ready.");
        }
        return new(provider, plugin, services, prefix, Get("PlayerData.Pairs.PairManager"), Get("WebAPI.ApiController"));
    }

    private SyncshellConnection Connection()
    {
        var connected = ReflectionAccess.Read(controller, "IsConnected") is true;
        var session = connected ? ReflectionAccess.Read(controller, provider switch
        {
            SyncProvider.Lightless => "ConnectionSession",
            SyncProvider.Snowcloak => "_snowHub",
            _ => "_mareHub",
        }) : null;
        // SignalR can reuse the hub object on reconnect. Its connection ID changes,
        // unlike immutable profile/connection DTOs which can be replaced during a healthy session.
        var connectionId = provider == SyncProvider.Lightless ? null : ReflectionAccess.Read(session, "ConnectionId") as string;
        var ready = provider == SyncProvider.Lightless || !string.IsNullOrEmpty(connectionId);
        if (provider == SyncProvider.Snowcloak && ReflectionAccess.Read(controller, "ChatPairStateReady") is false) ready = false;
        return new(plugin, session, connectionId, connected ? ReflectionAccess.Text(ReflectionAccess.Read(controller, "UID")) : string.Empty, connected, ready);
    }

    private Dictionary<string, object> Groups()
    {
        var collection = provider == SyncProvider.Lightless
            ? ReflectionAccess.Read(ReflectionAccess.Method(manager, "GetCollectionView", 0)?.Invoke(manager, null), "GroupsByGid")
            : ReflectionAccess.Read(manager, "Groups");
        var values = ReflectionAccess.Read(collection, "Values")
            ?? throw new NotSupportedException("The native joined-shell collection is not exposed by this version.");
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var dto in ReflectionAccess.Values(values))
        {
            var gid = GroupId(dto);
            if (string.IsNullOrWhiteSpace(gid)) throw new NotSupportedException("The provider did not expose a native shell ID.");
            result.Add(gid, dto);
        }
        return result;
    }

    private static string? GroupId(object dto) => ReflectionAccess.Read(ReflectionAccess.Read(dto, "Group"), "GID") as string;

    private MethodInfo? LightlessGroupLookup() => ReflectionAccess.Method(manager, "TryGetGroupByGid", 2) is { } lookup
        && lookup.ReturnType == typeof(bool) && lookup.GetParameters() is [var gid, var dto]
        && gid.ParameterType == typeof(string) && dto.IsOut ? lookup : null;

    private object? FindGroup(string gid)
    {
        if (provider != SyncProvider.Lightless) return Groups().GetValueOrDefault(gid);
        var lookup = LightlessGroupLookup() ?? throw new NotSupportedException("This Lightless version does not expose native shell lookup by ID.");
        object?[] args = [gid, null];
        return lookup.Invoke(manager, args) is true && ReflectionAccess.Read(ReflectionAccess.Read(args[1], "Group"), "GID") as string == gid
            ? args[1] : null;
    }

    private static string Name(object dto, string fallback) => ReflectionAccess.Read(dto, "GroupAliasOrGID") is string name
        && !string.IsNullOrWhiteSpace(name) ? name : fallback;

    private SyncshellSetting PermissionSetting(SyncshellControl control, object? permissions, PermissionAccess[] flags, bool canWrite, SyncshellConnection connection)
    {
        var flag = flags[(int)control];
        var native = flag.Read?.Invoke(null, [permissions]) as bool?;
        var value = control == SyncshellControl.Pause ? native : !native;
        if (control == SyncshellControl.Pause && provider == SyncProvider.PlayerSync) return Unsupported(PlayerPause, value);
        if (value == null || flag.Write == null || !canWrite)
            return Unsupported("This plugin version does not expose this native shell control and readback.", value);
        return connection.CanAct ? new(value, SyncshellAvailability.Available)
            : Unavailable(connection.Connected ? "Waiting for the native sync connection to be ready." : "Connect this sync plugin to change its shell settings.", value);
    }

    private static readonly PermissionAccess[] NoPermissions = new PermissionAccess[Syncshells.Controls.Length];
    private static PermissionAccess[] Permissions(object? permissions) => permissions?.GetType() is { IsEnum: true } type
        ? PermissionMembers.GetValue(type, static type =>
        {
            var methods = type.Assembly.GetTypes().Where(t => t.IsAbstract && t.IsSealed)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static)).ToArray();
            MethodInfo? FindFlag(string name, bool setter) => methods.FirstOrDefault(m =>
                m.Name == name && m.ReturnType == (setter ? typeof(void) : typeof(bool)) && m.GetParameters() is var p
                && p.Length == (setter ? 2 : 1) && p[0].ParameterType == (setter ? type.MakeByRefType() : type)
                && (!setter || p[1].ParameterType == typeof(bool)));
            return [.. new[] { "Paused", "DisableSounds", "DisableVFX", "DisableAnimations" }
                .Select(suffix => new PermissionAccess(FindFlag("Is" + suffix, false), FindFlag("Set" + suffix, true)))];
        }) : NoPermissions;

    private PermissionEndpoint? PermissionRequest(object dto, object? permissions)
    {
        var groupType = ReflectionAccess.Read(dto, "Group")?.GetType();
        if (groupType == null || permissions == null) return null;
        var method = ReflectionAccess.Method(controller, "GroupChangeIndividualPermissionState", 1);
        if (method == null || method.ReturnType != typeof(Task)) return null;
        foreach (var constructor in method.GetParameters()[0].ParameterType.GetConstructors())
        {
            if (constructor.GetParameters() is not [var group, var user, var flags]
                || group.ParameterType != groupType || flags.ParameterType != permissions.GetType()) continue;
            var userConstructor = user.ParameterType.GetConstructors().FirstOrDefault(c => c.GetParameters() is { Length: > 0 } p
                && p[0].ParameterType == typeof(string) && p[0].Name == "UID" && p.Skip(1).All(arg => arg.HasDefaultValue));
            if (userConstructor != null) return new(method, constructor, userConstructor);
        }
        return null;
    }

    private (SyncshellSetting Setting, ProfileEndpoint? Endpoint) ProfileAccess(SyncshellConnection connection)
    {
        if (provider == SyncProvider.Snowcloak) return (Unsupported(SnowProfile), null);
        try
        {
            var assembly = plugin.GetType().Assembly;
            Type? TypeOf(string name) => assembly.GetType($"{prefix}.{name}");
            var lightless = provider == SyncProvider.Lightless;
            var mediatorType = TypeOf(lightless ? "Services.Mediator.LightlessMediator" : "Services.Mediator.MareMediator");
            var message = TypeOf(lightless ? "Services.Mediator.GroupProfileOpenStandaloneMessage" : "Services.Mediator.OpenSyncshellProfilePanel");
            var lifecycle = TypeOf(lightless ? "LightlessPlugin" : "MarePlugin");
            var uiType = TypeOf("Services.UiService");
            var publish = mediatorType?.GetMethods().FirstOrDefault(m => m.Name == "Publish" && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 1);
            if (mediatorType == null || message == null || lifecycle == null || uiType == null || publish == null)
                return (Unsupported("This version does not expose the native shell profile viewer contract."), null);
            if (!connection.CanAct) return (Unavailable("Connect this sync plugin to open its shell profile."), null);

            var mediator = services.GetService(mediatorType);
            var runtime = services.GetService(lifecycle);
            object? windowSystem = null;
            void ResolveWindows(object scope)
            {
                if (ReflectionAccess.Read(scope, "ServiceProvider") is IServiceProvider scoped)
                    windowSystem = ReflectionAccess.Read(scoped.GetService(uiType), "_windowSystem");
            }
            if (lightless)
            {
                if (ReflectionAccess.Read(runtime, "_runtimeLifecycle") is { } nativeLifecycle)
                    ReflectionAccess.Method(nativeLifecycle, "TryUseCurrent", 1)?.Invoke(nativeLifecycle, [(Action<object>)ResolveWindows]);
            }
            else if (ReflectionAccess.Read(runtime, "_runtimeServiceScope") is { } scope) ResolveWindows(scope);
            if (mediator == null || windowSystem == null || ReflectionAccess.Read(windowSystem, "Windows") == null)
                return (Unavailable("The native shell profile window is not ready."), null);
            return (new(null, SyncshellAvailability.Available), new(mediator, publish.MakeGenericMethod(message), message, windowSystem));
        }
        catch (Exception exception)
        {
            return (Unavailable($"Native profile viewer unavailable: {ReflectionAccess.Unwrap(exception).Message}"), null);
        }
    }

    private (Task, Func<bool>) OpenProfile(object dto, string gid, SyncshellConnection connection)
    {
        var (setting, endpoint) = ProfileAccess(connection);
        if (endpoint == null) throw new InvalidOperationException(setting.Reason);
        var lightless = provider == SyncProvider.Lightless;
        var payload = ProfilePayload(dto);
        var constructor = endpoint.Message.GetConstructor([payload.GetType()])
            ?? throw new NotSupportedException("This version's native shell profile message has changed.");
        IEnumerable<object> Windows() => ReflectionAccess.Values(ReflectionAccess.Read(endpoint.WindowSystem, "Windows")!);
        string? WindowGid(object window) => window.GetType().Name == (lightless ? "StandaloneProfileUi" : "SyncshellProfileUi")
            ? ReflectionAccess.Read(ReflectionAccess.Read(window, lightless ? "ProfileGroupData" : "GroupFullInfo"), "GID") as string
            : null;
        // PlayerSync registers native windows by display alias even though it selects the DTO by GID.
        if (!lightless && Windows().Any(window => WindowGid(window) is { } other && other != gid
            && ReflectionAccess.Read(window, "WindowName") as string == "Syncshell Profile for " + Name(dto, gid)))
            throw new InvalidOperationException("Close the other same-named shell profile in PlayerSync first.");
        if (!connection.Matches(Connection())) throw new InvalidOperationException("The sync connection changed; the profile was not requested.");
        endpoint.Publish.Invoke(endpoint.Mediator, [constructor.Invoke([payload])]);
        return (Task.CompletedTask, () => Windows().Any(window => WindowGid(window) == gid && ReflectionAccess.Read(window, "IsOpen") is true));
    }

    private object ProfilePayload(object dto) => provider == SyncProvider.Lightless
        ? ReflectionAccess.Read(dto, "Group") ?? throw new NotSupportedException("The native shell profile target is unavailable.") : dto;

    private static SyncshellSetting Unsupported(string reason, bool? value = null) => new(value, SyncshellAvailability.Unsupported, reason);
    private static SyncshellSetting Unavailable(string reason, bool? value = null) => new(value, SyncshellAvailability.Unavailable, reason);
    private readonly record struct PermissionAccess(MethodInfo? Read, MethodInfo? Write);
    private sealed record PermissionEndpoint(MethodInfo Method, ConstructorInfo Dto, ConstructorInfo User);
    private sealed record ProfileEndpoint(object Mediator, MethodInfo Publish, Type Message, object WindowSystem);
}
