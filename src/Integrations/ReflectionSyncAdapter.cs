using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Plugin;

namespace XivSyncManager;

internal sealed class ReflectionSyncAdapter(SyncProvider provider)
{
    internal const string HoldSource = "XIV Sync Manager";
    private object? instance;
    private object? pairManager;
    private object? api;
    private ProfileIntegration? profiles;
    private string scope = string.Empty;
    private object? observedServer;
    private readonly Dictionary<(Type Type, string Name), MethodInfo?> permissionExtensions = [];
    private readonly HashSet<Type> validatedPausePermissions = [];
    private readonly HashSet<Type> validatedMediaPermissions = [];

    internal SyncProvider Provider { get; } = provider;
    internal bool Connected { get; private set; }
    internal bool LocalHolds { get; private set; }
    internal ProviderStatus Status { get; private set; } = new() { Provider = provider, Description = "Not loaded" };

    internal List<PairSnapshot> Refresh(IEnumerable<IExposedPlugin> plugins)
    {
        var pairs = new List<PairSnapshot>();
        foreach (var step in RefreshIncrementally(plugins, pairs)) { }
        return pairs;
    }

    internal IEnumerable<string> RefreshIncrementally(IEnumerable<IExposedPlugin> plugins, List<PairSnapshot> pairs)
    {
        var installed = plugins as IReadOnlyList<IExposedPlugin> ?? plugins.ToArray();
        Connected = false;
        observedServer = null;
        var exposed = installed.FirstOrDefault(p => Matches(p.InternalName) && p.IsLoaded);
        if (exposed == null)
        {
            Reset();
            Status = new()
            {
                Provider = Provider, Description = "Not loaded",
                Integrations = new([], $"Install or enable {Provider.DisplayName()} to check its required and optional plugins."),
                Vram = VramUsage.Unavailable("Plugin is not loaded."),
            };
            yield break;
        }

        var integrations = new IntegrationReport([], "Plugin checks are unavailable. Check this sync's own settings.");
        var vram = VramUsage.Unavailable("VRAM statistics are unavailable.");
        object? loaded = null;
        object[] nativePairs = [];
        Exception? failure = null;
        try
        {
            loaded = ReflectionAccess.GetPluginInstance(exposed);
            if (!ReferenceEquals(instance, loaded)) Connect(loaded);
            Connected = ReflectionAccess.Boolean(ReflectionAccess.Read(api, "IsConnected"));
            if (Connected) scope = GetScope();
        }
        catch (Exception exception) { failure = exception; }
        yield return $"{Provider.DisplayName()}: read connection";

        if (loaded != null) integrations = IntegrationDiagnostics.Read(loaded, Provider, installed);
        yield return $"{Provider.DisplayName()}: integration checks";
        if (failure == null && !Connected)
        {
            var canReconnect = TryGetConnectionControl(out var reconnectError);
            Status = new()
            {
                Provider = Provider, Available = true, LocalHolds = LocalHolds, Description = "Disconnected",
                CanReconnect = canReconnect, ConnectionUnavailableReason = reconnectError,
                Integrations = integrations,
                Vram = VramUsage.Unavailable("Disconnected; no current visible-player statistics."),
            };
            yield break;
        }

        if (failure == null)
        {
            profiles?.Refresh();
            yield return $"{Provider.DisplayName()}: refresh profile cache";
            try { nativePairs = ReadPairs().ToArray(); }
            catch (Exception exception) { failure = exception; }
            yield return $"{Provider.DisplayName()}: enumerate native pairs";
        }
        if (failure == null)
        {
            vram = VramDiagnostics.Read(pairManager!, nativePairs, Provider);
            yield return $"{Provider.DisplayName()}: VRAM diagnostics";
            var pairStep = $"{Provider.DisplayName()}: read pair snapshot";
            foreach (var nativePair in nativePairs)
            {
                try { pairs.Add(ReadPair(nativePair)); }
                catch (Exception exception) { failure = exception; }
                if (failure != null) break;
                yield return pairStep;
            }
            if (failure == null)
            {
                try { EnsureCurrentConnection(); }
                catch (Exception exception) { failure = exception; }
            }
        }
        if (failure != null)
        {
            pairs.Clear();
            Connected = false;
            Status = new()
            {
                Provider = Provider,
                Description = $"Integration unavailable: {ReflectionAccess.Unwrap(failure).Message}",
                Integrations = integrations,
                Vram = vram,
            };
            yield break;
        }
        var canDisconnect = TryGetConnectionControl(out var disconnectError);
        Status = new()
        {
            Provider = Provider, Connected = true, Available = true, LocalHolds = LocalHolds,
            Description = "Connected", CanDisconnect = canDisconnect, ConnectionUnavailableReason = disconnectError,
            Integrations = integrations, Vram = vram,
        };
    }

