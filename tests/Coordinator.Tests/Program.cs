using System.Globalization;
using System.Reflection;
using CoordinatorTests;
using XivSyncManager;
using TestEnvironment = CoordinatorTests.Environment;

var checks = 0;
void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}
object? Invoke(object target, string name, params object[] args)
{
    try { return target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args); }
    catch (TargetInvocationException exception) { throw exception.InnerException!; }
}
void SetPairs(SyncCoordinator coordinator, params PairSnapshot[] pairs) =>
    typeof(SyncCoordinator).GetProperty(nameof(SyncCoordinator.Pairs))!.SetValue(coordinator, pairs);

// Exercise real reflection discovery and native API dispatch, including repeated storage errors.
TestEnvironment.Reset();
var native = new TestEnvironment(SyncProvider.PlayerSync);
var config = new Configuration();
var manager = new SyncCoordinator(config);
var pair = native.Snapshot();
var writes = 0;
Plugin.PluginInterface.Write = _ => { writes++; throw new IOException("Simulated storage failure"); };
for (var attempt = 0; attempt < 2; attempt++)
{
    try { Invoke(manager, "StartPause", pair); throw new Exception("Failed storage write was ignored"); }
    catch (IOException) { }
    Assert(native.Api.Requests == 0, "Every failed initial or retried save prevents a native pause");
    Assert(config.OwnedPauses.Count == 0, "A failed initial save cannot leave an unsent pause claimed in memory");
}
Assert(writes == 2, "Retry attempts persistence again");
config.OwnedPauses[pair.Key] = native.Adapter.PreparePause(pair);
try { Invoke(manager, "StartPause", pair); throw new Exception("Existing ownership bypassed persistence"); }
catch (IOException) { }
Assert(writes == 3 && native.Api.Requests == 0, "Existing unconfirmed records also require a successful save before dispatch");
Configuration? persisted = null;
Plugin.PluginInterface.Write = value => persisted = value;
native.Api.BeforeRequest = _ => Assert(persisted?.OwnedPauses.ContainsKey(pair.Key) == true,
    "Restoration record is durable before native permission dispatch");
Invoke(manager, "StartPause", pair);
Assert(native.Api.Requests == 1 && native.Pair.UserPair.OwnPermissions.IsPaused(), "Recovered storage allows the pause");
native.Api.BeforeRequest = null;

// A successful request must remain recoverable after reload with automatic management off.
var loaded = persisted!.CreateSnapshot();
var reloaded = new SyncCoordinator(loaded);
var pausedPair = native.Snapshot();
SetPairs(reloaded, pausedPair);
Invoke(reloaded, "StartRestore", pausedPair, loaded.OwnedPauses[pausedPair.Key]);
SetPairs(reloaded, native.Snapshot());
Invoke(reloaded, "FinishOperations");
Assert(!native.Pair.UserPair.OwnPermissions.IsPaused() && loaded.OwnedPauses.Count == 0,
    "Reload restores and confirms the durable native pause");

