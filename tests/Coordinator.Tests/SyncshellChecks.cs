using CoordinatorTests;
using XivSyncManager;

namespace CoordinatorTests
{
    public sealed record ShellGroup(string GID, string AliasOrGID = "Same name");
    public sealed record ShellUser(string UID, string? Alias = null, bool IsAdmin = false);
    public sealed record ShellRequest(ShellGroup Group, ShellUser User, Permissions GroupPairPermissions);
    public sealed record ProfileScope(IServiceProvider ServiceProvider);
    public sealed class ProfileWindows { public List<object> Windows { get; } = []; }
    public sealed record ShellGroupInfo(ShellGroup Group)
    {
        public string GID => Group.GID;
        public string GroupAliasOrGID => Group.AliasOrGID;
        public Permissions GroupUserPermissions { get; set; }
    }

    internal static class SyncshellChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            Environment.Reset();
            var installed = Plugin.PluginInterface.InstalledPlugins;
            var lightless = new LightlessSync.WebAPI.ApiController();
            var snow = new Snowcloak.WebAPI.ApiController([]);
            var player = new PlayerSync.WebAPI.ApiController([]);
            var lights = new LightlessSync.PlayerData.Pairs.PairManager();
            var snows = new Snowcloak.PlayerData.Pairs.PairManager([]);
            var players = new PlayerSync.PlayerData.Pairs.PairManager([]);
            var l = new ShellGroupInfo(new("gid"));
            var s = new ShellGroupInfo(new("gid")) { GroupUserPermissions = Permissions.Sounds | Permissions.Sticky };
            var p = new ShellGroupInfo(new("gid")) { GroupUserPermissions = Permissions.Paused };
            lights.GroupsByGid.Add(l.Group.GID, l);
            snows.Groups.Add(s.Group.GID, s);
            snows.Groups.Add("second", new(new("second")));
            players.Groups.Add(p.Group.GID, p);
            player.OnGroupsGetAll = () => Task.FromResult(players.Groups.Values.ToList());
            var unrelated = new NativePair("remote");
            unrelated.UserPair.OwnPermissions = Permissions.Paused;
            var config = new Configuration { AutomaticManagement = false };
            config.OwnedPauses.Add("owned", new() { OriginalPauseReason = "original" });
            Plugin.PluginInterface.Write = _ => throw new InvalidOperationException("Shell actions must not save manager config");
            var lightUi = new LightlessSync.Services.UiService();
            var lightMediator = new LightlessSync.Services.Mediator.LightlessMediator();
            var lightRuntime = new LightlessSync.LightlessPlugin(new(new Services(lightUi)));
            var playerUi = new PlayerSync.Services.UiService();
            var playerMediator = new PlayerSync.Services.Mediator.MareMediator();
            var playerRuntime = new PlayerSync.MarePlugin(new(new Services(playerUi)));
            installed.Add(new ExposedPlugin("LightlessSync", new NativePlugin(new Services(lights, lightless, lightMediator, lightRuntime))));
            installed.Add(new ExposedPlugin("Snowcloak", new NativePlugin(new Services(snows, snow))));
            installed.Add(new ExposedPlugin("MareSempiterne", new NativePlugin(new Services(players, player, playerMediator, playerRuntime))));
            var tab = new Syncshells(() => installed);
            var now = TimeSpan.Zero;
            void Refresh() => tab.Refresh(now);
            SyncshellRow Row(SyncProvider provider, string id = "gid") => tab.VisibleRows.Single(row => row.Key == new SyncshellKey(provider, id));
            bool ErrorFor(SyncProvider provider, string text, string id = "gid") =>
                tab.Errors.TryGetValue(new(provider, id), out var error) && error.Contains(text, StringComparison.Ordinal);
            Refresh();
            check(tab.VisibleRows.Count() == 4 && tab.VisibleRows.Select(r => r.Key).Distinct().Count() == 4,
                "Same-named empty shells are listed separately even when IDs overlap across providers");
            var offline = new NativePair("offline") { IsOnline = false, IsVisible = false, PlayerName = "" };
            lights.GroupPairs.Add(new(l.Group), [offline]);
            snows.GroupPairs.Add(new(s.Group), [new("snow-only")]);
            players.GroupPairs.Add(new(p.Group), [new("player-only")]);
            snows.GroupPairs.Add(new(new("second")), []);
            void RefreshRosters(Func<SyncshellKey, bool> expanded) => tab.Refresh(now, expanded);
            string[]? Roster(SyncProvider provider, string id = "gid") => Row(provider, id).Members;
            RefreshRosters(_ => true);
            check(Roster(SyncProvider.Lightless) is ["offline"], "The native roster includes offline members and falls back to the UID");
            check(Roster(SyncProvider.Snowcloak) is ["snow-only"] && Roster(SyncProvider.PlayerSync) is ["player-only"],
                "Same-named shells with the same GID across providers have separate rosters");
            check(Roster(SyncProvider.Snowcloak, "second") is [], "An empty roster is distinct from an unavailable roster");
            snows.GroupPairs.Clear();
            RefreshRosters(_ => true);
            check(Roster(SyncProvider.Snowcloak) == null && Row(SyncProvider.Snowcloak)[SyncshellControl.Pause].Availability == SyncshellAvailability.Available,
                "Missing roster data does not hide shells or disable native shell actions");
            lights.GroupPairs.Values.Single().Clear();
            RefreshRosters(key => key.Provider == SyncProvider.Lightless);
            check(Roster(SyncProvider.Lightless) is [] && Roster(SyncProvider.PlayerSync) == null,
                "Expanded rows reflect membership removals and collapsed rows do not project members");
            var nativeReads = lights.Reads;
            tab.Toggle(SyncProvider.Snowcloak);
            check(tab.VisibleRows.Count() == 2 && lights.Reads == nativeReads && snow.ShellRequests.Count == 0, "Filters only affect presentation");
            tab.Toggle(SyncProvider.Lightless);
            tab.Toggle(SyncProvider.PlayerSync);
            check(tab.VisibleRows.Count() == 0 && !tab.AnyVisibleProvider, "All providers can be hidden independently");
            foreach (var provider in Enum.GetValues<SyncProvider>()) tab.Toggle(provider);

