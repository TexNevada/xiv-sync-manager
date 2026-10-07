using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace XivSyncManager;

internal sealed class ProfileIntegration
{
    private readonly SyncProvider provider;
    private readonly object? manager;
    private readonly object? viewer;
    private readonly MethodInfo? openProfile;
    private ILookup<string, object>? snowProfiles;

    internal ProfileIntegration(object plugin, SyncProvider provider)
    {
        this.provider = provider;
        // Profile support is optional; a changed contract must not disable pair management.
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
            object? Resolve(string suffix)
            {
                try
                {
                    var type = prefixes.Select(p => assembly.GetType($"{p}.{suffix}")).FirstOrDefault(t => t != null);
                    return type == null ? null : services.GetService(type);
                }
                catch { return null; }
            }

            manager = provider == SyncProvider.PlayerSync ? null
                : Resolve(provider == SyncProvider.Lightless ? "Services.LightlessProfileManager" : "Services.SnowProfileManager");
            viewer = Resolve(provider == SyncProvider.Snowcloak
                ? "UI.Handlers.UidDisplayHandler" : "UI.Handlers.IdDisplayHandler");
            openProfile = viewer?.GetType().GetMethods(ReflectionAccess.Members).FirstOrDefault(m =>
                m.Name == "OpenProfile" && m.ReturnType == typeof(void) && m.GetParameters() is [{ ParameterType: var type }]
                && type.Name == "Pair");
        }
        catch { }
    }

    internal void Refresh()
    {
        snowProfiles = null;
        if (provider != SyncProvider.Snowcloak) return;
        try
        {
            // Snapshot the existing cache once per refresh. Do not call getters that fetch profiles
            // or expire entries in the original plugin's cache.
            if (ReflectionAccess.Read(manager, "_profiles") is { } cache)
                snowProfiles = ReflectionAccess.Values(cache).ToLookup(
                    p => ReflectionAccess.Text(ReflectionAccess.Read(p, "Ident")), StringComparer.Ordinal);
        }
        catch { }
    }

    internal ProfileStatus Read(object pair, object user, bool ownPaused, bool otherPaused)
    {
        var canOpen = viewer != null && openProfile?.GetParameters()[0].ParameterType.IsInstanceOfType(pair) == true;
        var reason = canOpen ? string.Empty : "This plugin version's profile viewer is unavailable.";
        try
        {
            if (provider == SyncProvider.PlayerSync && (ownPaused || otherPaused
                || ReflectionAccess.Boolean(ReflectionAccess.Read(pair, "IsPaused"))))
                return new(ProfileAvailability.Paused,
                    otherPaused ? "They have paused this PlayerSync pair. PlayerSync hides profiles while either side is paused."
                        : "This PlayerSync pair is paused on your side. PlayerSync hides profiles while either side is paused.",
                    false, "The pair must be unpaused in PlayerSync before its profile can be opened. Automatic management may be keeping this route paused.");
            if (provider == SyncProvider.Snowcloak && string.IsNullOrWhiteSpace(ReflectionAccess.Text(ReflectionAccess.Read(pair, "Ident"))))
            {
                canOpen = false;
                reason = "Snowcloak has not identified this character. The profile can be opened when their character is identified again.";
            }
            var status = provider switch
            {
                SyncProvider.Lightless => ReadLightless(user),
                SyncProvider.Snowcloak => ReadSnowcloak(pair, user),
                _ => ReadPlayerSync(pair, user),
            };
            return status with { CanOpen = canOpen, OpenUnavailableReason = reason };
        }
        catch
        {
            return new(ProfileAvailability.Unknown, "Profile information could not be read from this sync.", canOpen, reason);
        }
    }

    internal void Open(object pair)
    {
        if (viewer == null || openProfile == null || !openProfile.GetParameters()[0].ParameterType.IsInstanceOfType(pair))
            throw new NotSupportedException("This sync's profile viewer is unavailable in the installed version.");
        if (provider == SyncProvider.Snowcloak && string.IsNullOrWhiteSpace(ReflectionAccess.Text(ReflectionAccess.Read(pair, "Ident"))))
            throw new InvalidOperationException("Snowcloak has not identified this character yet.");
        // Use the same UI handler as the native pair list, including its visibility preferences.
        openProfile.Invoke(viewer, [pair]);
    }

    private static ProfileStatus ReadPlayerSync(object pair, object user)
    {
        // Pair.HasProfile defaults an unknown nullable flag to false. Preserve that distinction.
        var flag = ReflectionAccess.Read(pair, "_hasProfile") ?? ReflectionAccess.Read(user, "HasProfile");
        return flag switch
        {
            true => new(ProfileAvailability.Available, "PlayerSync reports a profile for this account."),
            false => new(ProfileAvailability.NoProfile, "PlayerSync reports no profile for this account."),
            _ => new(ProfileAvailability.Unknown, "PlayerSync has not reported whether this account has a profile."),
        };
    }

    private ProfileStatus ReadLightless(object user)
    {
        if (manager == null) return new(ProfileAvailability.Unknown, "Lightless's profile checks are unavailable.");
        var profile = Lookup(ReflectionAccess.Read(manager, "_userProfiles"), user);
        if (profile == null)
            return new(ProfileAvailability.Unknown, "Lightless has no cached profile for this account yet. Click to open it in Lightless.");

        // This inspected native filter applies profile visibility settings without fetching data.
        var filter = ReflectionAccess.Method(manager, "ApplyUserVisibility", 2);
        if (filter == null)
            return new(ProfileAvailability.Unknown, "Lightless's profile visibility check is unavailable in this version.");
        profile = filter.Invoke(manager, [user, profile]);
        if (ReflectionAccess.Boolean(ReflectionAccess.Read(profile, "IsModerationDisabled")))
            return new(ProfileAvailability.Unavailable, "Lightless reports that this profile is unavailable.");
        return ReflectionAccess.Text(ReflectionAccess.Read(profile, "State")) switch
        {
            "Ready" => new(ProfileAvailability.Cached, "Lightless has loaded profile data for this account. Cached data may include an empty profile."),
            "Blocked" => new(ProfileAvailability.Unavailable, "This profile is hidden by Lightless's profile settings."),
            "Unavailable" => new(ProfileAvailability.Unavailable, "Lightless could not load this profile. This does not confirm that no profile exists."),
            _ => new(ProfileAvailability.Unknown, "Lightless has not finished loading this profile."),
        };
    }

    private ProfileStatus ReadSnowcloak(object pair, object user)
    {
        if (manager == null) return new(ProfileAvailability.Unknown, "Snowcloak's profile checks are unavailable.");
        var ident = ReflectionAccess.Text(ReflectionAccess.Read(pair, "Ident"));
        if (string.IsNullOrEmpty(ident))
            return new(ProfileAvailability.Unknown, "Snowcloak has not identified this character's profile.");
        var uid = ReflectionAccess.Text(ReflectionAccess.Read(user, "UID"));
        bool MatchesUser(object profile) => ReflectionAccess.Text(
            ReflectionAccess.Read(ReflectionAccess.Read(profile, "User"), "UID")) == uid;

        // The native cache includes public summaries and full profiles already obtained by this client.
        // Only read availability; the native viewer handles access and displays the contents.
        var cached = snowProfiles?[ident].Where(p => MatchesUser(p)
            && ReflectionAccess.Text(ReflectionAccess.Read(p, "Visibility")) is "Public" or "Private")
            .OrderByDescending(p => ReflectionAccess.Read(p, "Revision") is long sortRevision ? sortRevision : 0)
            .ThenBy(p => ReflectionAccess.Boolean(ReflectionAccess.Read(p, "Disabled"))).FirstOrDefault();
        if (cached != null)
        {
            if (ReflectionAccess.Boolean(ReflectionAccess.Read(cached, "Disabled")))
                return new(ProfileAvailability.Unavailable, "Snowcloak reports that this cached profile is unavailable.");
            if (ReflectionAccess.Read(cached, "Revision") is long cachedRevision && cachedRevision > 0
                && string.IsNullOrEmpty(ReflectionAccess.Text(ReflectionAccess.Read(cached, "DisabledReason"))))
                return new(ProfileAvailability.Cached, "Snowcloak has cached profile data for this character. Its viewer controls which parts can be viewed.");
        }
        var summary = Lookup(ReflectionAccess.Read(manager, "_summaries"), ident);
        if (summary != null && MatchesUser(summary)
            && (ReflectionAccess.Read(summary, "Revision") is long summaryRevision && summaryRevision > 0
                || ReflectionAccess.Boolean(ReflectionAccess.Read(summary, "HasDetailedBio"))))
            return new(ProfileAvailability.Cached, "Snowcloak has a cached public profile summary for this character.");
        return new(ProfileAvailability.Unknown, "Snowcloak has no confirmed profile in its cache yet. Click to open its profile viewer.");
    }

    private static object? Lookup(object? dictionary, object key)
    {
        if (dictionary == null) return null;
        var method = dictionary.GetType().GetMethods(ReflectionAccess.Members).FirstOrDefault(m =>
            m.Name == "TryGetValue" && m.ReturnType == typeof(bool) && m.GetParameters() is [var input, var output]
            && input.ParameterType.IsInstanceOfType(key) && output.IsOut);
        if (method == null) return null;
        object?[] args = [key, null];
        return method.Invoke(dictionary, args) is true ? args[1] : null;
    }
}