    private bool Matches(string internalName) => Provider switch
    {
        SyncProvider.Lightless => internalName.Equals("LightlessSync", StringComparison.OrdinalIgnoreCase),
        SyncProvider.Snowcloak => internalName.Equals("Snowcloak", StringComparison.OrdinalIgnoreCase),
        _ => internalName.Equals("MareSempiterne", StringComparison.OrdinalIgnoreCase)
             || internalName.Equals("PlayerSync", StringComparison.OrdinalIgnoreCase),
    };

    private void Connect(object plugin)
    {
        Reset();
        var services = ReflectionAccess.GetServices(plugin);
        var assembly = plugin.GetType().Assembly;
        var prefixes = Provider switch
        {
            SyncProvider.Lightless => new[] { "LightlessSync" },
            SyncProvider.Snowcloak => ["Snowcloak"],
            _ => ["MareSynchronos", "PlayerSync"],
        };
        object GetService(string suffix)
        {
            var type = prefixes.Select(p => assembly.GetType($"{p}.{suffix}")).FirstOrDefault(t => t != null)
                ?? throw new NotSupportedException($"Missing sync service: {suffix}.");
            return services.GetService(type) ?? throw new NotSupportedException($"Sync service is not ready: {suffix}.");
        }

        pairManager = GetService("PlayerData.Pairs.PairManager");
        api = GetService("WebAPI.ApiController");
        profiles = new(plugin, Provider);
        instance = plugin;
    }

    private void Reset()
    {
        instance = null;
        pairManager = null;
        api = null;
        profiles = null;
        scope = string.Empty;
        observedServer = null;
        LocalHolds = false;
        permissionExtensions.Clear();
        validatedPausePermissions.Clear();
        validatedMediaPermissions.Clear();
    }

    private string GetScope()
    {
        var serverManager = ReflectionAccess.Read(api, "_serverManager");
        var server = ReflectionAccess.Read(serverManager, "CurrentServer");
        var serverUri = ReflectionAccess.Text(ReflectionAccess.Read(server, "ServerUri"));
        var localUid = ReflectionAccess.Text(ReflectionAccess.Read(api, "UID"));
        if (string.IsNullOrWhiteSpace(serverUri) || string.IsNullOrWhiteSpace(localUid))
            throw new NotSupportedException("The server or local account identity is unavailable.");
        return $"{Provider}|{Uri.EscapeDataString(serverUri.TrimEnd('/'))}|{Uri.EscapeDataString(localUid)}|";
    }

    private IEnumerable<object> ReadPairs()
    {
        foreach (var methodName in new[] { "GetPairsSnapshot", "EnumeratePairs" })
        {
            if (ReflectionAccess.Method(pairManager!, methodName, 0) is { } method)
                return ReflectionAccess.Values(method.Invoke(pairManager, null)!);
        }
        return ReflectionAccess.Values(ReflectionAccess.Read(pairManager, "_allClientPairs")
            ?? throw new NotSupportedException("The sync plugin's pair enumeration has changed."));
    }