            var readySnow = Row(SyncProvider.Snowcloak);
            snow.ChatPairStateReady = false;
            tab.Act(readySnow, SyncshellControl.Vfx, now);
            Refresh();
            check(snow.ShellRequests.Count == 0 && Row(SyncProvider.Snowcloak)[SyncshellControl.Vfx].Availability == SyncshellAvailability.Unavailable,
                "Snowcloak cannot write a retained permission mask while native reconciliation is pending");
            snow.ChatPairStateReady = true;
            Refresh();

            foreach (var (provider, dto, api) in new (SyncProvider, ShellGroupInfo, NativeApi)[]
                { (SyncProvider.Lightless, l, lightless), (SyncProvider.Snowcloak, s, snow) })
            {
                var before = dto.GroupUserPermissions;
                var row = Row(provider);
                var nativeTask = new TaskCompletionSource();
                api.OnShellRequest = _ => nativeTask.Task;
                var viewReads = lights.Reads;
                tab.Act(row, SyncshellControl.Pause, now);
                if (provider == SyncProvider.Lightless)
                    check(lights.Reads == viewReads, "Lightless dispatch resolves the GID directly, without building member projections");
                var request = api.ShellRequests.Single();
                check(request.Group.GID == dto.Group.GID && request.User.UID == "local" && request.GroupPairPermissions == (before | Permissions.Paused),
                    "Pause sends only the local joined shell permission for the exact GID");
                Refresh();
                check(tab.Pending(row.Key) == SyncshellControl.Pause && Row(provider)[SyncshellControl.Pause].Value == false,
                    "Pending action does not optimistically set provider state");
                tab.Act(Row(provider), SyncshellControl.Vfx, now);
                check(api.ShellRequests.Count == 1, "Block all writes to a shell while its whole permission mask is in flight");
                nativeTask.SetResult();
                Refresh();
                check(tab.Pending(row.Key) == SyncshellControl.Pause, "Transport completion alone cannot confirm a pause");
                dto.GroupUserPermissions = request.GroupPairPermissions;
                if (provider == SyncProvider.Snowcloak) snow._connectionContext = new();
                Refresh();
                check(tab.Pending(row.Key) == null && Row(provider)[SyncshellControl.Pause].Value == true, "Native state confirms pause");
                if (provider == SyncProvider.Snowcloak)
                    check(!tab.Errors.ContainsKey(row.Key), "Replacing the Snowcloak profile context does not invalidate the transport");
                api.OnShellRequest = null;
                tab.Act(Row(provider), SyncshellControl.Pause, now);
                check(api.ShellRequests.Last().GroupPairPermissions == before, "Native resume preserves all other flags without a member list");
                dto.GroupUserPermissions = before;
                Refresh();
            }
            check(Row(SyncProvider.PlayerSync)[SyncshellControl.Pause] is { Value: true, Availability: SyncshellAvailability.Unsupported },
                "PlayerSync pause is read-only because its native action mutates member pause reasons");
            tab.Act(Row(SyncProvider.PlayerSync), SyncshellControl.Pause, now);
            check(player.ShellRequests.Count == 0, "Unsupported PlayerSync pause never calls the shell or pair endpoint");
            check(Row(SyncProvider.Snowcloak)[SyncshellControl.Profile].Availability == SyncshellAvailability.Unsupported, "Snowcloak has no shell profile");

