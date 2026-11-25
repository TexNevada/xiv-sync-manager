using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace XivSyncManager.Windows;

public sealed partial class MainWindow
{
    private readonly Syncshells syncshells;
    private SyncshellRow? shellPauseConfirmation;
    private bool openShellPauseConfirmation;
    private readonly HashSet<SyncshellKey> expandedShells = [];
    private readonly Stopwatch shellClock = Stopwatch.StartNew();
    private TimeSpan nextShellRefresh;

    private void DrawSyncshells()
    {
        var now = shellClock.Elapsed;
        // Refresh on a timer, not when toggling providers. The buttons only filter the rows.
        if (now >= nextShellRefresh) RefreshShells(now);

        var firstFilter = true;
        foreach (var provider in Syncshells.ProviderOrder)
        {
            using var id = ImRaii.PushId(provider.ToString());
            var visible = syncshells.IsVisible(provider);
            var label = $"{provider.DisplayName()}: {(visible ? "On" : "Off")}";
            if (!firstFilter) ShellSameLineIfFits(label);
            using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)(visible ? ImGuiCol.HeaderActive : ImGuiCol.FrameBg)]))
                if (ImGui.Button($"{label}###filter")) syncshells.Toggle(provider);
            Tooltip(visible ? "Hide this provider's rows." : "Show this provider's rows.");
            firstFilter = false;
        }
        ShellSameLineIfFits("Refresh");
        if (ImGui.Button("Refresh##shells")) RefreshShells(now);
        Tooltip("Read the joined shells and settings again from the loaded plugins.");
        ImGui.TextWrapped("These are your native shell settings, separate from character priorities. Provider rules and individual pair settings still apply.");
        ImGui.Spacing();

        if (syncshells.Errors.Count != 0)
        {
            foreach (var error in syncshells.Errors.Values) ShellWrappedText(error);
            if (ImGui.SmallButton("Dismiss##shells")) syncshells.DismissErrors();
            ImGui.Spacing();
        }

        if (!syncshells.AnyVisibleProvider)
        {
            ImGui.TextDisabled("All providers are hidden. Turn one on to show its shells.");
            return;
        }

        var anyAvailable = false;
        foreach (var provider in Syncshells.ProviderOrder.Where(syncshells.IsVisible))
        {
            var view = syncshells.Providers[provider];
            anyAvailable |= view.CanList;
            if (view.Notice is { } notice) ShellWrappedText($"{provider.DisplayName()}: {notice}");
        }
        var rows = syncshells.VisibleRows.ToArray();
        if (rows.Length == 0)
        {
            ImGui.TextDisabled(anyAvailable ? "No joined shells on the available providers." : "No visible provider can list joined shells right now.");
            return;
        }

        var style = ImGui.GetStyle();
        var pauseWidth = Math.Max(ImGui.GetFrameHeight(), ShellButtonWidth("Unavailable"));
        var profileWidth = Math.Max(pauseWidth, ShellButtonWidth("Opening…"));
        var serviceWidth = ImGui.GetFrameHeight() + style.ItemSpacing.X
            + Syncshells.ProviderOrder.Max(provider => ImGui.CalcTextSize(ShellServiceName(provider)).X);
        var mediaWidth = ShellMediaWidth("Sound") + ShellMediaWidth("VFX") + ShellMediaWidth("Animations")
            + 2 * style.ItemSpacing.X;
        var recoveryWidth = 3 * ImGui.GetFrameHeight() + 2 * style.ItemSpacing.X;
        var minimumWidth = serviceWidth + ImGui.CalcTextSize("Syncshells").X
            + pauseWidth + mediaWidth + profileWidth + recoveryWidth + 12 * style.CellPadding.X;
        var horizontalScroll = minimumWidth > ImGui.GetContentRegionAvail().X - style.ScrollbarSize;
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;
        if (horizontalScroll) flags |= ImGuiTableFlags.ScrollX;
        if (!ImGui.BeginTable("JoinedSyncshells", 6, flags, new Vector2(0, -1), horizontalScroll ? minimumWidth : 0)) return;
        try
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Service", ImGuiTableColumnFlags.WidthFixed, serviceWidth);
            ImGui.TableSetupColumn("Syncshells", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Pause state", ImGuiTableColumnFlags.WidthFixed, pauseWidth);
            ImGui.TableSetupColumn("Media permissions", ImGuiTableColumnFlags.WidthFixed, mediaWidth);
            ImGui.TableSetupColumn("Profile", ImGuiTableColumnFlags.WidthFixed, profileWidth);
            ImGui.TableSetupColumn("Recovery", ImGuiTableColumnFlags.WidthFixed, recoveryWidth);
            ImGui.TableHeadersRow();
            foreach (var row in rows) DrawShellRow(row, now, profileWidth);
        }
        finally { ImGui.EndTable(); }
    }

    private void RefreshShells(TimeSpan now)
    {
        syncshells.Refresh(now, expandedShells.Count == 0 ? null : expandedShells.Contains);
        nextShellRefresh = now + TimeSpan.FromSeconds(1);
    }

    private void DrawShellRow(SyncshellRow row, TimeSpan now, float profileWidth)
    {
        using var providerId = ImRaii.PushId(row.Key.Provider.ToString());
        using var shellId = ImRaii.PushId(row.Key.Gid);
        var expanded = expandedShells.Contains(row.Key);
        var provider = row.Key.Provider.DisplayName();
        var shellTooltip = $"{row.Name}\n{provider} • Shell ID: {row.Key.Gid}";
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (ImGui.ArrowButton("members", expanded ? ImGuiDir.Down : ImGuiDir.Right))
        {
            if (expanded) expandedShells.Remove(row.Key);
            else expandedShells.Add(row.Key);
            nextShellRefresh = now;
        }
        Tooltip(expanded ? "Hide shell members" : "Show shell members");
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(ShellServiceName(row.Key.Provider));
        Tooltip(shellTooltip);
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(ShellSingleLine(row.Name));
        Tooltip(shellTooltip);

        var pending = syncshells.Pending(row.Key);

        ImGui.TableNextColumn();
        DrawShellControl(row, SyncshellControl.Pause, string.Empty, pending, now);

        ImGui.TableNextColumn();
        DrawMedia(SyncshellControl.Sound);
        ImGui.SameLine();
        DrawMedia(SyncshellControl.Vfx);
        ImGui.SameLine();
        DrawMedia(SyncshellControl.Animations);
        ImGui.TableNextColumn();
        DrawShellControl(row, SyncshellControl.Profile, "Open", pending, now,
            new Vector2(profileWidth, 0));

        if (expanded) DrawShellMembers(row);

        void DrawMedia(SyncshellControl control)
        {
            var state = row[control].Value switch { true => "On", false => "Off", null => "—" };
            DrawShellControl(row, control, $"{Syncshells.Label(control)}: {state}", pending, now,
                new Vector2(ShellMediaWidth(Syncshells.Label(control)), 0));
        }
    }

    private void DrawShellMembers(SyncshellRow row)
    {
        if (row.NativeMembers is not { Length: > 0 } members)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(1);
            ImGui.TextDisabled(row.NativeMembers == null ? "Member list unavailable" : "No members reported");
            return;
        }
        // Native object identity + provider + UID, never a display alias.
        var current = plugin.Coordinator.Pairs.Where(p => p.Provider == row.Key.Provider)
            .ToDictionary(p => p.Uid, StringComparer.Ordinal);
        foreach (var member in members)
        {
            using var id = ImRaii.PushId(member.Uid);
            var pair = current.GetValueOrDefault(member.Uid);
            if (pair != null && !ReferenceEquals(pair.Pair, member.Pair)) pair = null;
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            if (pair != null) ImGui.TextDisabled(PairState(pair));
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(ShellSingleLine(pair?.CharacterName ?? member.Label));
            Tooltip($"{member.Label}\nNative UID: {member.Uid}");
            if (pair == null)
            {
                ImGui.TableSetColumnIndex(2);
                ImGui.TextDisabled("Unavailable");
                Tooltip("Waiting for a current pair from this provider. No action will target a stale roster member.");
                continue;
            }
            using var disabled = ImRaii.Disabled(syncshells.Pending(row.Key) != null);
            ImGui.TableSetColumnIndex(2);
            DrawPairPause(pair);
            ImGui.TableSetColumnIndex(3);
            DrawPairMedia(pair);
            ImGui.TableSetColumnIndex(4);
            DrawPairProfile(pair);
            ImGui.TableSetColumnIndex(5);
            DrawPairRecovery(pair);
        }
    }

    private void DrawShellControl(SyncshellRow row, SyncshellControl control, string label, SyncshellControl? pending, TimeSpan now,
        Vector2 size = default)
    {
        var setting = row[control];
        using var id = ImRaii.PushId(control.ToString());
        var media = control is SyncshellControl.Sound or SyncshellControl.Vfx or SyncshellControl.Animations;
        var pause = control == SyncshellControl.Pause;
        var unavailable = setting.Availability != SyncshellAvailability.Available;
        var anotherProfileOpening = control == SyncshellControl.Profile && syncshells.IsProfilePending(row.Key.Provider);
        var busy = pending == control;
        if (busy) label = media ? label + "…" : control == SyncshellControl.Profile ? "Opening…" : "…";
        else if (unavailable && !media)
            label = setting.Availability == SyncshellAvailability.Unsupported ? "N/A" : "Unavailable";

        using (ImRaii.Disabled(pending != null || anotherProfileOpening || unavailable))
        {
            using var colors = ImRaii.PushColor(ImGuiCol.Text, Vector4.One, media && setting.Value.HasValue);
            if (media && setting.Value is { } on)
            {
                var color = on ? new Vector4(0.1f, 0.4f, 0.18f, 1) : new Vector4(0.42f, 0.28f, 0.06f, 1);
                colors.Push(ImGuiCol.Button, color)
                    .Push(ImGuiCol.ButtonHovered, color + new Vector4(0.08f, 0.08f, 0.08f, 0))
                    .Push(ImGuiCol.ButtonActive, color + new Vector4(0.04f, 0.04f, 0.04f, 0));
            }
            var clicked = pause && !busy && !unavailable
                ? ImGuiComponents.IconButton("action", setting.Value == true ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause,
                    new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()))
                : ImGui.Button($"{label}###action", size);
            if (clicked)
            {
                if (pause && setting.Value == false)
                {
                    shellPauseConfirmation = row;
                    openShellPauseConfirmation = true;
                }
                else syncshells.Act(row, control, now);
            }
        }
        var tip = busy ? syncshells.PendingAfterFailure(row.Key) ? "Request still running after failure; wait before trying again."
                : "Waiting for the provider to confirm this action."
            : unavailable ? $"{setting.Availability}: {setting.Reason}"
            : pending != null ? "Waiting for the provider to confirm another action on this shell."
            : anotherProfileOpening ? "Wait for the provider to open the other shell profile."
            : control == SyncshellControl.Profile ? "Open this shell's own profile in its sync plugin. That plugin controls access."
            : media ? $"{(setting.Value == true ? "Disable" : "Enable")} your {Syncshells.Label(control)} preference for this shell in {row.Key.Provider.DisplayName()}."
            : $"{(setting.Value == true ? "Resume" : "Pause")} this shell in {row.Key.Provider.DisplayName()}.";
        Tooltip(pause ? $"{(setting.Value switch { true => "Paused", false => "Not paused", null => "Pause state unknown" })}\n{tip}" : tip);
    }

    private void DrawShellPauseConfirmation()
    {
        const string popup = "Pause this syncshell?##shellPauseConfirmation";
        if (openShellPauseConfirmation) { ImGui.OpenPopup(popup); openShellPauseConfirmation = false; }
        var open = true;
        if (!ImGui.BeginPopupModal(popup, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!open) shellPauseConfirmation = null;
            return;
        }
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 420);
        if (shellPauseConfirmation is { } row)
        {
            ImGui.TextUnformatted(ShellSingleLine(row.Name));
            ImGui.TextDisabled($"{ShellServiceName(row.Key.Provider)} · {row.Key.Gid}");
            ImGui.TextWrapped("Pause your sync with members of this shell, including members who are offline or not nearby? This changes your native shell setting, not other members' settings. Provider rules and individual overrides still apply.");
            if (row.Key.Provider == SyncProvider.PlayerSync)
                ImGui.TextWrapped("PlayerSync also replaces saved pause reasons for non-sticky members with its shell-pause reason. The manager preserves its own restoration tracking. Resuming the shell does not erase individual pauses.");
            if (ImGui.Button("Pause shell"))
            {
                syncshells.Act(row, SyncshellControl.Pause, shellClock.Elapsed);
                shellPauseConfirmation = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
        }
        if (ImGui.Button("Cancel")) { shellPauseConfirmation = null; ImGui.CloseCurrentPopup(); }
        ImGui.PopTextWrapPos();
        ImGui.EndPopup();
    }

    private static string ShellServiceName(SyncProvider provider) => provider == SyncProvider.Lightless ? "Lightless" : provider.DisplayName();
    private static string ShellSingleLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');
    private static float ShellButtonWidth(string label) => ImGui.CalcTextSize(label).X + 2 * ImGui.GetStyle().FramePadding.X;
    private static float ShellMediaWidth(string label) => Math.Max(ShellButtonWidth($"{label}: Off…"), ShellButtonWidth($"{label}: On…"));

    private static void ShellSameLineIfFits(string label)
    {
        var rightEdge = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + ShellButtonWidth(label) <= rightEdge)
            ImGui.SameLine();
    }

    private static void ShellWrappedText(string text)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }
}