// Native Snowcloak permission pauses are independent of manager source holds.
TestEnvironment.Reset();
var snow = new TestEnvironment(SyncProvider.Snowcloak);
var backup = new TestEnvironment(SyncProvider.PlayerSync);
var snowConfig = new Configuration { AutomaticManagement = true, Priority = [SyncProvider.Snowcloak, SyncProvider.PlayerSync, SyncProvider.Lightless] };
var snowPolicy = new DuplicatePolicy();
var snowPair = snow.Snapshot();
backup.Pair.UserPair.OwnPermissions = Permissions.Paused;
var backupPair = backup.Snapshot();
snowConfig.OwnedPauses[backupPair.Key] = backup.Adapter.PreparePause(backupPair);
snowConfig.OwnedPauses[backupPair.Key].OriginalPermissions = "0";
var visible = new HashSet<string> { "Example@1" };
var first = snowPolicy.GetAutomaticPauses([snowPair, backupPair], snowConfig, [snow.Adapter.Status, backup.Adapter.Status], visible);
Assert(first.Contains(backupPair.Key), "Initially the active higher-priority Snowcloak keeps its backup paused");
var nativeSnow = (Snowcloak.PlayerData.Pairs.Pair)snow.Pair;
nativeSnow.HoldDownloads(ReflectionSyncAdapter.HoldSource, 1);
nativeSnow.HoldApplication(ReflectionSyncAdapter.HoldSource, 1);
snowConfig.OwnedPauses[snowPair.Key] = new() { LocalHold = true, CharacterIdentity = "Example@1", Confirmed = true };
foreach (var otherSide in new[] { false, true })
{
    nativeSnow.UserPair.OwnPermissions = otherSide ? Permissions.None : Permissions.Paused;
    nativeSnow.UserPair.OtherPermissions = otherSide ? Permissions.Paused : Permissions.None;
    nativeSnow.IsOnline = false; // Nearby presence alone may still make a managed hold restorable.
    var blocked = snow.Snapshot();
    Assert(blocked.OwnPaused && blocked.ExternalHold && blocked.ManagerHeld,
        "Adapter separates a native permission restriction from its own local source hold");
    var decision = snowPolicy.GetAutomaticPauses([blocked, backupPair], snowConfig, [snow.Adapter.Status, backup.Adapter.Status], visible);
    Assert(!decision.Contains(backupPair.Key), "Usable backup is selected for restoration instead of the natively blocked Snowcloak");
}
nativeSnow.UserPair.OwnPermissions = Permissions.None;
nativeSnow.UserPair.OtherPermissions = Permissions.None;
Assert(!snow.Snapshot().ExternalHold, "Manager-only local holds do not look like external native restrictions");