            tab.Act(Row(SyncProvider.Lightless), SyncshellControl.Profile, now);
            lights.GroupsByGid.Add("alternate", new(new("alternate")));
            Refresh();
            check(lightMediator.Last is LightlessSync.Services.Mediator.GroupProfileOpenStandaloneMessage { Group.GID: "gid" }
                && tab.Pending(Row(SyncProvider.Lightless).Key) == SyncshellControl.Profile, "Lightless publishes its exact shell profile message and waits for the viewer");
            tab.Act(Row(SyncProvider.Lightless, "alternate"), SyncshellControl.Profile, now);
            check(lightMediator.Last is LightlessSync.Services.Mediator.GroupProfileOpenStandaloneMessage { Group.GID: "gid" },
                "Another shell profile from the same provider cannot replace a pending open request");
            lightUi._windowSystem.Windows.Add(new LightlessSync.Services.StandaloneProfileUi(new("not-the-target")));
            Refresh();
            check(tab.Pending(Row(SyncProvider.Lightless).Key) == SyncshellControl.Profile, "A different native profile window cannot confirm this request");
            lightUi._windowSystem.Windows.Add(new LightlessSync.Services.StandaloneProfileUi(l.Group));
            Refresh();
            check(tab.Pending(Row(SyncProvider.Lightless).Key) == null, "Lightless profile confirms on matching native GID");

            var otherProfile = new PlayerSync.Services.SyncshellProfileUi(new(new("different")));
            playerUi._windowSystem.Windows.Add(otherProfile);
            tab.Act(Row(SyncProvider.PlayerSync), SyncshellControl.Profile, now);
            check(playerMediator.Last == null && ErrorFor(SyncProvider.PlayerSync, "same-named"),
                "Do not send a colliding native PlayerSync window or open the wrong shell");
            playerUi._windowSystem.Windows.Clear();
            tab.Act(Row(SyncProvider.PlayerSync), SyncshellControl.Profile, now);
            check(playerMediator.Last is PlayerSync.Services.Mediator.OpenSyncshellProfilePanel { GroupInfo.GID: "gid" },
                "PlayerSync opens the native shell profile for the selected group DTO");
            playerUi._windowSystem.Windows.Add(new PlayerSync.Services.SyncshellProfileUi(p));
            Refresh();
            check(tab.Pending(Row(SyncProvider.PlayerSync).Key) == null, "PlayerSync profile confirms from the native window system");

