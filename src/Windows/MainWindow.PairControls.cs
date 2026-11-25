using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace XivSyncManager.Windows;

public sealed partial class MainWindow
{
    private readonly HashSet<string> expandedCharacters = [];

    private void DrawSyncListRoute(PairSnapshot pair)
    {
        using var id = ImRaii.PushId(pair.Key);
        ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight() + 2 * ImGui.GetStyle().CellPadding.Y);
        ImGui.TableSetColumnIndex(0);
        if (!ImGui.IsRectVisible(new Vector2(1, ImGui.GetFrameHeight()))) return;
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(ShellServiceName(pair.Provider) + " · " + ShellSingleLine(pair.Label));
        Tooltip($"{pair.CharacterName ?? pair.Label}\nNative UID: {pair.Uid}");
        ImGui.TableSetColumnIndex(1);
        DrawPairProfile(pair);
        ImGui.TableSetColumnIndex(2);
        DrawPairMedia(pair);
        ImGui.TableSetColumnIndex(3);
        ImGui.TextUnformatted(PairState(pair));
        if (plugin.Coordinator.ActionErrorFor(pair) is { } error) Tooltip(error);
        ImGui.TableSetColumnIndex(4);
        DrawPairPause(pair);
        ImGui.SameLine();
        DrawPairRecovery(pair);
    }

    private string PairState(PairSnapshot pair) => plugin.Coordinator.IsPending(pair) ? "Updating…"
        : plugin.Coordinator.IsRetrying(pair) ? "Retrying automatically…"
        : pair.ManagerHeld || pair.OwnPaused || pair.OtherPaused || pair.ExternalHold ? "Paused"
        : pair.Visible ? "Nearby" : pair.Online ? "Online" : "Offline";

    private void DrawPairPause(PairSnapshot pair)
    {
        var coordinator = plugin.Coordinator;
        var resume = coordinator.IsManual(pair) || coordinator.IsOwned(pair);
        var external = pair.ExternalHold || pair.OtherPaused || pair.OwnPaused && !coordinator.IsOwned(pair);
        var characterPaused = pair.CharacterIdentity != null && plugin.Configuration.CharacterPauses.Contains(pair.CharacterIdentity);
        using (ImRaii.Disabled(coordinator.IsPending(pair) || external && !resume || characterPaused))
            if (ImGuiComponents.IconButton("pairPause", resume ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause))
                coordinator.SetManualPause(pair, !resume);
        Tooltip(characterPaused ? "This character is paused across all routes. Resume the character first."
            : external && !resume ? "Paused by native permissions or another player. Resume in the original plugin; the manager will not silently override it."
            : resume ? "Release this route's manager-owned pause. Automatic priority and native restrictions still apply."
            : "Pause this provider route. The manager saves its restoration record before dispatch.");
    }

    private void DrawPairProfile(PairSnapshot pair)
    {
        using (ImRaii.Disabled(!pair.Profile.CanOpen))
            if (ImGui.Button("Open##pairProfile")) plugin.Coordinator.OpenProfile(pair);
        Tooltip(plugin.Coordinator.ProfileErrorFor(pair) ?? pair.Profile.Description);
    }

    private void DrawPairMedia(PairSnapshot pair)
    {
        var coordinator = plugin.Coordinator;
        foreach (var kind in new[] { MediaKind.Sounds, MediaKind.Vfx, MediaKind.Animations })
        {
            if (kind != MediaKind.Sounds) ImGui.SameLine();
            using var id = ImRaii.PushId(kind.ToString());
            var on = !pair.Media.Disabled.HasFlag(kind);
            var label = kind == MediaKind.Sounds ? "Sound" : kind == MediaKind.Vfx ? "VFX" : "Animations";
            var color = on ? new Vector4(0.1f, 0.4f, 0.18f, 1) : new Vector4(0.42f, 0.28f, 0.06f, 1);
            using (ImRaii.Disabled(!coordinator.CanChangeMedia(pair)))
            using (ImRaii.PushColor(ImGuiCol.Button, color))
            using (ImRaii.PushColor(ImGuiCol.Text, Vector4.One))
                if (ImGui.Button($"{label}: {(pair.Media.CanEdit ? on ? "On" : "Off" : "N/A")}"))
                    coordinator.SetMedia([pair], kind, on);
            Tooltip(coordinator.MediaErrorFor(pair) ?? (!pair.Media.CanEdit ? pair.Media.Description
                : coordinator.IsPending(pair) ? "Wait for pending changes."
                : $"{(on ? "Disable" : "Enable")} {label} for this provider account. Individual settings can override shell defaults."
                    + (pair.Media.OtherDisabled.HasFlag(kind) ? " The other player also disables this type; you cannot remove their restriction." : "")));
        }
    }

    private void DrawPairRecovery(PairSnapshot pair)
    {
        var coordinator = plugin.Coordinator;
        using var id = ImRaii.PushId("recovery:" + pair.Key);
        var reloadReason = coordinator.ReloadUnavailableReason(pair);
        using (ImRaii.Disabled(reloadReason != null))
            if (ImGuiComponents.IconButton("reload", FontAwesomeIcon.Sync)) coordinator.ReloadLastData(pair);
        Tooltip(reloadReason ?? "Reload last data using this plugin's native action. This reapplies its last received appearance; it does not fetch new data.");
        ImGui.SameLine();
        var cycleReason = coordinator.CycleUnavailableReason(pair);
        using (ImRaii.Disabled(cycleReason != null))
            if (ImGuiComponents.IconButton("cycle", FontAwesomeIcon.PlayCircle)) coordinator.CyclePauseState(pair);
        Tooltip(cycleReason ?? "Cycle pause state using the native action: temporarily pause, then resume this route. Existing pauses are protected.");
        if (coordinator.ActionErrorFor(pair) is { } error)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted("!");
            Tooltip(error);
        }
    }
}
