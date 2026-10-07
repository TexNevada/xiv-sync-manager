using System;

namespace XivSyncManager;

internal static class AudioDiagnostics
{
    internal static SoundActivity Read(object pair, SyncProvider provider)
    {
        try
        {
            if (provider == SyncProvider.Snowcloak)
                return new(false, null, false, null, "This Snowcloak version does not expose per-player audio activity.");
            if (provider == SyncProvider.PlayerSync)
            {
                var property = pair.GetType().GetProperty("LastLoadedSoundSinceRedraw", ReflectionAccess.Members);
                if (property == null) return new(false, null, false, null, "This PlayerSync version has no sound-load counter.");
                var loaded = property.GetValue(pair) is DateTimeOffset time ? time.UtcDateTime : (DateTime?)null;
                return new(true, loaded, false, null,
                    "PlayerSync observed a sound resource loading for this player or their companion. Loading does not confirm playback.");
            }
            // The native snapshot leases the handler and releases it before returning.
            var method = ReflectionAccess.Method(pair, "GetDisplayRuntimeState", 2);
            if (method == null) return new(false, null, false, null, "This Lightless version has no sound activity snapshot.");
            var state = method.Invoke(pair, [false, true]);
            var activity = ReflectionAccess.Read(state, "RecentSoundActivity");
            var observed = ReflectionAccess.Read(activity, "LastObservedAtUtc") is DateTime date ? date.ToUniversalTime() : (DateTime?)null;
            return new(true, observed, ReflectionAccess.Boolean(ReflectionAccess.Read(activity, "IsCurrentlyPlaying")), null,
                "Lightless attributes loaded sound resources to this player or their companion and checks whether their sound paths are playing. Shared sound paths can make attribution ambiguous.");
        }
        catch
        {
            return new(false, null, false, null, "Audio activity could not be read from this plugin version.");
        }
    }
}