            foreach (var (provider, dto, api) in new (SyncProvider, ShellGroupInfo, NativeApi)[]
                { (SyncProvider.Lightless, l, lightless), (SyncProvider.Snowcloak, s, snow), (SyncProvider.PlayerSync, p, player) })
            {
                foreach (var (control, flag) in new[] { (SyncshellControl.Sound, Permissions.Sounds), (SyncshellControl.Vfx, Permissions.Vfx), (SyncshellControl.Animations, Permissions.Animations) })
                {
                    Refresh();
                    // Simulate a native-menu change to a different flag between display and click.
                    dto.GroupUserPermissions ^= Permissions.Sticky;
                    var before = dto.GroupUserPermissions;
                    tab.Act(Row(provider), control, now);
                    check(api.ShellRequests.Last().GroupPairPermissions == (before ^ flag), "Each media control preserves the latest other native flags");
                    dto.GroupUserPermissions ^= flag;
                    Refresh();
                    check(Row(provider)[control].Value == !dto.GroupUserPermissions.HasFlag(flag), "Readback is the native media allowance, not the request");
                }
            }
            var playerRow = Row(SyncProvider.PlayerSync);
            var freshGroups = new TaskCompletionSource<List<ShellGroupInfo>>();
            player.OnGroupsGetAll = () => freshGroups.Task;
            var playerSent = player.ShellRequests.Count;
            tab.Act(playerRow, SyncshellControl.Sound, now);
            check(player.ShellRequests.Count == playerSent, "PlayerSync waits for fresh native membership before writing a permission mask");
            var freshPlayerFlags = p.GroupUserPermissions ^ Permissions.Sticky;
            freshGroups.SetResult([new(p.Group) { GroupUserPermissions = freshPlayerFlags }]);
            check(player.ShellRequests.Count == playerSent + 1 && player.ShellRequests.Last().GroupPairPermissions == (freshPlayerFlags ^ Permissions.Sounds),
                "PlayerSync uses the fresh native mask rather than retained permissions from before reconnect");
            p.GroupUserPermissions = freshPlayerFlags ^ Permissions.Sounds;
            Refresh();
            player.OnGroupsGetAll = () => Task.FromResult(players.Groups.Values.ToList());
            var slow = new TaskCompletionSource();
            snow.OnShellRequest = _ => slow.Task;
            var second = Row(SyncProvider.Snowcloak, "second");
            tab.Act(second, SyncshellControl.Vfx, now);
            check(snow.ShellRequests.Last().Group.GID == "second", "Actions target GID rather than same-provider display alias");
            now += TimeSpan.FromSeconds(16);
            Refresh();
            check(ErrorFor(SyncProvider.Snowcloak, "did not confirm", "second") && tab.Pending(second.Key) != null,
                "Running, unconfirmed requests time out visibly but remain blocked until their task finishes");
            var sent = snow.ShellRequests.Count;
            tab.Act(second, SyncshellControl.Sound, now);
            check(snow.ShellRequests.Count == sent, "A timed-out running write cannot be overwritten by a second request");
            slow.SetResult();
            Refresh();
            check(tab.Pending(second.Key) == null, "Timed-out controls unlock when the request actually stops running");

