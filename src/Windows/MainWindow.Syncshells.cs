using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace XivSyncManager.Windows;

public sealed partial class MainWindow
{
    private readonly Syncshells syncshells = new(() => Plugin.PluginInterface.InstalledPlugins);
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

        var pauseWidth = ShellButtonWidth("Unsupported");
        var profileWidth = Math.Max(ShellButtonWidth("Unsupported"), ImGui.CalcTextSize("Opening…").X);
        var mediaWidth = ShellButtonWidth("Sound: Off") + ShellButtonWidth("VFX: Off")
            + ShellButtonWidth("Animations: Off") + 2 * ImGui.GetStyle().ItemSpacing.X;
        var minimumWidth = ShellButtonWidth("Lightless Sync") + pauseWidth + mediaWidth + profileWidth
            + 8 * ImGui.GetStyle().CellPadding.X;
        var horizontalScroll = minimumWidth > ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ScrollbarSize;
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;
        if (horizontalScroll) flags |= ImGuiTableFlags.ScrollX;
        if (!ImGui.BeginTable("JoinedSyncshells", 4, flags, new Vector2(0, -1), horizontalScroll ? minimumWidth : 0)) return;
        try
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Joined shell", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Pause state", ImGuiTableColumnFlags.WidthFixed, pauseWidth);
            ImGui.TableSetupColumn("Media permissions", ImGuiTableColumnFlags.WidthFixed, mediaWidth);
            ImGui.TableSetupColumn("Profile", ImGuiTableColumnFlags.WidthFixed, profileWidth);
            ImGui.TableHeadersRow();
            foreach (var row in rows) DrawShellRow(row, now);
        }
        finally { ImGui.EndTable(); }
    }

    private void RefreshShells(TimeSpan now)
    {
        syncshells.Refresh(now);
        nextShellRefresh = now + TimeSpan.FromSeconds(1);
    }

    private void DrawShellRow(SyncshellRow row, TimeSpan now)
    {
        using var providerId = ImRaii.PushId(row.Key.Provider.ToString());
        using var shellId = ImRaii.PushId(row.Key.Gid);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ShellWrappedText(row.Name);
        Tooltip($"{row.Key.Provider.DisplayName()}\nShell ID: {row.Key.Gid}");
        var style = ImGui.GetStyle();
        using (ImRaii.PushColor(ImGuiCol.FrameBg, style.Colors[(int)ImGuiCol.Header]))
        {
            // Themed, non-interactive badge; the text remains readable without provider colors.
            var label = row.Key.Provider.DisplayName();
            var pos = ImGui.GetCursorScreenPos();
            var size = ImGui.CalcTextSize(label);
            var padding = new Vector2(style.FramePadding.X, 1);
            ImGui.GetWindowDrawList().AddRectFilled(pos, pos + size + 2 * padding,
                ImGui.GetColorU32(ImGuiCol.FrameBg), style.FrameRounding);
            ImGui.GetWindowDrawList().AddText(pos + padding, ImGui.GetColorU32(ImGuiCol.Text), label);
            ImGui.Dummy(size + 2 * padding);
        }

        var pending = syncshells.Pending(row.Key);
        var pause = row[SyncshellControl.Pause];
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(pause.Value switch { true => "Paused", false => "Not paused", null => "Unknown" });
        DrawShellControl(row, SyncshellControl.Pause, pause.Value == true ? "Resume" : "Pause", pending, now);

        ImGui.TableNextColumn();
        DrawMedia(SyncshellControl.Sound);
        ImGui.SameLine();
        DrawMedia(SyncshellControl.Vfx);
        ImGui.SameLine();
        DrawMedia(SyncshellControl.Animations);
        if (pending is { } operation && operation != SyncshellControl.Profile)
            ImGui.TextDisabled(syncshells.PendingAfterFailure(row.Key) ? "Request still running" : $"Confirming {Syncshells.Label(operation).ToLowerInvariant()}…");

        ImGui.TableNextColumn();
        DrawShellControl(row, SyncshellControl.Profile, "Open", pending, now);
        if (pending == SyncshellControl.Profile) ImGui.TextDisabled("Opening…");

        void DrawMedia(SyncshellControl control)
        {
            var state = row[control].Value switch { true => "On", false => "Off", null => "—" };
            DrawShellControl(row, control, $"{Syncshells.Label(control)}: {state}", pending, now);
        }
    }

    private void DrawShellControl(SyncshellRow row, SyncshellControl control, string label, SyncshellControl? pending, TimeSpan now)
    {
        var setting = row[control];
        using var id = ImRaii.PushId(control.ToString());
        var media = control is SyncshellControl.Sound or SyncshellControl.Vfx or SyncshellControl.Animations;
        var unavailable = setting.Availability != SyncshellAvailability.Available;
        var anotherProfileOpening = control == SyncshellControl.Profile && syncshells.IsProfilePending(row.Key.Provider);
        if (unavailable && !media)
            label = setting.Availability == SyncshellAvailability.Unsupported ? "Unsupported" : "Unavailable";
        using (ImRaii.Disabled(pending != null || anotherProfileOpening || unavailable))
            if (ImGui.Button($"{label}###action")) syncshells.Act(row, control, now);
        Tooltip(unavailable ? $"{setting.Availability}: {setting.Reason}"
            : pending != null ? "Waiting for the provider to confirm the current shell action."
            : anotherProfileOpening ? "Wait for the provider to open the other shell profile."
            : control == SyncshellControl.Profile ? "Open this shell's own profile in its sync plugin. That plugin controls access."
            : media ? $"{(setting.Value == true ? "Disable" : "Enable")} your {Syncshells.Label(control)} preference for this shell in {row.Key.Provider.DisplayName()}."
            : $"{label} this shell in {row.Key.Provider.DisplayName()}.");
    }

    private static float ShellButtonWidth(string label) => ImGui.CalcTextSize(label).X + 2 * ImGui.GetStyle().FramePadding.X;

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