    private PairSnapshot ReadPair(object pair)
    {
        var user = ReflectionAccess.Read(pair, "UserData")
            ?? throw new NotSupportedException("Missing pair user data.");
        var uid = ReflectionAccess.Text(ReflectionAccess.Read(user, "UID"));
        if (string.IsNullOrWhiteSpace(uid)) throw new NotSupportedException("Missing pair UID.");

        var canHold = Provider == SyncProvider.Snowcloak &&
            new[] { "HoldApplication", "UnholdApplication", "HoldDownloads", "UnholdDownloads" }
                .All(name => ReflectionAccess.Method(pair, name, 2) != null);
        if (canHold && (ReflectionAccess.Read(pair, "HoldApplicationReasons") == null
                       || ReflectionAccess.Read(pair, "HoldDownloadReasons") == null))
            throw new NotSupportedException("Snowcloak's hold ownership interface has changed.");
        LocalHolds = canHold;

        var userPair = ReflectionAccess.Read(pair, "UserPair");
        var own = ReflectionAccess.Read(userPair, "OwnPermissions");
        var other = ReflectionAccess.Read(userPair, "OtherPermissions");
        var permissions = canHold ? string.Empty : Fingerprint(own
            ?? throw new NotSupportedException("This pair does not expose editable permissions."));
        if (!canHold && !validatedPausePermissions.Contains(own!.GetType()))
        {
            _ = ChangePaused(own!, true); // Validate the API contract before enabling controls.
            _ = PermissionRequestMethod();
            validatedPausePermissions.Add(own!.GetType());
        }

        var handler = ReflectionAccess.Read(pair, "CachedPlayer") ?? ReflectionAccess.Read(pair, "Handler");
        var address = ReflectionAccess.Address(ReflectionAccess.Read(pair, "PlayerCharacter") ?? ReflectionAccess.Read(pair, "Address"));
        if (address == nint.Zero)
            address = ReflectionAccess.Address(ReflectionAccess.Read(handler, "PlayerCharacter") ?? ReflectionAccess.Read(handler, "Address"));

        var applicationReasons = Reasons(pair, "HoldApplicationReasons");
        var downloadReasons = Reasons(pair, "HoldDownloadReasons");
        var ownPaused = canHold ? ReflectionAccess.Boolean(ReflectionAccess.Read(pair, "IsPaused")) : IsPaused(own!);
        var otherPaused = !canHold && other != null && IsPaused(other);
        return new PairSnapshot
        {
            Provider = Provider, Key = scope + Uri.EscapeDataString(uid), Uid = uid,
            Label = ReflectionAccess.Text(ReflectionAccess.Read(user, "AliasOrUID")) is { Length: > 0 } alias ? alias : uid,
            Ident = ReflectionAccess.Text(ReflectionAccess.Read(pair, "Ident")),
            PlayerName = ReflectionAccess.Text(ReflectionAccess.Read(pair, "PlayerName") ?? ReflectionAccess.Read(handler, "PlayerName")),
            Address = address, Online = ReflectionAccess.Boolean(ReflectionAccess.Read(pair, "IsOnline")),
            Visible = ReflectionAccess.Boolean(ReflectionAccess.Read(pair, "IsVisible")),
            OwnPaused = ownPaused,
            OtherPaused = otherPaused,
            ExternalHold = canHold && (applicationReasons.Concat(downloadReasons).Any(r => r != HoldSource)
                                       || Reasons(pair, "AutoPauseReasons").Count != 0),
            ManagerHeld = canHold && (applicationReasons.Contains(HoldSource) || downloadReasons.Contains(HoldSource)),
            ManagerFullyHeld = canHold && applicationReasons.Contains(HoldSource) && downloadReasons.Contains(HoldSource),
            Permissions = permissions, PauseReason = GetPauseReason(pair), Pair = pair, Adapter = this,
            Profile = profiles?.Read(pair, user, ownPaused, otherPaused) ?? new(ProfileAvailability.Unknown, "Profile checks are unavailable."),
            Media = ReadMedia(own, other),
            Audio = AudioDiagnostics.Read(pair, Provider),
        };
    }

    private static List<string> Reasons(object pair, string name) =>
        ReflectionAccess.Read(pair, name) is IEnumerable reasons ? reasons.Cast<object>().Select(ReflectionAccess.Text).ToList() : [];

    private static string GetPauseReason(object pair) =>
        ReflectionAccess.Method(pair, "GetPauseReason", 0) is { } method
            ? ReflectionAccess.Text(method.Invoke(pair, null)) : string.Empty;

    internal OwnedPause PreparePause(PairSnapshot pair) => new()
    {
        CharacterIdentity = pair.CharacterIdentity ?? string.Empty,
        OriginalPermissions = pair.Permissions,
        PausedPermissions = LocalHolds ? string.Empty : Fingerprint(ChangePaused(GetOwnPermissions(pair), true)),
        OriginalPauseReason = pair.PauseReason, LocalHold = LocalHolds,
    };

    internal void OpenProfile(PairSnapshot pair)
    {
        EnsureCurrent(pair);
        if (pair.Adapter != this || !pair.Profile.CanOpen || profiles == null)
            throw new InvalidOperationException("This profile is not available to open right now.");
        if (Provider == SyncProvider.PlayerSync && (IsPaused(GetOwnPermissions(pair))
            || ReflectionAccess.Boolean(ReflectionAccess.Read(pair.Pair, "IsPaused"))
            || ReflectionAccess.Read(ReflectionAccess.Read(pair.Pair, "UserPair"), "OtherPermissions") is { } other && IsPaused(other)))
            throw new InvalidOperationException("PlayerSync hides profiles while either side of the pair is paused.");
        profiles.Open(pair.Pair);
    }

