using Dalamud.Plugin;
using Dalamud.Plugin.Internal.Types;
using XivSyncManager;
using CoordinatorTests;

namespace CoordinatorTests
{
    // Small native API fixtures. Production code resolves their enum extension methods,
    // service hosts, pair collections, hold reasons, and permission DTOs by reflection.
    [Flags]
    public enum Permissions { None = 0, Paused = 1, Sounds = 2, Animations = 4, Vfx = 8, Sticky = 16 }
    public static class PermissionExtensions
    {
        public static bool IsPaused(this Permissions value) => value.HasFlag(Permissions.Paused);
        public static bool IsDisableSounds(this Permissions value) => value.HasFlag(Permissions.Sounds);
        public static bool IsDisableAnimations(this Permissions value) => value.HasFlag(Permissions.Animations);
        public static bool IsDisableVFX(this Permissions value) => value.HasFlag(Permissions.Vfx);
        public static void SetPaused(this ref Permissions value, bool enabled) => Set(ref value, Permissions.Paused, enabled);
        public static void SetDisableSounds(this ref Permissions value, bool enabled) => Set(ref value, Permissions.Sounds, enabled);
        public static void SetDisableAnimations(this ref Permissions value, bool enabled) => Set(ref value, Permissions.Animations, enabled);
        public static void SetDisableVFX(this ref Permissions value, bool enabled) => Set(ref value, Permissions.Vfx, enabled);
        public static void SetSticky(this ref Permissions value, bool enabled) => Set(ref value, Permissions.Sticky, enabled);
        private static void Set(ref Permissions value, Permissions bit, bool enabled) => value = enabled ? value | bit : value & ~bit;
    }
    public sealed class User(string uid)
    {
        public string UID { get; } = uid;
        public string AliasOrUID => UID;
    }
    public sealed class UserPair
    {
        public Permissions OwnPermissions { get; set; }
        public Permissions OtherPermissions { get; set; }
    }
    public sealed class PermissionRequest(User user, Permissions permissions)
    {
        public User User { get; } = user;
        public Permissions Permissions { get; } = permissions;
    }
    public class NativePair(string uid)
    {
        public User UserData { get; } = new(uid);
        public UserPair UserPair { get; } = new();
        public bool IsOnline { get; set; } = true;
        public bool IsVisible { get; set; } = true;
        public string Ident { get; set; } = "verified";
        public string PlayerName { get; set; } = "Example";
        public nint Address { get; set; } = 1;
        public string PauseReason { get; set; } = "";
        public string GetPauseReason() => PauseReason;
        public long LastAppliedApproximateVRAMBytes => 0;
    }
    public sealed class Server
    {
        public string ServerUri { get; set; } = "https://test.invalid";
        public bool FullPause { get; set; }
    }
    public sealed class ServerManager
    {
        public Server CurrentServer { get; } = new();
        public void Save() { }
    }
    public class NativeApi(List<NativePair> pairs)
    {
        public ServerManager _serverManager = new();
        public string UID => "local";
        public bool IsConnected { get; set; } = true;
        public bool UserRequestedFullPause { get; set; }
        public int Requests { get; private set; }
        public Action<PermissionRequest>? BeforeRequest { get; set; }
        public Func<PermissionRequest, Task>? Request { get; set; }
        public Task UserSetPairPermissions(PermissionRequest request)
        {
            BeforeRequest?.Invoke(request);
            Requests++;
            if (Request != null) return Request(request);
            pairs.Single(p => p.UserData.UID == request.User.UID).UserPair.OwnPermissions = request.Permissions;
            return Task.CompletedTask;
        }
        public Task CreateConnectionsAsync() { IsConnected = !_serverManager.CurrentServer.FullPause; return Task.CompletedTask; }
    }
    public sealed class Services(params object[] services) : IServiceProvider
    {
        public object? GetService(Type type) => services.FirstOrDefault(type.IsInstanceOfType);
    }
    public sealed class Host(IServiceProvider services) { public IServiceProvider Services { get; } = services; }
    public sealed class NativePlugin(IServiceProvider services) { public Host _host = new(services); }
    public sealed class ExposedPlugin(string name, object instance) : IExposedPlugin
    {
        private readonly LocalPlugin localPlugin = new(instance);
        public string InternalName { get; } = name;
        public string Name => InternalName;
        public bool IsLoaded => true;
    }
    internal sealed class Environment
    {
        internal NativePair Pair { get; }
        internal NativeApi Api { get; }
        internal ReflectionSyncAdapter Adapter { get; }
        internal Environment(SyncProvider provider)
        {
            var list = new List<NativePair>();
            object manager;
            string name;
            if (provider == SyncProvider.Snowcloak)
            {
                Pair = new Snowcloak.PlayerData.Pairs.Pair("remote");
                Api = new Snowcloak.WebAPI.ApiController(list);
                manager = new Snowcloak.PlayerData.Pairs.PairManager(list);
                name = "Snowcloak";
            }
            else
            {
                Pair = new PlayerSync.PlayerData.Pairs.Pair("remote");
                Api = new PlayerSync.WebAPI.ApiController(list);
                manager = new PlayerSync.PlayerData.Pairs.PairManager(list);
                name = "PlayerSync";
            }
            list.Add(Pair);
            Plugin.PluginInterface.InstalledPlugins.Add(new ExposedPlugin(name, new NativePlugin(new Services(manager, Api))));
            Adapter = new(provider);
        }
        internal PairSnapshot Snapshot()
        {
            var result = Adapter.Refresh(Plugin.PluginInterface.InstalledPlugins).Single();
            result.CharacterIdentity = "Example@1";
            return result;
        }
        internal static void Reset()
        {
            Plugin.PluginInterface.Write = null;
            Plugin.PluginInterface.InstalledPlugins.Clear();
            Plugin.ObjectTable.Clear();
        }
    }
}
namespace PlayerSync.PlayerData.Pairs
{
    public sealed class Pair(string uid) : CoordinatorTests.NativePair(uid);
    public sealed class PairManager(List<CoordinatorTests.NativePair> pairs)
    {
        public object[] GetPairsSnapshot() => pairs.Cast<object>().ToArray();
    }
}
namespace PlayerSync.WebAPI
{
    public sealed class ApiController(List<CoordinatorTests.NativePair> pairs) : CoordinatorTests.NativeApi(pairs);
}
namespace Snowcloak.PlayerData.Pairs
{
    public sealed class Pair(string uid) : CoordinatorTests.NativePair(uid)
    {
        public bool IsPaused => UserPair.OwnPermissions.IsPaused() || UserPair.OtherPermissions.IsPaused();
        public List<string> HoldApplicationReasons { get; } = [];
        public List<string> HoldDownloadReasons { get; } = [];
        public List<string> AutoPauseReasons { get; } = [];
        public void HoldDownloads(string source, int maximum) { if (!HoldDownloadReasons.Contains(source)) HoldDownloadReasons.Add(source); }
        public void HoldApplication(string source, int maximum) { if (!HoldApplicationReasons.Contains(source)) HoldApplicationReasons.Add(source); }
        public void UnholdDownloads(string source, bool skipApplication) => HoldDownloadReasons.Remove(source);
        public void UnholdApplication(string source, bool skipApplication) => HoldApplicationReasons.Remove(source);
    }
    public sealed class PairManager(List<CoordinatorTests.NativePair> pairs)
    {
        public object[] GetPairsSnapshot() => pairs.Cast<object>().ToArray();
    }
}
namespace Snowcloak.WebAPI
{
    public sealed class ApiController(List<CoordinatorTests.NativePair> pairs) : CoordinatorTests.NativeApi(pairs);
}
