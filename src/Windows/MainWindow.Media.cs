using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace XivSyncManager.Windows;

public sealed partial class MainWindow
{
    private void DrawMediaPopup(string popup, string title, IReadOnlyList<PairSnapshot> pairs, IEnumerable<SyncProvider> providers)
    {
        ImGui.SetNextWindowSizeConstraints(Vector2.Zero, new Vector2(620, 650) * ImGuiHelpers.GlobalScale);
        if (!ImGui.BeginPopup(popup)) return;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 540 * ImGuiHelpers.GlobalScale);
        ImGui.TextUnformatted(title);
        ImGui.TextWrapped("Choose which mod types may sync. Restrictions work in both directions. Resume removes your restriction; the other player's restrictions still apply.");
        ImGui.TextDisabled("These choices are saved by the sync services and remain after closing this manager.");
        ImGui.Separator();
        DrawMediaControls("All sync plugins", pairs);
        foreach (var provider in plugin.Configuration.Priority.Where(providers.Contains))
        {
            using var id = ImRaii.PushId(provider.ToString());
            ImGui.Separator();
            var routes = pairs.Where(p => p.Provider == provider).ToArray();
            DrawMediaControls(provider.DisplayName(), routes);
        }
        if (pairs.Count == 0)
            ImGui.TextWrapped("No identified pairs are available. Reconnect the sync and wait for this character to be identified.");
        var errors = pairs.Select(plugin.Coordinator.MediaErrorFor).Where(e => e != null).Distinct().ToArray();
        foreach (var error in errors) ImGui.TextWrapped("Could not update: " + error);
        if (popup == "globalMedia")
            foreach (var error in plugin.Coordinator.MediaErrors.Except(pairs.Where(p => plugin.Coordinator.MediaErrorFor(p) != null)
                         .Select(p => $"{p.Provider.DisplayName()}: {plugin.Coordinator.MediaErrorFor(p)}")))
                ImGui.TextWrapped("Could not update: " + error);
        ImGui.Separator();
        ImGui.TextWrapped("Only listed pairs on connected services are changed. Disconnected services and future pairs are excluded. Normal game effects can remain; this controls synced animation, sound, and VFX mods. Individual choices override syncshell defaults where the sync supports that.");
        ImGui.PopTextWrapPos();
        ImGui.EndPopup();
    }

    private void DrawMediaControls(string title, IReadOnlyList<PairSnapshot> pairs)
    {
        var coordinator = plugin.Coordinator;
        ImGui.TextUnformatted(title);
        var editable = pairs.Where(p => p.Media.CanEdit).ToArray();
        var busy = editable.Any(p => !coordinator.CanChangeMedia(p));
        for (var index = 0; index < 3; index++)
        {
            var kind = index switch { 0 => MediaKind.Animations, 1 => MediaKind.Sounds, _ => MediaKind.Vfx };
            var label = index switch { 0 => "Animations", 1 => "Sounds", _ => "VFX" };
            var allPaused = editable.Length > 0 && editable.All(p => p.Media.Disabled.HasFlag(kind));
            var mixed = editable.Any(p => p.Media.Disabled.HasFlag(kind)) && !allPaused;
            var otherPaused = editable.Count(p => p.Media.OtherDisabled.HasFlag(kind));
            if (index > 0) ImGui.SameLine();
            var colour = allPaused ? new Vector4(0.6f, 0.12f, 0.16f, 1) : new Vector4(0.1f, 0.4f, 0.18f, 1);
            using (ImRaii.Disabled(editable.Length == 0 || busy))
            using (ImRaii.PushColor(ImGuiCol.Button, colour))
                if (ImGui.Button($"{(allPaused ? "Resume" : "Pause")} {label}{(mixed ? " (mixed)" : "")}##{title}{kind}"))
                    coordinator.SetMedia(editable, kind, !allPaused);
            Tooltip(editable.Length == 0 ? "No editable pairs are available on this sync."
                : busy ? "Wait for pending sync or media changes to finish."
                : $"{(allPaused ? "Remove your restriction" : "Disable this mod type")} for {editable.Length} pair(s)."
                    + (mixed ? " Some pairs already have this restriction." : "")
                    + (otherPaused > 0 ? $"\n{otherPaused} pair(s) also have this disabled on the other player's side. You cannot remove their restriction." : ""));
        }
        if (busy) ImGui.TextDisabled("Updating…");
        var theirRestrictions = new[] { MediaKind.Animations, MediaKind.Sounds, MediaKind.Vfx }
            .Where(kind => editable.Any(p => p.Media.OtherDisabled.HasFlag(kind)))
            .Select(kind => kind == MediaKind.Vfx ? "VFX" : kind.ToString());
        if (theirRestrictions.Any())
            ImGui.TextWrapped("The other side has restrictions on: " + string.Join(", ", theirRestrictions) + ". Resume cannot remove those.");
        foreach (var unavailable in pairs.Where(p => !p.Media.CanEdit).Select(p => p.Media.Description).Distinct())
            ImGui.TextWrapped("Controls unavailable: " + unavailable);
        if (pairs.Count == 0) ImGui.TextDisabled("Disconnected or no identified pair.");
    }

    private void DrawRecentAudio()
    {
        ImGui.TextUnformatted("Recent audio · last 60 seconds");
        var pairs = plugin.Coordinator.Pairs.Where(p => p.Audio.Recent)
            .OrderByDescending(p => p.Audio.Playing).ThenByDescending(p => p.Audio.LastPlayedAt ?? p.Audio.LastLoadedAt).ToArray();
        if (pairs.Length == 0) ImGui.TextDisabled("No recent audio activity reported.");
        foreach (var pair in pairs)
        {
            using var id = ImRaii.PushId(pair.Key);
            ImGui.Separator();
            ImGui.TextUnformatted(pair.CharacterName ?? (pair.PlayerName.Length > 0 ? pair.PlayerName : pair.Label + " (account)"));
            ImGui.TextColored(pair.Audio.Playing ? ConnectedGreen : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], AudioLabel(pair));
            Tooltip(pair.Audio.Description);
            ImGui.SameLine();
            if (ImGui.SmallButton("Media…")) ImGui.OpenPopup("audioMedia");
            DrawMediaPopup("audioMedia", pair.CharacterName ?? pair.Label, [pair], [pair.Provider]);
        }
        var unavailable = plugin.Coordinator.Pairs.Where(p => !p.Audio.Supported).Select(p => p.Audio.Description).Distinct();
        foreach (var reason in unavailable) ImGui.TextDisabled(reason);
    }

    private static string AudioLabel(PairSnapshot pair)
    {
        var state = pair.Audio.Playing ? "Playing audio"
            : pair.Audio.LastPlayedAt != null ? "Audio recently playing" : "Sound loaded";
        var at = pair.Audio.LastPlayedAt ?? pair.Audio.LastLoadedAt;
        var elapsed = at == null || pair.Audio.Playing ? "" : $" · {Math.Max(0, (int)(DateTime.UtcNow - at.Value).TotalSeconds)}s ago";
        return $"{pair.Provider.DisplayName()} · {state}{elapsed}";
    }
}
