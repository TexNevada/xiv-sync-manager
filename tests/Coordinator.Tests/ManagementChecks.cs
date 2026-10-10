using System.Reflection;
using XivSyncManager;

namespace CoordinatorTests;

internal static class ManagementChecks
{
    private static object? Invoke(SyncCoordinator manager, string name, params object[] args)
    {
        try { return typeof(SyncCoordinator).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(manager, args); }
        catch (TargetInvocationException exception) { throw exception.InnerException!; }
    }

    private static void Refresh(SyncCoordinator manager)
    {
        foreach (var step in (IEnumerable<string>)Invoke(manager, "Refresh")!) { }
    }

    internal static async Task Run(Action<bool, string> assert)
    {
        // Run the actual coordinator refresh against server pauses and Snowcloak source holds.
        foreach (var backupProvider in new[] { SyncProvider.PlayerSync, SyncProvider.Snowcloak })
        {
            Environment.Reset();
            var primaryProvider = backupProvider == SyncProvider.PlayerSync ? SyncProvider.Snowcloak : SyncProvider.PlayerSync;
            var primary = new Environment(primaryProvider);
            var backup = new Environment(backupProvider);
            Plugin.ObjectTable.Add(new Dalamud.Game.ClientState.Objects.SubKinds.TestPlayer(1, "Example", 1));
            var state = new Configuration { AutomaticManagement = true, Priority = [primaryProvider, backupProvider] };
            var manager = new SyncCoordinator(state);
            Refresh(manager);
            Refresh(manager);
            var key = backup.Snapshot().Key;
            assert(state.OwnedPauses[key].Confirmed, "Automatic management confirms the backup pause");
            var requests = backup.Api.Requests;
            manager.SetAutomaticManagement(false);
            assert(!state.OwnedPauses[key].RestoreRequested, "Turning management off does not request restoration");
            for (var pass = 0; pass < 3; pass++) Refresh(manager);
            var pair = backup.Snapshot();
            assert((pair.OwnPaused || pair.ManagerFullyHeld) && backup.Api.Requests == requests,
                "Repeated off refreshes retain the pause without native resume requests");
            assert(!primary.Snapshot().OwnPaused && !primary.Snapshot().ManagerHeld,
                "Turning management off does not pause the active primary");
            var character = manager.Characters.Single();
            assert(manager.CanResumeCharacter(character) && !manager.IsCharacterPaused(character),
                "Resume is available for retained automatic pauses without creating a manual choice");

            backup.Api.IsConnected = false;
            Refresh(manager);
            backup.Api.IsConnected = true;
            Refresh(manager);
            assert(!state.OwnedPauses[key].RestoreRequested && (backup.Snapshot().OwnPaused || backup.Snapshot().ManagerFullyHeld),
                "Reconnection while management is off retains the manager pause");

            manager.SetCharacterPaused(manager.Characters.Single(), false);
            Refresh(manager);
            Refresh(manager);
            assert(state.OwnedPauses.Count == 0 && !backup.Snapshot().OwnPaused && !backup.Snapshot().ManagerHeld,
                "Explicit character Resume still releases retained pauses while management is off");

            // Restore automatic rules, then change the preferred sync while they are suspended.
            manager.SetPreferred("Example@1", null);
            manager.SetAutomaticManagement(true);
            Refresh(manager);
            Refresh(manager);
            manager.SetAutomaticManagement(false);
            manager.SetPreferred("Example@1", backupProvider);
            Refresh(manager);
            assert(backup.Snapshot().OwnPaused || backup.Snapshot().ManagerFullyHeld,
                "Preference changes do not resume held syncs while management is off");
            manager.SetAutomaticManagement(true);
            for (var pass = 0; pass < 4; pass++) Refresh(manager);
            assert(!backup.Snapshot().OwnPaused && !backup.Snapshot().ManagerHeld
                && (primary.Snapshot().OwnPaused || primary.Snapshot().ManagerFullyHeld),
                "Re-enabling management restores the new winner before pausing its backup");

            manager.SetAutomaticManagement(false);
            state.CharacterIndexActivity["Example@1"].LastSeenOnlineUtc = DateTime.UtcNow.AddDays(-31);
            Invoke(manager, "ExpireStaleIndex", DateTime.UtcNow);
            assert(state.DuplicateCharacters.Count == 0 && state.OwnedPauses.Values.All(o => !o.RestoreRequested),
                "Stale-index expiry while off preserves pause ownership without requesting resume");
            manager.ClearCache();
            Refresh(manager);
            Refresh(manager);
            assert(state.OwnedPauses.Count == 0, "Explicit cache clearing still restores retained pauses");
            await state.FlushSavesAsync();
        }

        // An already dispatched automatic pause may finish after Off was clicked.
        Environment.Reset();
        var active = new Environment(SyncProvider.Snowcloak);
        var delayed = new Environment(SyncProvider.PlayerSync);
        Plugin.ObjectTable.Add(new Dalamud.Game.ClientState.Objects.SubKinds.TestPlayer(1, "Example", 1));
        var delayedState = new Configuration { AutomaticManagement = true, Priority = [SyncProvider.Snowcloak, SyncProvider.PlayerSync] };
        var delayedManager = new SyncCoordinator(delayedState);
        var completion = new TaskCompletionSource();
        Permissions expected = Permissions.None;
        delayed.Api.Request = request => { expected = request.Permissions; return completion.Task; };
        Refresh(delayedManager);
        delayedManager.SetAutomaticManagement(false);
        delayed.Pair.UserPair.OwnPermissions = expected;
        completion.SetResult();
        Refresh(delayedManager);
        Refresh(delayedManager);
        assert(delayed.Pair.UserPair.OwnPermissions.IsPaused() && delayed.Api.Requests == 1
            && delayedState.OwnedPauses[delayed.Snapshot().Key].Confirmed,
            "A pause completing after Off stays paused rather than immediately resuming");
        await delayedState.FlushSavesAsync();
    }
}
