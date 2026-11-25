using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Plugin;

namespace XivSyncManager;

internal enum SyncshellControl { Pause, Sound, Vfx, Animations, Profile }
internal enum SyncshellAvailability { Available, Unsupported, Unavailable }
internal readonly record struct SyncshellKey(SyncProvider Provider, string Gid);
internal readonly record struct SyncshellSetting(bool? Value, SyncshellAvailability Availability, string Reason = "");
internal sealed record SyncshellMember(string Uid, string Label, object Pair);
internal sealed record SyncshellRow(SyncshellKey Key, string Name, SyncshellConnection Connection, SyncshellSetting[] Settings, string[]? Members)
{
    internal SyncshellMember[]? NativeMembers { get; init; }
    internal SyncshellSetting this[SyncshellControl control] => Settings[(int)control];
}
internal sealed record SyncshellProviderView(bool CanList, string? Notice, SyncshellRow[] Rows);
internal sealed record SyncshellConnection(object Plugin, object? Session, string? ConnectionId, string Uid, bool Connected, bool Ready)
{
    internal bool CanAct => Connected && Ready && Session != null && !string.IsNullOrWhiteSpace(Uid);
    internal bool SameSnapshot(SyncshellConnection other) => ReferenceEquals(Plugin, other.Plugin)
        && Connected == other.Connected && Ready == other.Ready && ReferenceEquals(Session, other.Session)
        && ConnectionId == other.ConnectionId && Uid == other.Uid;
    internal bool Matches(SyncshellConnection other) => CanAct && other.CanAct && SameSnapshot(other);
}

// Transient display snapshots and in-flight feedback; PlayerSync reason changes use the coordinator's ownership gate.
internal sealed class Syncshells(Func<IEnumerable<IExposedPlugin>> installedPlugins,
    Action<IReadOnlyList<object>, string, Action>? prepareReasons = null)
{
    internal static readonly SyncProvider[] ProviderOrder = [SyncProvider.Lightless, SyncProvider.Snowcloak, SyncProvider.PlayerSync];
    internal static readonly SyncshellControl[] Controls = Enum.GetValues<SyncshellControl>();
    private readonly HashSet<SyncProvider> hidden = [];
    private readonly Dictionary<SyncProvider, SyncshellProviderView> providers = [];
    private readonly Dictionary<SyncshellKey, Operation> operations = [];
    private readonly Dictionary<SyncshellKey, string> errors = [];
    private SyncshellRow[] rows = [];

    internal IReadOnlyDictionary<SyncProvider, SyncshellProviderView> Providers => providers;
    internal IEnumerable<SyncshellRow> VisibleRows => rows.Where(row => IsVisible(row.Key.Provider));
    internal bool AnyVisibleProvider => ProviderOrder.Any(IsVisible);
    internal IReadOnlyDictionary<SyncshellKey, string> Errors => errors;
    internal void DismissErrors() => errors.Clear();
    internal bool IsVisible(SyncProvider provider) => !hidden.Contains(provider);
    internal void Toggle(SyncProvider provider) { if (!hidden.Remove(provider)) hidden.Add(provider); }
    internal SyncshellControl? Pending(SyncshellKey key) => operations.TryGetValue(key, out var operation) ? operation.Control : null;
    internal bool PendingAfterFailure(SyncshellKey key) => operations.GetValueOrDefault(key)?.Failure != null;
    internal bool IsProfilePending(SyncProvider provider) => operations.Any(entry => entry.Key.Provider == provider
        && entry.Value.Control == SyncshellControl.Profile);

    internal void Refresh(TimeSpan now, Func<SyncshellKey, bool>? expanded = null)
    {
        var installed = installedPlugins().ToArray();
        foreach (var provider in ProviderOrder)
            providers[provider] = SyncshellIntegration.Read(provider, installed, IsVisible(provider) ? expanded : null);
        rows = providers.Values.SelectMany(p => p.Rows)
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Key.Provider)
            .ThenBy(row => row.Key.Gid, StringComparer.Ordinal).ToArray();
        if (operations.Count == 0) return;

        var current = rows.ToDictionary(row => row.Key);
        foreach (var (key, operation) in operations.ToArray())
        {
            var row = current.GetValueOrDefault(key);
            if (operation.Failure == null)
            {
                if (row == null || !operation.Row.Connection.Matches(row.Connection))
                    operation.Failure = "The shell or sync connection changed before confirmation. Nothing will replay.";
                else if (operation.Request.IsFaulted)
                    operation.Failure = ReflectionAccess.Unwrap(operation.Request.Exception!.GetBaseException()).Message;
                else if (operation.Request.IsCanceled)
                    operation.Failure = "The provider cancelled the request.";
                else if (operation.Request.IsCompletedSuccessfully)
                {
                    try
                    {
                        var confirmed = operation.ProfileOpened?.Invoke()
                            ?? (operation.Expected is { } expected && row[operation.Control].Value == expected);
                        if (confirmed)
                        {
                            operations.Remove(key);
                            continue;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // The native window list may change while the provider processes UI messages.
                        // Check again on the next refresh instead of assuming it opened.
                    }
                    catch (Exception exception) { operation.Failure = ReflectionAccess.Unwrap(exception).Message; }
                }

                if (operation.Failure == null && now - operation.Started >= TimeSpan.FromSeconds(15))
                    operation.Failure = "The provider did not confirm this action. Check its native menu before retrying.";
                if (operation.Failure != null) Report(operation.Row, operation.Control, operation.Failure);
            }
            // If a timed-out request is still running, don't send another permission mask on top of it.
            if (operation.Failure != null && operation.Request.IsCompleted) operations.Remove(key);
        }
    }

    internal void Act(SyncshellRow row, SyncshellControl control, TimeSpan now)
    {
        if (operations.ContainsKey(row.Key) || !IsVisible(row.Key.Provider)
            || (control == SyncshellControl.Profile && IsProfilePending(row.Key.Provider))) return;
        var setting = row[control];
        if (setting.Availability != SyncshellAvailability.Available)
        {
            Report(row, control, setting.Reason);
            return;
        }
        try
        {
            errors.Remove(row.Key);
            var (request, profileOpened) = SyncshellIntegration.Start(row, control, installedPlugins(), prepareReasons);
            // Observe faults even if the UI is closed or the manager unloads before the next refresh.
            _ = request.ContinueWith(static task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            operations.Add(row.Key, new(row, control, setting.Value is { } value ? !value : null, now, request, profileOpened));
        }
        catch (Exception exception) { Report(row, control, ReflectionAccess.Unwrap(exception).Message); }
    }

    private void Report(SyncshellRow row, SyncshellControl control, string reason) =>
        errors[row.Key] = $"{row.Key.Provider.DisplayName()} — {row.Name} ({Label(control)}): {reason}";

    internal static string Label(SyncshellControl control) => control switch
    {
        SyncshellControl.Pause => "Pause / Resume", SyncshellControl.Vfx => "VFX", _ => control.ToString(),
    };

    private sealed record Operation(SyncshellRow Row, SyncshellControl Control, bool? Expected, TimeSpan Started, Task Request, Func<bool>? ProfileOpened)
    {
        internal string? Failure { get; set; }
    }
}
