using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace XivSyncManager.Windows;

public sealed class SettingsWindow : Window
{
    private readonly Plugin plugin;
    private static readonly Vector4 WarningRed = new(1f, 0.3f, 0.3f, 1f);

    public SettingsWindow(Plugin plugin) : base("XIV Sync Manager Settings###XivSyncManagerSettings")
    {
        this.plugin = plugin;
        Size = new Vector2(440, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new()
        {
            MinimumSize = new Vector2(360, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        lock (plugin.Coordinator.SyncRoot)
        {
            var keepFallback = plugin.Configuration.KeepFallbackUntilReentry;
            if (ImGui.Checkbox("Keep fallback sync until player returns", ref keepFallback))
                plugin.Coordinator.SetKeepFallbackUntilReentry(keepFallback);
            ImGui.TextWrapped("When a service reconnects, keep using the working fallback until the character leaves Nearby and returns. Turning this off returns to the preferred sync as soon as it is available.");
            ImGui.Separator();
            ImGui.Spacing();
            var showInfo = plugin.Configuration.ShowIntegrationInfo;
            if (ImGui.Checkbox("Show integration info boxes", ref showInfo))
            {
                plugin.Configuration.ShowIntegrationInfo = showInfo;
                plugin.Configuration.Save();
            }
            ImGui.TextWrapped("Show clickable plugin requirement and optional feature badges beside the sync names.");
            var hideReady = plugin.Configuration.HideReadyIntegrationInfo;
            using (ImRaii.Disabled(!showInfo))
                if (ImGui.Checkbox("Hide green integration info boxes", ref hideReady))
                {
                    plugin.Configuration.HideReadyIntegrationInfo = hideReady;
                    plugin.Configuration.Save();
                }
            ImGui.TextWrapped("Hide the badge when all checked integrations are ready. Enabled by default.");
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextWrapped("Clear indexed characters and saved pause choices. Your sync priority and preferred sync choices are kept.");
            ImGui.Spacing();
            if (ImGui.Button("Clear cache")) plugin.Coordinator.ClearCache();
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, WarningRed);
            ImGui.TextWrapped("ALPHA TEST: This plugin is still being tested. Sync behavior may be unreliable.");
            ImGui.PopStyleColor();
            ImGui.Spacing();
            ImGui.TextWrapped("Manager pauses will be released. Disconnected syncs are released when they reconnect. Nearby duplicates can be indexed again on the next refresh.");
        }
    }
}