            snow.OnShellRequest = _ => Task.FromException(new InvalidOperationException("refused"));
            tab.Act(Row(SyncProvider.Snowcloak), SyncshellControl.Vfx, now);
            Refresh();
            check(ErrorFor(SyncProvider.Snowcloak, "refused") && ErrorFor(SyncProvider.Snowcloak, "did not confirm", "second"),
                "Failures in separate shells are reported independently");
            snow.OnShellRequest = null;
            tab.Act(Row(SyncProvider.Snowcloak), SyncshellControl.Vfx, now);
            var stale = Row(SyncProvider.Snowcloak);
            snow.IsConnected = false;
            Refresh();
            check(tab.VisibleRows.Contains(Row(SyncProvider.Snowcloak)) && Row(SyncProvider.Snowcloak)[SyncshellControl.Vfx].Availability == SyncshellAvailability.Unavailable,
                "Disconnected native listings can still display but are read-only");
            check(ErrorFor(SyncProvider.Snowcloak, "connection changed"), "Connection loss prevents false confirmation");
            var requests = snow.ShellRequests.Count;
            snow._connectionContext = new();
            snow._snowHub.ConnectionId = "reconnected";
            snow.IsConnected = true;
            tab.Act(stale, SyncshellControl.Sound, now);
            Refresh();
            check(snow.ShellRequests.Count == requests, "Stale rows cannot dispatch after reconnect and nothing replays");
            snows.Groups.Clear();
            installed.RemoveAt(0);
            Refresh();
            check(!tab.Providers[SyncProvider.Lightless].CanList && tab.Providers[SyncProvider.Snowcloak].CanList && tab.VisibleRows.Count() == 1,
                "Missing provider is distinct from a connected empty registry; the other provider still works");
            var reloaded = new Syncshells(() => installed);
            check(!reloaded.VisibleRows.Any(), "New tab instances do not load shell state from a cache");
            p.GroupUserPermissions ^= Permissions.Paused;
            reloaded.Refresh(now);
            check(reloaded.VisibleRows.Single()[SyncshellControl.Pause].Value == p.GroupUserPermissions.IsPaused(),
                "Reload reads the current reported shell state directly from the provider");
            check(unrelated.UserPair.OwnPermissions.IsPaused() && config.OwnedPauses["owned"].OriginalPauseReason == "original"
                && lightless.Requests + snow.Requests + player.Requests == 0, "No group action touches individual pair permissions or manager ownership");
            Environment.Reset();
        }
    }
}

namespace LightlessSync.PlayerData.Pairs
{
    public sealed class PairManager
    {
        public Dictionary<string, ShellGroupInfo> GroupsByGid { get; } = new(StringComparer.Ordinal);
        public Dictionary<ShellGroupInfo, List<NativePair>> GroupPairs { get; } = [];
        public int Reads { get; private set; }
        public PairManager GetCollectionView() { Reads++; return this; }
        public bool TryGetGroupByGid(string gid, out ShellGroupInfo? group) => GroupsByGid.TryGetValue(gid, out group);
    }
}
namespace LightlessSync.WebAPI
{
    public sealed class ApiController() : NativeApi([]);
}
namespace LightlessSync
{
    public sealed class LightlessPlugin(ProfileScope scope)
    {
        private readonly RuntimeLifecycle _runtimeLifecycle = new(scope);
    }
    internal sealed class RuntimeLifecycle(ProfileScope scope)
    {
        public bool TryUseCurrent(Action<ProfileScope> use) { use(scope); return true; }
    }
}
namespace LightlessSync.Services
{
    public sealed class UiService { public readonly ProfileWindows _windowSystem = new(); }
    public sealed record StandaloneProfileUi(ShellGroup ProfileGroupData) { public bool IsOpen => true; }
}
namespace LightlessSync.Services.Mediator
{
    public sealed record GroupProfileOpenStandaloneMessage(ShellGroup Group);
    public sealed class LightlessMediator
    {
        public object? Last { get; private set; }
        public void Publish<T>(T message) => Last = message;
    }
}
namespace PlayerSync
{
    public sealed class MarePlugin(ProfileScope scope) { private readonly ProfileScope _runtimeServiceScope = scope; }
}
namespace PlayerSync.Services
{
    public sealed class UiService { public readonly ProfileWindows _windowSystem = new(); }
    public sealed record SyncshellProfileUi(ShellGroupInfo GroupFullInfo)
    {
        public bool IsOpen => true;
        public string WindowName => "Syncshell Profile for " + GroupFullInfo.GroupAliasOrGID;
    }
}
namespace PlayerSync.Services.Mediator
{
    public sealed record OpenSyncshellProfilePanel(ShellGroupInfo GroupInfo);
    public sealed class MareMediator
    {
        public object? Last { get; private set; }
        public void Publish<T>(T message) => Last = message;
    }
}
