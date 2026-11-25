using System.Reflection;
using XivSyncManager;

namespace CoordinatorTests;

internal static class UnifiedListChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Environment.Reset();
        var player = new Environment(SyncProvider.PlayerSync);
        var snow = new Environment(SyncProvider.Snowcloak);
        snow.Pair.IsOnline = false;
        snow.Pair.IsVisible = false;
        var state = new Configuration();
        var manager = new SyncCoordinator(state);
        var p = player.Snapshot();
        var s = snow.Snapshot();
        p.CharacterIdentity = null;
        s.CharacterIdentity = null;
        typeof(SyncCoordinator).GetProperty(nameof(SyncCoordinator.Pairs))!.SetValue(manager, new[] { p, s });
        check(manager.Characters.Count == 2, "Unidentified accounts from every provider appear without needing a duplicate observation");
        check(manager.Characters.Count(c => c.Nearby) == 1 && manager.Characters.Count(c => !c.Online) == 1,
            "Native visibility and online state classify unidentified accounts");
        check(manager.Characters.Select(c => c.Identity).Distinct().Count() == 2,
            "Same UID on different providers never merges unidentified accounts");
        var row = manager.Characters.Single(c => c.Pairs.Contains(p));
        manager.SetCharacterPaused(row, true);
        check(state.ManualPauses.Contains(p.Key) && !state.CharacterPauses.Contains(row.Identity),
            "Unidentified account pause targets its account-scoped route, not a guessed character");
        manager.SetCharacterPaused(row, false);
        check(!state.ManualPauses.Contains(p.Key), "Unidentified account resume is safe without a duplicate cache entry");
        p.CharacterIdentity = s.CharacterIdentity = "Example@1";
        p.CharacterName = s.CharacterName = "Example@World";
        typeof(SyncCoordinator).GetProperty(nameof(SyncCoordinator.Pairs))!.SetValue(manager, new[] { p, s });
        check(manager.Characters.Count == 1 && manager.Characters.Single().Pairs.Count == 2,
            "Verified character identities combine routes without requiring duplicate history");

        var reload = typeof(SyncCoordinator).GetMethod("ReloadLastData");
        check(reload != null, "The sync list exposes native reload last data");
        reload!.Invoke(manager, [p]);
        check(player.Pair.ReapplyRequests == 1 && player.Pair.ReapplyForced == true,
            "Manual reload uses the provider's forced cached-data action");
        reload.Invoke(manager, [s]);
        check(snow.Pair.ReapplyRequests == 0, "Offline users cannot reload appearance data");
        var cycle = typeof(SyncCoordinator).GetMethod("CyclePauseState");
        check(cycle != null, "The sync list exposes native cycle pause state");
        cycle!.Invoke(manager, [p]);
        cycle.Invoke(manager, [p]);
        check(player.Api.Requests == 1 && state.OwnedPauses.ContainsKey(p.Key) && manager.IsPending(p),
            "A coordinated native cycle persists ownership and cannot duplicate its first request");
        check(!manager.CanChangeMedia(p), "Media requests cannot race with native cycle writes");
        cycle.Invoke(manager, [s]);
        check(snow.Api.CycleRequests == 0, "Offline accounts never dispatch cycle requests");

        Environment.Reset();
        var shellOnly = new Snowcloak.PlayerData.Pairs.Pair("shell-only") { UserPair = null! };
        var direct = new Snowcloak.PlayerData.Pairs.Pair("direct");
        var nativePairs = new List<NativePair> { direct, shellOnly };
        var nativeApi = new Snowcloak.WebAPI.ApiController(nativePairs);
        var nativeManager = new Snowcloak.PlayerData.Pairs.PairManager(nativePairs);
        Plugin.PluginInterface.InstalledPlugins.Add(new ExposedPlugin("Snowcloak",
            new NativePlugin(new Services(nativeManager, nativeApi))));
        var adapter = new ReflectionSyncAdapter(SyncProvider.Snowcloak);
        var snapshots = adapter.Refresh(Plugin.PluginInterface.InstalledPlugins);
        check(adapter.Status.Connected && snapshots.Count == 2,
            "A Snowcloak syncshell-only member without direct permissions must not disable the provider or erase its users");
        var shellSnapshot = snapshots.Single(pair => pair.Uid == "shell-only");
        check(!shellSnapshot.Media.CanEdit && shellSnapshot.Permissions == string.Empty,
            "A syncshell-only member has no invented direct media permissions");
        var ownership = adapter.PreparePause(shellSnapshot);
        adapter.SetPaused(shellSnapshot, true).GetAwaiter().GetResult();
        var held = adapter.Refresh(Plugin.PluginInterface.InstalledPlugins).Single(pair => pair.Uid == "shell-only");
        check(ownership.LocalHold && held.ManagerFullyHeld && nativeApi.Requests == 0,
            "Snowcloak syncshell-only members can still be paused through owned local holds");
        adapter.SetPaused(held, false).GetAwaiter().GetResult();
        check(shellOnly.HoldApplicationReasons.Count == 0 && shellOnly.HoldDownloadReasons.Count == 0,
            "Snowcloak syncshell-only local holds can be safely released");
    }
}