    internal Task SetPaused(PairSnapshot pair, bool paused)
    {
        EnsureCurrent(pair);
        if (LocalHolds)
        {
            if (paused)
            {
                ReflectionAccess.Invoke(pair.Pair, "HoldDownloads", HoldSource, 1);
                ReflectionAccess.Invoke(pair.Pair, "HoldApplication", HoldSource, 1);
            }
            else
            {
                ReleaseLocalHold(pair);
            }
            return Task.CompletedTask;
        }

        var currentPermissions = GetOwnPermissions(pair);
        if (Fingerprint(currentPermissions) != pair.Permissions || GetPauseReason(pair.Pair) != pair.PauseReason)
            throw new InvalidOperationException("Pair permissions changed since the last refresh; this request was not sent.");
        var permissions = ChangePaused(currentPermissions, paused);
        var method = PermissionRequestMethod();
        var dtoType = method.GetParameters()[0].ParameterType;
        var user = ReflectionAccess.Read(pair.Pair, "UserData")!;
        var dto = Activator.CreateInstance(dtoType, user, permissions)
            ?? throw new NotSupportedException("The pair permissions request has changed.");
        EnsureCurrent(pair);
        return method.Invoke(api, [dto]) as Task
            ?? throw new NotSupportedException("The pair permissions operation is no longer asynchronous.");
    }

    private MediaStatus ReadMedia(object? own, object? other)
    {
        try
        {
            if (own == null) throw new NotSupportedException("This pair has no editable media permissions.");
            _ = PermissionRequestMethod();
            if (!validatedMediaPermissions.Contains(own.GetType()))
            {
                foreach (var kind in new[] { MediaKind.Animations, MediaKind.Sounds, MediaKind.Vfx })
                    _ = ChangeMedia(own, kind, true);
                validatedMediaPermissions.Add(own.GetType());
            }
            var disabled = MediaKind.None;
            var otherDisabled = MediaKind.None;
            foreach (var kind in new[] { MediaKind.Animations, MediaKind.Sounds, MediaKind.Vfx })
            {
                var suffix = MediaSuffix(kind);
                if (PermissionExtension(own, "IsDisable" + suffix, false).Invoke(null, [own]) is true) disabled |= kind;
                if (other != null && PermissionExtension(other, "IsDisable" + suffix, false).Invoke(null, [other]) is true) otherDisabled |= kind;
            }
            return new(true, Fingerprint(own), disabled, otherDisabled,
                "Changes this pair's native sync permissions. Restrictions affect synced mods in both directions; the other player's restrictions still apply.");
        }
        catch (Exception exception)
        {
            var fingerprint = string.Empty;
            try { if (own != null) fingerprint = Fingerprint(own); }
            catch { }
            return new(false, fingerprint, MediaKind.None, MediaKind.None, ReflectionAccess.Unwrap(exception).Message);
        }
    }

    internal string MediaPermissionsAfter(PairSnapshot pair, MediaKind kind, bool disabled, string? fingerprint = null)
    {
        var permissions = GetOwnPermissions(pair);
        if (fingerprint != null) permissions = Enum.ToObject(permissions.GetType(), ulong.Parse(fingerprint, CultureInfo.InvariantCulture));
        return Fingerprint(ChangeMedia(permissions, kind, disabled));
    }

    internal string PausedPermissionsFrom(PairSnapshot pair, string fingerprint) => Fingerprint(ChangePaused(
        Enum.ToObject(GetOwnPermissions(pair).GetType(), ulong.Parse(fingerprint, CultureInfo.InvariantCulture)), true));

    internal Task SetMedia(PairSnapshot pair, MediaKind kind, bool disabled)
    {
        EnsureCurrent(pair);
        if (!pair.Media.CanEdit) throw new NotSupportedException(pair.Media.Description);
        var current = GetOwnPermissions(pair);
        if (Fingerprint(current) != pair.Media.Permissions || GetPauseReason(pair.Pair) != pair.PauseReason)
            throw new InvalidOperationException("Pair permissions changed; wait for the next update and try again.");
        var permissions = ChangeMedia(current, kind, disabled);
        var method = PermissionRequestMethod();
        var dto = Activator.CreateInstance(method.GetParameters()[0].ParameterType,
            ReflectionAccess.Read(pair.Pair, "UserData")!, permissions)
            ?? throw new NotSupportedException("The media permissions request has changed.");
        EnsureCurrent(pair);
        return method.Invoke(api, [dto]) as Task
            ?? throw new NotSupportedException("The media permissions operation is no longer asynchronous.");
    }

