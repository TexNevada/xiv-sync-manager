using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace XivSyncManager.Windows;

public sealed partial class MainWindow : Window
{
    private readonly Plugin plugin;
    private string search = string.Empty;
    private static readonly Vector4 ConnectedGreen = new(0.5f, 0.85f, 0.6f, 1);

    public MainWindow(Plugin plugin) : base("XIV Sync Manager###XivSyncManagerMain")
    {
        this.plugin = plugin;
        TitleBarButtons =
        [
            new TitleBarButton
            {
                Icon = FontAwesomeIcon.Cog,
                Priority = 0,
                Click = _ => plugin.ToggleSettingsUi(),
                ShowTooltip = () => ImGui.SetTooltip("Settings"),
            },
        ];
        Size = new Vector2(880, 580);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new() { MinimumSize = new Vector2(700, 400), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
    }

    public override void Draw()
    {
        lock (plugin.Coordinator.SyncRoot) DrawContents();
    }

    private void DrawContents()
    {
        DrawManagementButton();
        Tooltip("Click to turn automatic duplicate management on or off. Green means on; red means off.\nKeeps one eligible sync active per character. If it disconnects for more than five seconds, an available fallback takes over. Settings controls whether to keep that fallback until the character leaves Nearby and returns.\n\nLightless and PlayerSync pauses can affect both directions. Agree on a preferred sync with the other player.");
        ImGui.Spacing();
        DrawPriority();
        ImGui.Spacing();
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("Theme", plugin.Configuration.Theme.ToString()))
        {
            foreach (var theme in Enum.GetValues<SyncTheme>())
            {
                var selected = plugin.Configuration.Theme == theme;
                if (ImGui.Selectable(theme.ToString(), selected))
                {
                    plugin.Configuration.Theme = theme;
                    plugin.Configuration.Save();
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.Separator();
        if (!ImGui.BeginTabBar("syncTabs")) return;
        if (ImGui.BeginTabItem("Sync List"))
        {
            DrawSyncList();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Statistics"))
        {
            DrawStatistics();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawSyncList()
    {
        var coordinator = plugin.Coordinator;
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search character or world", ref search, 128);

        var characters = coordinator.Characters;
        var filtered = characters.Where(c => string.IsNullOrWhiteSpace(search)
            || c.DisplayName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        ImGui.TextDisabled($"{characters.Count} cached duplicate character(s)");
        if (coordinator.UnresolvedPauses > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"· {coordinator.UnresolvedPauses} saved pause(s) awaiting reconnection");
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Media for all pairs")) ImGui.OpenPopup("globalMedia");
        Tooltip("Pause or resume animation, sound, and VFX sync for all pairs on connected services, including pairs currently offline. Changes are saved by each sync service. Disconnected services and future pairs are excluded.");
        DrawMediaPopup("globalMedia", "All pairs on connected services", coordinator.Pairs, Enum.GetValues<SyncProvider>());

        using var child = ImRaii.Child("DuplicateCharacters", Vector2.Zero, false);
        if (!child.Success) return;
        DrawList("Nearby", filtered.Where(c => c.Nearby).ToArray(), defaultOpen: true);
        DrawList("Online", filtered.Where(c => c.Online && !c.Nearby).ToArray(), defaultOpen: true);
        DrawList("Offline", filtered.Where(c => !c.Online && !c.Nearby).ToArray(), defaultOpen: false);
        if (characters.Count == 0)
            ImGui.TextWrapped("Duplicates appear after the same character has been identified in view through two or more syncs. Once identified, they stay in this list when offline.");
        else if (filtered.Length == 0)
            ImGui.TextDisabled("No characters match your search.");
    }

    private void DrawStatistics()
    {
        using var child = ImRaii.Child("statistics", Vector2.Zero, false);
        if (!child.Success) return;
        ImGui.TextUnformatted("Synced players' VRAM");
        ImGui.TextWrapped("Estimates for all visible paired players.");
        ImGui.Spacing();
        var statuses = plugin.Coordinator.Providers.ToDictionary(p => p.Provider);
        var reports = plugin.Configuration.Priority.Select(p => statuses[p]).ToArray();
        if (ImGui.BeginTable("vramUsage", 3, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Sync plugin", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("Estimated VRAM", ImGuiTableColumnFlags.WidthFixed, 160 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch, 2);
            ImGui.TableHeadersRow();
            foreach (var status in reports)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(status.Provider.DisplayName());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(status.Vram.Bytes is { } bytes
                    ? FormatVram(bytes) + (status.Vram.Partial ? " (partial)" : string.Empty) : "Unavailable");
                ImGui.TableNextColumn();
                ImGui.TextWrapped(status.Vram.Description);
            }

            var available = reports.Where(p => p.Vram.Bytes != null).ToArray();
            var partial = reports.Any(p => p.Vram.Bytes == null || p.Vram.Partial);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Total (sum)");
            ImGui.TableNextColumn();
            // Decimal avoids overflow when combining counters from independent plugins.
            ImGui.TextUnformatted(available.Length > 0
                ? FormatVram(available.Sum(p => (decimal)p.Vram.Bytes!.Value)) + (partial ? " (partial)" : string.Empty)
                : "Unavailable");
            ImGui.TableNextColumn();
            ImGui.TextWrapped(available.Length == 0 ? "No current plugin estimates are available."
                : partial ? $"Includes available estimates from {available.Length} of {reports.Length} plugins. Some data is unavailable or still being calculated."
                : "Sum of all three plugin estimates.");
            ImGui.EndTable();
        }
        ImGui.Spacing();
        ImGui.TextDisabled("Updates automatically as each plugin updates its estimates.");
        ImGui.Spacing();
        ImGui.Separator();
        DrawRecentAudio();
    }

    private static string FormatVram(decimal bytes) => bytes >= 1024m * 1024 * 1024
        ? $"{bytes / (1024m * 1024 * 1024):N2} GiB" : $"{bytes / (1024m * 1024):N1} MiB";

    private void DrawManagementButton()
    {
        var automatic = plugin.Configuration.AutomaticManagement;
        var colour = automatic ? new Vector4(0.1f, 0.4f, 0.18f, 1f) : new Vector4(0.6f, 0.12f, 0.16f, 1f);
        var hovered = colour + new Vector4(0.08f, 0.08f, 0.08f, 0);
        var active = colour + new Vector4(0.04f, 0.04f, 0.04f, 0);
        using var textColour = ImRaii.PushColor(ImGuiCol.Text, Vector4.One);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.SyncAlt,
                automatic ? "Sync management: On" : "Sync management: Off", colour, active, hovered))
            plugin.Coordinator.SetAutomaticManagement(!automatic);
    }

    private void DrawPriority()
    {
        var coordinator = plugin.Coordinator;
        var statuses = coordinator.Providers.ToDictionary(p => p.Provider);
        ImGui.TextDisabled("Sync priority · highest first");
        Tooltip("Your priority order is saved automatically across logouts and plugin reloads.");
        // Snapshot the order so moving one row does not change the later rows during this frame.
        var order = plugin.Configuration.Priority.ToArray();
        for (var index = 0; index < order.Length; index++)
        {
            var provider = order[index];
            using var id = ImRaii.PushId(provider.ToString());
            using (ImRaii.Disabled(index == 0))
                if (ImGui.ArrowButton("up", ImGuiDir.Up)) coordinator.MovePriority(plugin.Configuration.Priority.IndexOf(provider), -1);
            Tooltip("Move this sync higher in priority.");
            ImGui.SameLine();
            using (ImRaii.Disabled(index == order.Length - 1))
                if (ImGui.ArrowButton("down", ImGuiDir.Down)) coordinator.MovePriority(plugin.Configuration.Priority.IndexOf(provider), 1);
            Tooltip("Move this sync lower in priority.");
            ImGui.SameLine();
            var status = statuses[provider];
            DrawConnectionIndicator(status);
            ImGui.SameLine();
            var green = plugin.Configuration.Theme == SyncTheme.PaperAndInk ? new Vector4(0.12f, 0.45f, 0.22f, 1) : ConnectedGreen;
            var colour = status.Connected ? green : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
            ImGui.TextColored(colour, provider.DisplayName());
            DrawIntegrationBadge(status);
        }
    }

    private void DrawIntegrationBadge(ProviderStatus status)
    {
        var report = status.Integrations;
        var ready = report.RequiredProblems == 0 && report.OptionalProblems == 0 && !report.Incomplete;
        if (!plugin.Configuration.ShowIntegrationInfo || (plugin.Configuration.HideReadyIntegrationInfo && ready)) return;
        ImGui.SameLine();
        var requiredUnknown = report.Plugins.Any(p => p.Required && p.State == IntegrationState.Unknown);
        var label = report.RequiredProblems > 0 ? $"Required: {report.RequiredProblems} need attention"
            : requiredUnknown ? "Required: unable to check"
            : report.OptionalProblems > 0 ? $"Optional extras: {report.OptionalProblems} need attention"
            : report.Incomplete ? "Plugins: unable to check" : "Integrations";
        var colour = report.RequiredProblems > 0 ? new Vector4(0.6f, 0.12f, 0.16f, 1)
            : report.OptionalProblems > 0 && !requiredUnknown ? new Vector4(0.42f, 0.28f, 0.06f, 1)
            : report.Incomplete ? new Vector4(0.25f, 0.28f, 0.33f, 1) : new Vector4(0.1f, 0.4f, 0.18f, 1);
        var icon = report.RequiredProblems > 0 ? FontAwesomeIcon.ExclamationTriangle
            : report.OptionalProblems > 0 && !requiredUnknown ? FontAwesomeIcon.InfoCircle
            : report.Incomplete ? FontAwesomeIcon.QuestionCircle : FontAwesomeIcon.CheckCircle;
        using (ImRaii.PushColor(ImGuiCol.Text, Vector4.One))
            if (ImGuiComponents.IconButtonWithText(icon, label, colour,
                    colour + new Vector4(0.04f, 0.04f, 0.04f, 0), colour + new Vector4(0.08f, 0.08f, 0.08f, 0)))
                ImGui.OpenPopup("integrations");

        var missing = report.Plugins.Where(p => p.NeedsAttention).OrderByDescending(p => p.Required)
            .Select(p => $"{p.Name}: {p.StatusLabel}");
        var summary = report.RequiredProblems > 0 ? "Required plugins need attention before appearance sync can work properly."
            : requiredUnknown ? "Required plugin checks are incomplete."
            : report.OptionalProblems > 0 ? "These extras are optional. Required plugins are ready."
            : report.Incomplete ? report.Notice ?? "Some plugin checks are incomplete."
            : "All checked integrations are ready.";
        Tooltip($"{summary}\n{string.Join("\n", missing)}\nClick to see plugins, features, and how to fix them.");

        if (!ImGui.BeginPopup("integrations")) return;
        var width = 440 * ImGuiHelpers.GlobalScale;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextUnformatted($"{status.Provider.DisplayName()} · Integrations");
        ImGui.TextWrapped(summary);
        if (report.Notice != null && report.Notice != summary) ImGui.TextWrapped(report.Notice);
        ImGui.Separator();
        if (report.Plugins.Count > 0)
        {
            // Keep long optional lists inside the screen and retain the user's scroll position.
            using var child = ImRaii.Child("integrationList", new Vector2(width, 320 * ImGuiHelpers.GlobalScale), false);
            if (child.Success)
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - 25 * ImGuiHelpers.GlobalScale);
                DrawIntegrationGroup("Required · needed for appearance sync", report.Plugins.Where(p => p.Required));
                ImGui.Spacing();
                DrawIntegrationGroup("Optional · extra features", report.Plugins.Where(p => !p.Required));
                ImGui.PopTextWrapPos();
            }
        }
        ImGui.Separator();
        ImGui.TextWrapped("Project opens the plugin's project and documentation. Copy repo URL copies its installation feed: add it in /xlsettings > Experimental > Custom Plugin Repositories and save, then use Find to search All Plugins. Checks update automatically; this sync may take a few seconds to notice changes.");
        if (ImGui.Button("Open plugin installer"))
            Plugin.PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins);
        ImGui.PopTextWrapPos();
        ImGui.EndPopup();
    }

    private void DrawIntegrationGroup(string heading, IEnumerable<PluginIntegration> integrations)
    {
        ImGui.TextUnformatted(heading);
        ImGui.Separator();
        foreach (var integration in integrations.OrderByDescending(p => p.NeedsAttention)
                     .ThenByDescending(p => p.State == IntegrationState.Unknown))
        {
            using var id = ImRaii.PushId(integration.Name);
            var light = plugin.Configuration.Theme == SyncTheme.PaperAndInk;
            var colour = integration.State == IntegrationState.Ready ? light ? new Vector4(0.12f, 0.45f, 0.22f, 1) : ConnectedGreen
                : integration.State == IntegrationState.Unknown ? ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]
                : integration.Required ? light ? new Vector4(0.7f, 0.1f, 0.1f, 1) : new Vector4(1, 0.4f, 0.4f, 1)
                : light ? new Vector4(0.5f, 0.32f, 0.02f, 1) : new Vector4(1, 0.8f, 0.4f, 1);
            ImGui.TextUnformatted(integration.Name);
            ImGui.SameLine();
            ImGui.TextColored(colour, integration.StatusLabel);
            ImGui.TextWrapped(integration.Feature);
            if (integration.State != IntegrationState.Ready || integration.Name == "Moodles / Loci")
                ImGui.TextWrapped(integration.Explanation);
            foreach (var resource in integration.Resources)
            {
                using var resourceId = ImRaii.PushId(resource.Name);
                if (integration.Resources.Count > 1)
                {
                    ImGui.TextUnformatted(resource.Name);
                    ImGui.SameLine();
                }
                if (ImGui.SmallButton("Project"))
                    Util.OpenLink(resource.ProjectUrl);
                Tooltip($"Open {resource.Name}'s project and documentation in your browser.\n{resource.ProjectUrl}");
                if (resource.RepositoryUrl != null)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Copy repo URL"))
                        ImGui.SetClipboardText(resource.RepositoryUrl);
                    Tooltip($"Copy the custom repository URL for /xlsettings > Experimental > Custom Plugin Repositories. Add it and save before searching the installer.\n{resource.RepositoryUrl}");
                }
                if (integration.State != IntegrationState.Ready)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Find"))
                        Plugin.PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, resource.SearchTerm);
                    Tooltip($"Search All Plugins for {resource.SearchTerm}. This does not install anything automatically.");
                }
                if (resource.RepositoryUrl == null)
                    ImGui.TextWrapped("Available in Dalamud's official plugin repository.");
            }
            ImGui.Spacing();
        }
    }

    private void DrawConnectionIndicator(ProviderStatus status)
    {
        var coordinator = plugin.Coordinator;
        var progress = coordinator.ConnectionProgressFor(status.Provider);
        var connectionError = coordinator.ConnectionErrorFor(status.Provider);
        var colour = status.Connected ? new Vector4(0.1f, 0.4f, 0.18f, 1f) : new Vector4(0.6f, 0.12f, 0.16f, 1f);
        var hovered = colour + new Vector4(0.08f, 0.08f, 0.08f, 0);
        var active = colour + new Vector4(0.04f, 0.04f, 0.04f, 0);
        var size = new Vector2(ImGui.GetFrameHeight());
        bool clicked;
        using (ImRaii.PushColor(ImGuiCol.Text, Vector4.One))
            clicked = ImGuiComponents.IconButton("connection", FontAwesomeIcon.Wifi, colour, active, hovered, size);

        if (!status.Connected)
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var inset = size.X * 0.22f;
            var start = new Vector2(min.X + inset, max.Y - inset);
            var end = new Vector2(max.X - inset, min.Y + inset);
            var background = ImGui.IsItemActive() ? active : ImGui.IsItemHovered() ? hovered : colour;
            var drawList = ImGui.GetWindowDrawList();
            // Cut through the WiFi glyph before drawing the slash so the offline shape stays readable.
            drawList.AddLine(start, end, ImGui.ColorConvertFloat4ToU32(background), 5 * ImGuiHelpers.GlobalScale);
            drawList.AddLine(start, end, ImGui.ColorConvertFloat4ToU32(Vector4.One), 2 * ImGuiHelpers.GlobalScale);
        }

        var description = status.Connected ? "Online" : $"Offline · {status.Description}";
        var action = progress != null ? progress
            : status.Connected && status.CanDisconnect ? "Click to disconnect this plugin for all paired players."
            : !status.Connected && status.CanReconnect ? "Click to resume this sync plugin using its existing server and account setup.\nSaved character pauses and automatic duplicate rules still apply."
            : $"Connection controls unavailable: {(string.IsNullOrEmpty(status.ConnectionUnavailableReason) ? status.Description : status.ConnectionUnavailableReason)}\nClick for details.";
        var details = $"{description}\n{action}";
        if (connectionError != null) details += $"\nConnection error: {connectionError}";
        Tooltip($"{status.Provider.DisplayName()}: {details}");
        if (clicked)
        {
            if (progress == null && (status.Connected ? status.CanDisconnect : status.CanReconnect))
                coordinator.SetProviderConnected(status.Provider, !status.Connected);
            else ImGui.OpenPopup("connectionDetails");
        }
        if (!ImGui.BeginPopup("connectionDetails")) return;
        ImGui.TextUnformatted(status.Provider.DisplayName());
        ImGui.Separator();
        ImGui.PushTextWrapPos(300 * ImGuiHelpers.GlobalScale);
        ImGui.TextWrapped(details);
        ImGui.PopTextWrapPos();
        ImGui.EndPopup();
    }

    private void DrawList(string name, IReadOnlyList<DuplicateCharacter> characters, bool defaultOpen)
    {
        var expanded = plugin.Configuration.SectionExpanded.GetValueOrDefault(name, defaultOpen);
        // Configuration is authoritative across reloads; the header can still toggle on this frame.
        ImGui.SetNextItemOpen(expanded, ImGuiCond.Always);
        var open = ImGui.CollapsingHeader($"{name} ({characters.Count})###{name}");
        if (open != expanded)
        {
            plugin.Configuration.SectionExpanded[name] = open;
            plugin.Configuration.Save();
        }
        if (!open) return;
        if (characters.Count == 0)
        {
            ImGui.TextDisabled($"No {name.ToLowerInvariant()} duplicate characters.");
            return;
        }
        using var id = ImRaii.PushId(name);
        if (!ImGui.BeginTable("Characters", 5, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        var profileWidth = 3 * (ImGui.GetFrameHeight() + ImGui.CalcTextSize("W").X + ImGui.GetStyle().FramePadding.X)
            + 2 * ImGui.GetStyle().ItemSpacing.X;
        ImGui.TableSetupColumn("Profiles", ImGuiTableColumnFlags.WidthFixed, profileWidth);
        ImGui.TableSetupColumn("Preferred sync", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 1);
        var controlWidth = MathF.Max(80 * ImGuiHelpers.GlobalScale,
            MathF.Max(ImGui.CalcTextSize("Pause All").X, ImGui.CalcTextSize("Resume").X)
            + 2 * ImGui.GetStyle().FramePadding.X)
            + ImGui.CalcTextSize("Media…").X + 2 * ImGui.GetStyle().FramePadding.X + ImGui.GetStyle().ItemSpacing.X;
        ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthFixed, controlWidth);
        ImGui.TableHeadersRow();
        foreach (var character in characters) DrawCharacter(character);
        ImGui.EndTable();
    }

    private void DrawCharacter(DuplicateCharacter character)
    {
        var coordinator = plugin.Coordinator;
        using var id = ImRaii.PushId(character.Identity);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(character.DisplayName);
        Tooltip($"Known on: {string.Join(", ", character.Providers.Select(p => p.DisplayName()))}");
        foreach (var pair in character.Pairs.Where(p => p.Audio.Recent))
        {
            ImGui.TextDisabled(AudioLabel(pair));
            Tooltip(pair.Audio.Description);
        }
        ImGui.TableNextColumn();
        DrawProfiles(character);
        ImGui.TableNextColumn();
        var hasPreference = plugin.Configuration.PreferredProviders.TryGetValue(character.Identity, out var preferred);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##preferred", hasPreference ? preferred.DisplayName() : "Use priority"))
        {
            if (ImGui.Selectable("Use priority", !hasPreference)) coordinator.SetPreferred(character.Identity, null);
            // Preferences can be saved before a character has been identified on every sync.
            // Route availability is checked by the coordinator when applying the choice.
            foreach (var provider in plugin.Configuration.Priority)
            {
                var selected = hasPreference && preferred == provider;
                if (ImGui.Selectable(provider.DisplayName(), selected))
                    coordinator.SetPreferred(character.Identity, provider);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        Tooltip("Used when automatic management is enabled. Your preferred sync is used when available for this character; otherwise, priority is used. This choice is saved across logouts and plugin reloads, including while offline.");
        ImGui.TableNextColumn();
        var paused = coordinator.IsCharacterPaused(character);
        var errors = character.Pairs.Select(p => (Pair: p, Error: coordinator.ErrorFor(p)))
            .Where(entry => entry.Error != null).ToArray();
        var externalChanges = character.Pairs.Any(p => plugin.Configuration.AutomaticExceptions.Contains(p.Key));
        ImGui.TextUnformatted(errors.Length > 0 || externalChanges ? "Needs attention" : StateFor(character, paused));
        if (errors.Length > 0)
            Tooltip(string.Join("\n", errors.Select(entry => $"{entry.Pair.Provider.DisplayName()}: {entry.Error}")));
        else if (externalChanges)
            Tooltip("A sync was changed in its original plugin. Changes to that route are suspended. Retry enables management again; pauses made in the original sync plugin are preserved.");
        else if (!character.Online)
            Tooltip("No sync currently reports this character online and they are not in view. Pause and Resume choices are saved for reconnection.");
        else if (coordinator.IsKeepingFallback(character.Identity))
            Tooltip("Keeping the current fallback sync until this character leaves Nearby and returns. Change the preferred sync or priority to switch immediately.");
        if (errors.Length > 0 || externalChanges)
        {
            if (ImGui.SmallButton("Retry"))
                foreach (var pair in character.Pairs) coordinator.UseAutomaticRules(pair);
            Tooltip("Retry this character's saved choice and enable automatic rules again. Existing external pauses are preserved.");
        }
        ImGui.TableNextColumn();
        if (ImGui.Button("Media…")) ImGui.OpenPopup("characterMedia");
        Tooltip("Pause or resume animations, sounds, and VFX for this character across identified syncs or for one plugin.");
        ImGui.SameLine();
        if (ImGui.Button(paused ? "Resume" : "Pause All", new Vector2(-1, 0))) coordinator.SetCharacterPaused(character, !paused);
        Tooltip(paused
            ? "Release this character's manual pause. Automatic management keeps the preferred available sync active. Pauses made in the original sync plugins are preserved."
            : "Pause this character on every identified sync. This choice is saved even while offline.");
        DrawMediaPopup("characterMedia", character.DisplayName, character.Pairs, character.Providers);
    }

    private void DrawProfiles(DuplicateCharacter character)
    {
        var coordinator = plugin.Coordinator;
        for (var index = 0; index < character.Providers.Count; index++)
        {
            var provider = character.Providers[index];
            using var id = ImRaii.PushId(provider.ToString());
            if (index > 0) ImGui.SameLine();
            var pair = character.Pairs.Where(p => p.Provider == provider)
                .OrderByDescending(p => p.Profile.CanOpen).FirstOrDefault();
            // An external pause can remove the identifier. Show the last known account's blocked
            // profile state for display only; do not assign its identity or enable character actions.
            var historicalPair = pair == null ? coordinator.Pairs.FirstOrDefault(p => p.Provider == provider
                && p.CharacterIdentity == null && p.Profile.Availability == ProfileAvailability.Paused
                && plugin.Configuration.DuplicateCharacters[character.Identity].Routes.ContainsKey(p.Key)) : null;
            var status = pair?.Profile ?? new(ProfileAvailability.Unknown,
                "This sync has no currently identified pair for this character. Reconnect it or wait until the character is identified again.");
            if (historicalPair != null)
                status = historicalPair.Profile with { Description = historicalPair.Profile.Description
                    + " This is the last known pair account; its current character identity cannot be verified while paused." };
            var present = status.Availability is ProfileAvailability.Available or ProfileAvailability.Cached;
            var colour = status.Availability == ProfileAvailability.Paused ? new Vector4(0.7f, 0.35f, 0.04f, 1)
                : present ? new Vector4(0.1f, 0.4f, 0.18f, 1)
                : new Vector4(0.25f, 0.28f, 0.33f, 1);
            var icon = status.Availability == ProfileAvailability.Paused ? FontAwesomeIcon.PauseCircle
                : present ? FontAwesomeIcon.AddressCard
                : status.Availability == ProfileAvailability.NoProfile ? FontAwesomeIcon.Ban
                : status.Availability == ProfileAvailability.Unavailable ? FontAwesomeIcon.ExclamationCircle
                : FontAwesomeIcon.QuestionCircle;
            var label = provider switch { SyncProvider.Lightless => "L", SyncProvider.Snowcloak => "S", _ => "P" };
            bool clicked;
            using (ImRaii.PushStyle(ImGuiStyleVar.DisabledAlpha,
                       status.Availability == ProfileAvailability.Paused ? 1f : ImGui.GetStyle().DisabledAlpha))
            using (ImRaii.Disabled(!status.CanOpen))
            using (ImRaii.PushColor(ImGuiCol.Text, Vector4.One))
                clicked = ImGuiComponents.IconButtonWithText(icon, label, colour,
                    colour + new Vector4(0.04f, 0.04f, 0.04f, 0), colour + new Vector4(0.08f, 0.08f, 0.08f, 0));
            var error = pair == null ? null : coordinator.ProfileErrorFor(pair);
            var action = status.CanOpen ? "Click to open this sync's own profile viewer. It may load the profile and images."
                : string.IsNullOrEmpty(status.OpenUnavailableReason) ? "No current pair is available to open." : status.OpenUnavailableReason;
            Tooltip($"{provider.DisplayName()} · {status.Label}\n{status.Description}\n{action}"
                + (error == null ? string.Empty : $"\nCould not open: {error}"));
            if (clicked && pair != null && coordinator.OpenProfile(pair) != null)
                ImGui.OpenPopup("profileError");
            if (!ImGui.BeginPopup("profileError")) continue;
            ImGui.TextUnformatted($"{provider.DisplayName()} · Profile");
            ImGui.PushTextWrapPos(320 * ImGuiHelpers.GlobalScale);
            ImGui.TextWrapped(pair == null ? "This pair is no longer available." : coordinator.ProfileErrorFor(pair) ?? "Try opening the profile again.");
            ImGui.PopTextWrapPos();
            ImGui.EndPopup();
        }
    }

    private string StateFor(DuplicateCharacter character, bool paused)
    {
        var coordinator = plugin.Coordinator;
        if (character.Pairs.Any(coordinator.IsPending)) return "Updating…";
        if (!character.Online) return paused ? "Pause saved" : "Offline";
        if (paused)
            return character.Pairs.Count > 0 && character.Pairs.All(p => p.OwnPaused || p.ManagerFullyHeld || p.ExternalHold)
                ? "Paused" : "Pause queued";
        if (coordinator.IsWaitingForReconnect(character.Identity)) return "Waiting for reconnect…";
        if (character.Pairs.Any(p => plugin.Configuration.OwnedPauses.TryGetValue(p.Key, out var owned) && owned.RestoreRequested))
            return "Resuming…";
        if (character.Pairs.Count == 0) return "Awaiting sync";
        var active = character.Pairs.Where(p => p.Online && !p.OwnPaused && !p.OtherPaused && !p.ManagerHeld && !p.ExternalHold).ToArray();
        if (active.Length == 0) return "Paused in sync";
        if (!plugin.Configuration.AutomaticManagement) return "Syncing";
        return coordinator.IsKeepingFallback(character.Identity) ? "Managed · fallback" : "Managed";
    }

    private static void Tooltip(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(400 * ImGuiHelpers.GlobalScale);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }
}