// Track real media requests that fail locally but may still receive a server callback later.
void ExerciseLateMedia(bool finishRestoreFirst, bool reload, bool changeReason,
    bool runningRestore = false, bool completedRestoreAwaitingState = false)
{
    TestEnvironment.Reset();
    var environment = new TestEnvironment(SyncProvider.PlayerSync);
    var state = new Configuration();
    var coordinator = new SyncCoordinator(state);
    var initial = environment.Snapshot();
    Invoke(coordinator, "StartPause", initial);
    var paused = environment.Snapshot();
    SetPairs(coordinator, paused);
    Invoke(coordinator, "FinishOperations");
    environment.Api.Request = _ => Task.FromException(new TimeoutException("Transport failed after possible dispatch"));
    coordinator.SetMedia([paused], MediaKind.Sounds, true);
    Invoke(coordinator, "StartQueuedMedia");
    var change = state.OwnedPauses[paused.Key].MediaChange!;
    var expectedAfter = change.After;
    var expectedOriginal = change.OriginalAfter;
    Invoke(coordinator, "FinishMediaOperations");
    Assert(state.OwnedPauses[paused.Key].MediaChange != null, "Early failure retains its pending alternatives");
    change.Deadline = DateTime.UtcNow.AddSeconds(-1);
    Invoke(coordinator, "ObserveExternalChanges");
    Assert(state.OwnedPauses[paused.Key].MediaChange == null && state.LateMediaChanges[paused.Key].Count == 1,
        "Expired callback expectation is saved separately and no longer blocks Resume");
    environment.Api.Request = null;
    if (finishRestoreFirst)
    {
        Invoke(coordinator, "StartRestore", paused, state.OwnedPauses[paused.Key]);
        SetPairs(coordinator, environment.Snapshot());
        Invoke(coordinator, "FinishOperations");
        Assert(state.OwnedPauses.Count == 0 && state.LateMediaChanges.Count == 1,
            "Normal restoration completes while late-response history survives");
    }
    TaskCompletionSource? restoreCompletion = null;
    if (runningRestore || completedRestoreAwaitingState)
    {
        restoreCompletion = new();
        environment.Api.Request = _ => runningRestore ? restoreCompletion.Task : Task.CompletedTask;
        Invoke(coordinator, "StartRestore", paused, state.OwnedPauses[paused.Key]);
        Invoke(coordinator, "FinishOperations");
    }
    if (reload)
    {
        state = state.CreateSnapshot();
        coordinator = new(state);
    }
    if (changeReason) environment.Pair.PauseReason = "Changed outside the manager";
    environment.Pair.UserPair.OwnPermissions = (Permissions)ulong.Parse(expectedAfter, CultureInfo.InvariantCulture);
    var late = environment.Snapshot();
    SetPairs(coordinator, late);
    Invoke(coordinator, "ObserveExternalChanges");
    if (runningRestore)
    {
        var requests = environment.Api.Requests;
        Assert(state.LateMediaChanges.Count == 1 && state.OwnedPauses[late.Key].OriginalPermissions != expectedOriginal,
            "A running restore blocks interpretation of a competing late callback");
        Invoke(coordinator, "StartRestore", late, state.OwnedPauses[late.Key]);
        Assert(environment.Api.Requests == requests, "No conflicting request is sent while restoration is running");
        restoreCompletion!.SetResult();
        Invoke(coordinator, "ObserveExternalChanges");
    }
    environment.Api.Request = null;
    if (changeReason)
    {
        Assert(state.OwnedPauses.Count == 0, "Different pause reason prevents claiming a late matching external restriction");
        return;
    }
    Assert(state.OwnedPauses.TryGetValue(late.Key, out var owned) && owned.OriginalPermissions == expectedOriginal,
        "Expected late media callback retains or recovers the correct restoration permissions");
    Assert(!state.AutomaticExceptions.Contains(late.Key), "Manager's late media update is not misclassified as an external edit");
    Invoke(coordinator, "StartRestore", late, owned!);
    SetPairs(coordinator, environment.Snapshot());
    Invoke(coordinator, "FinishOperations");
    Assert(environment.Pair.UserPair.OwnPermissions.IsDisableSounds() && !environment.Pair.UserPair.OwnPermissions.IsPaused(),
        "Restoration preserves the requested sound choice while releasing the manager pause");
    Assert(state.OwnedPauses.Count == 0 && state.LateMediaChanges.Count == 0, "Confirmed recovery leaves no active ownership or unsettled history");
}
ExerciseLateMedia(finishRestoreFirst: false, reload: false, changeReason: false);
ExerciseLateMedia(finishRestoreFirst: true, reload: true, changeReason: false);
ExerciseLateMedia(finishRestoreFirst: true, reload: true, changeReason: true);
ExerciseLateMedia(finishRestoreFirst: false, reload: false, changeReason: false, runningRestore: true);
ExerciseLateMedia(finishRestoreFirst: false, reload: false, changeReason: false, completedRestoreAwaitingState: true);

// An unpaused late result may arrive while a previous owned pause still needs confirmation.
TestEnvironment.Reset();
var unpausedEnvironment = new TestEnvironment(SyncProvider.PlayerSync);
var unpausedState = new Configuration();
var unpausedCoordinator = new SyncCoordinator(unpausedState);
var unpausedPair = unpausedEnvironment.Snapshot();
unpausedState.OwnedPauses[unpausedPair.Key] = unpausedEnvironment.Adapter.PreparePause(unpausedPair);
unpausedState.OwnedPauses[unpausedPair.Key].Confirmed = true;
unpausedState.LateMediaChanges[unpausedPair.Key] = [new()
{
    CharacterIdentity = unpausedPair.CharacterIdentity!, After = "2", OriginalAfter = "2", PausedAfter = "3", PauseReason = "",
}];
unpausedEnvironment.Pair.UserPair.OwnPermissions = Permissions.Sounds;
unpausedPair = unpausedEnvironment.Snapshot();
SetPairs(unpausedCoordinator, unpausedPair);
Invoke(unpausedCoordinator, "ObserveExternalChanges");
Assert(unpausedState.OwnedPauses[unpausedPair.Key].RestoreRequested && !unpausedState.AutomaticExceptions.Contains(unpausedPair.Key),
    "Recognized unpaused callback requests restoration confirmation without external-change suspension");