    private static string MediaSuffix(MediaKind kind) => kind switch
    {
        MediaKind.Animations => "Animations", MediaKind.Sounds => "Sounds", MediaKind.Vfx => "VFX",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private object ChangeMedia(object permissions, MediaKind kind, bool disabled)
    {
        object?[] args = [permissions, disabled];
        PermissionExtension(permissions, "SetDisable" + MediaSuffix(kind), true).Invoke(null, args);
        // These APIs use Sticky to preserve individual choices over syncshell defaults.
        // Snowcloak has no Sticky flag. Resolve by contract rather than assuming shared bit masks.
        if (Enum.GetNames(permissions.GetType()).Contains("Sticky"))
        {
            object?[] sticky = [args[0], true];
            PermissionExtension(permissions, "SetSticky", true).Invoke(null, sticky);
            return sticky[0]!;
        }
        return args[0]!;
    }

    internal void ReleaseLocalHold(PairSnapshot pair)
    {
        // This can also clean up our own holds during disconnection or plugin disposal.
        ReflectionAccess.Invoke(pair.Pair, "UnholdDownloads", HoldSource, true);
        ReflectionAccess.Invoke(pair.Pair, "UnholdApplication", HoldSource, false);
    }

    internal void Reapply(PairSnapshot pair)
    {
        EnsureCurrent(pair);
        var method = pair.Pair.GetType().GetMethods(ReflectionAccess.Members).FirstOrDefault(m =>
            m.Name == "ApplyLastReceivedData" && m.GetParameters() is { Length: > 0 } parameters
            && parameters[0].ParameterType == typeof(bool) && parameters.All(p => p.IsOptional));
        if (method == null) return;
        var args = method.GetParameters().Select(p => p.DefaultValue).ToArray();
        args[0] = true;
        method.Invoke(pair.Pair, args);
    }

    private void EnsureCurrent(PairSnapshot pair)
    {
        EnsureCurrentConnection();
        if (!pair.Key.StartsWith(scope, StringComparison.Ordinal))
            throw new InvalidOperationException("The sync connection changed; refresh before changing this pair.");
        var current = ReadPairs().FirstOrDefault(p => ReferenceEquals(p, pair.Pair));
        if (current == null) throw new InvalidOperationException("This pair is no longer available.");
    }

    private void EnsureCurrentConnection()
    {
        EnsureLoadedPlugin();
        if (!Connected || !IsConnectionOpen || GetScope() != scope)
            throw new InvalidOperationException("The sync connection changed; wait for the next update before retrying.");
    }

    private void EnsureLoadedPlugin()
    {
        var exposed = Plugin.PluginInterface.InstalledPlugins.FirstOrDefault(p => Matches(p.InternalName) && p.IsLoaded);
        if (exposed == null || api == null || !ReferenceEquals(ReflectionAccess.GetPluginInstance(exposed), instance))
            throw new InvalidOperationException("The sync plugin reloaded; wait for the next update before retrying.");
    }

    internal bool IsConnectionOpen => ReflectionAccess.Boolean(ReflectionAccess.Read(api, "IsConnected"));

    internal Task SetConnected(bool connected)
    {
        if (connected) EnsureLoadedPlugin();
        else EnsureCurrentConnection();
        var control = GetConnectionControl();
        EnsureLoadedPlugin();
        if (!ReferenceEquals(observedServer, control.Server)
            || !ReferenceEquals(ReflectionAccess.Read(control.ServerManager, "CurrentServer"), control.Server))
            throw new InvalidOperationException("The selected sync server changed; this connection request was not sent.");
        if (IsConnectionOpen == connected)
            throw new InvalidOperationException("The sync connection changed; wait for the next update before retrying.");

        // Mirror native connection controls. Offline reconnects use the configured server;
        // a remote account UID is only available after a successful connection.
        control.FullPause.SetValue(control.Server, !connected);
        control.UserRequestedFullPause?.SetValue(api, !connected);
        control.Save.Invoke(control.ServerManager, null);
        return control.UpdateConnection.Invoke(api, null) as Task
            ?? throw new NotSupportedException("The sync plugin's connection operation is no longer asynchronous.");
    }

    private bool TryGetConnectionControl(out string error)
    {
        try
        {
            observedServer = GetConnectionControl().Server;
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = ReflectionAccess.Unwrap(exception).Message;
            return false;
        }
    }

    private ConnectionControl GetConnectionControl()
    {
        var serverManager = ReflectionAccess.Read(api, "_serverManager")
            ?? throw new NotSupportedException("The sync plugin's server controls are unavailable.");
        var server = ReflectionAccess.Read(serverManager, "CurrentServer")
            ?? throw new NotSupportedException("The sync plugin's selected server is unavailable.");
        if (string.IsNullOrWhiteSpace(ReflectionAccess.Text(ReflectionAccess.Read(server, "ServerUri"))))
            throw new NotSupportedException("Configure a sync server in the original plugin before connecting.");
        var fullPause = server.GetType().GetProperty("FullPause", ReflectionAccess.Members);
        if (fullPause?.PropertyType != typeof(bool) || fullPause.GetMethod == null || fullPause.SetMethod == null)
            throw new NotSupportedException("The sync plugin's saved connection control has changed.");
        var save = ReflectionAccess.Method(serverManager, "Save", 0);
        if (save == null || save.ReturnType != typeof(void))
            throw new NotSupportedException("The sync plugin's connection settings save operation has changed.");
        var update = ReflectionAccess.Method(api!, "CreateConnectionsAsync", 0)
            ?? ReflectionAccess.Method(api!, "CreateConnections", 0);
        if (update == null || !typeof(Task).IsAssignableFrom(update.ReturnType))
            throw new NotSupportedException("The sync plugin's asynchronous connection operation has changed.");
        var userRequested = api!.GetType().GetProperty("UserRequestedFullPause", ReflectionAccess.Members);
        if (userRequested != null && (userRequested.PropertyType != typeof(bool) || userRequested.SetMethod == null))
            throw new NotSupportedException("The sync plugin's manual connection flag has changed.");
        return new(serverManager, server, fullPause, save, update, userRequested);
    }

    private sealed record ConnectionControl(object ServerManager, object Server, PropertyInfo FullPause,
        MethodInfo Save, MethodInfo UpdateConnection, PropertyInfo? UserRequestedFullPause);

    private MethodInfo PermissionRequestMethod()
    {
        var method = ReflectionAccess.Method(api!, "UserSetPairPermissions", 1);
        if (method == null || !typeof(Task).IsAssignableFrom(method.ReturnType))
            throw new NotSupportedException("The sync plugin's asynchronous pair pause operation has changed.");
        return method;
    }

    private static object GetOwnPermissions(PairSnapshot pair) =>
        ReflectionAccess.Read(ReflectionAccess.Read(pair.Pair, "UserPair"), "OwnPermissions")
        ?? throw new NotSupportedException("Pair permissions are unavailable.");

    internal static string Fingerprint(object permissions) =>
        Convert.ToUInt64(permissions, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private MethodInfo PermissionExtension(object permissions, string name, bool byRef)
    {
        var type = permissions.GetType();
        if (permissionExtensions.TryGetValue((type, name), out var cached))
            return cached ?? throw new NotSupportedException($"The sync API's {name} permissions extension is unavailable.");
        // Resolve the actual API's enum and extension methods. No copied bit masks or API DTO dependencies.
        var method = type.Assembly.GetTypes().Where(t => t.IsAbstract && t.IsSealed)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(m => m.Name == name && m.GetParameters() is var p
                && p.Length == (byRef ? 2 : 1) && p[0].ParameterType == (byRef ? type.MakeByRefType() : type)
                && m.ReturnType == (byRef ? typeof(void) : typeof(bool))
                && (!byRef || p[1].ParameterType == typeof(bool)));
        permissionExtensions[(type, name)] = method;
        return method ?? throw new NotSupportedException($"The sync API's {name} permissions extension is unavailable.");
    }

    private bool IsPaused(object permissions) =>
        PermissionExtension(permissions, "IsPaused", false).Invoke(null, [permissions]) is true;

    private object ChangePaused(object permissions, bool paused)
    {
        object?[] args = [permissions, paused];
        PermissionExtension(permissions, "SetPaused", true).Invoke(null, args);
        return args[0]!;
    }
}
