using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace XivSyncManager.Windows;

public sealed class SettingsWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly IFontHandle boldFont;
    private static readonly Vector4 WarningRed = new(1f, 0.3f, 0.3f, 1f);
    private static readonly IndexRetention[] RetentionOptions =
        [IndexRetention.None, IndexRetention.Days7, IndexRetention.Days30, IndexRetention.Days60,
            IndexRetention.Days90, IndexRetention.Days180, IndexRetention.OneYear];

    public SettingsWindow(Plugin plugin) : base("XIV Sync Manager Settings###XivSyncManagerSettings")
    {
        this.plugin = plugin;
        boldFont = Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(build =>
            build.OnPreBuild(toolkit => toolkit.AddGameGlyphs(
                new GameFontStyle(GameFontFamily.Axis, Plugin.PluginInterface.UiBuilder.FontDefaultSizePx) { Weight = 1 },
                [0x20, 0x7e, 0], default)));
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
            using var child = ImRaii.Child("settingsContent", Vector2.Zero, false);
            if (!child.Success) return;
            var keepFallback = plugin.Configuration.KeepFallbackUntilReentry;
            if (BoldCheckbox("Keep fallback sync until player returns", ref keepFallback))
                plugin.Coordinator.SetKeepFallbackUntilReentry(keepFallback);
            ImGui.TextWrapped("When a service reconnects, keep using the working fallback until the character leaves Nearby and returns. Turning this off returns to the preferred sync as soon as it is available.");
            ImGui.Separator();
            ImGui.Spacing();
            var showInfo = plugin.Configuration.ShowIntegrationInfo;
            if (BoldCheckbox("Show integration info boxes", ref showInfo))
            {
                plugin.Configuration.ShowIntegrationInfo = showInfo;
                plugin.Configuration.Save();
            }
            ImGui.TextWrapped("Show clickable plugin requirement and optional feature badges beside the sync names.");
            var hideReady = plugin.Configuration.HideReadyIntegrationInfo;
            using (ImRaii.Disabled(!showInfo))
                if (BoldCheckbox("Hide green integration info boxes", ref hideReady))
                {
                    plugin.Configuration.HideReadyIntegrationInfo = hideReady;
                    plugin.Configuration.Save();
                }
            ImGui.TextWrapped("Hide the badge when all checked integrations are ready. Enabled by default.");
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextUnformatted("Delete stale indexed characters after");
            var retention = Array.IndexOf(RetentionOptions, plugin.Configuration.StaleIndexRetention);
            if (retention < 0) retention = Array.IndexOf(RetentionOptions, IndexRetention.Days30);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##StaleIndexRetention", ref retention, "None (Does not delete index data)\0" +
                    "7 days\0" + "30 days\0" + "60 days\0" + "90 days\0" + "180 days\0" + "1 year\0"))
                plugin.Coordinator.SetStaleIndexRetention(RetentionOptions[retention]);
            ImGui.TextWrapped("Counts from the last time any sync reported the character Online. Nearby presence alone does not reset the timer. Entries without online history count from when tracking started.");
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

    public void Dispose() => boldFont.Dispose();

    private bool BoldCheckbox(string label, ref bool value)
    {
        using var font = boldFont.Push();
        return ImGui.Checkbox(label, ref value);
    }
}