Plugin.PluginInterface.Write = _ => throw new IOException("Simulated recovery save failure");
try { Invoke(unpausedCoordinator, "StartRestore", unpausedPair, unpausedState.OwnedPauses[unpausedPair.Key]); throw new Exception("Recovery dispatched without persistence"); }
catch (IOException) { }
Assert(unpausedEnvironment.Api.Requests == 0, "Recovered permissions must save successfully before dispatching restoration");
Plugin.PluginInterface.Write = null;
Invoke(unpausedCoordinator, "StartRestore", unpausedPair, unpausedState.OwnedPauses[unpausedPair.Key]);
SetPairs(unpausedCoordinator, unpausedEnvironment.Snapshot());
Invoke(unpausedCoordinator, "FinishOperations");
Assert(unpausedState.OwnedPauses.Count == 0 && unpausedEnvironment.Pair.UserPair.OwnPermissions == Permissions.Sounds,
    "Unpaused callback completes recovery while preserving its media choice");

// Cache data is built once per snapshot/config change, not once per UI frame.
TestEnvironment.Reset();
var cacheEnvironment = new TestEnvironment(SyncProvider.PlayerSync);
var cacheConfiguration = new Configuration();
var cacheManager = new SyncCoordinator(cacheConfiguration);
var snapshots = new List<PairSnapshot>();
for (var index = 0; index < 500; index++)
{
    var identity = $"Example{index}@1";
    cacheConfiguration.DuplicateCharacters[identity] = new()
    {
        DisplayName = identity,
        Routes = new() { [$"P{index}"] = SyncProvider.PlayerSync, [$"L{index}"] = SyncProvider.Lightless },
    };
    for (var number = 0; number < 3; number++)
        snapshots.Add(new()
        {
            Provider = SyncProvider.PlayerSync, Key = $"P{index}-{number}", Uid = "remote", Label = "example", Ident = "known",
            CharacterIdentity = identity, Online = true, Permissions = "0", PauseReason = "", Adapter = cacheEnvironment.Adapter,
        });
}
SetPairs(cacheManager, snapshots.ToArray());
var view = cacheManager.Characters;
var allocated = GC.GetAllocatedBytesForCurrentThread();
var same = true;
for (var frame = 0; frame < 1000; frame++) same &= ReferenceEquals(view, cacheManager.Characters);
var drawAllocations = GC.GetAllocatedBytesForCurrentThread() - allocated;
Assert(same && drawAllocations == 0, "Repeated character-view reads reuse the cached snapshot with zero allocations");
Assert(view.Count == 500 && view.All(c => c.Pairs.Count == 3), "Grouped cache preserves all identities and pair associations");
cacheManager.MovePriority(cacheConfiguration.Priority.IndexOf(SyncProvider.Lightless), -1);
var reordered = cacheManager.Characters;
Assert(!ReferenceEquals(view, reordered) && reordered[0].Providers[0] == SyncProvider.Lightless, "Priority changes invalidate the cached provider ordering");
SetPairs(cacheManager);
Assert(!ReferenceEquals(reordered, cacheManager.Characters) && cacheManager.Characters.All(c => !c.Online && c.Pairs.Count == 0),
    "Snapshot replacement refreshes offline state without forgetting indexed characters");
cacheConfiguration.LateMediaChanges["old"] = [new() { After = "3", OriginalAfter = "2", PausedAfter = "3" }];
cacheManager.ClearCache();
Assert(cacheManager.Characters.Count == 0 && cacheConfiguration.LateMediaChanges.Count == 1,
    "Cache clearing invalidates the view and retains unsettled restoration history");

await RecoveryChecks.Run(Assert);
await ManagementChecks.Run(Assert);
SelectionChecks.Run(Assert);
Console.WriteLine($"Coordinator and native-adapter checks passed: {checks}; cached draw allocations: {drawAllocations}");
