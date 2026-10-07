// Theme values adapted from the 12 ImGui themes supplied with this project.
using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace XivSyncManager;

public enum SyncTheme
{
    DarkStyle,
    ForestGreen,
    Amethyst,
    Sapphire,
    AmberYellow,
    Dracula,
    CatppuccinMocha,
    GruvboxHard,
    CrimsonVesuvius,
    RoseQuartz,
    Cyberpunk,
    PaperAndInk,
}

internal static class ThemeCatalog
{
    private static Vector4[]? darkColors;

    internal static IDisposable Push(SyncTheme theme) => new ThemeScope(theme);

    private static Vector4[] GetDarkColors(ImGuiStylePtr style)
    {
        if (darkColors != null) return darkColors;
        var previous = new Vector4[(int)ImGuiCol.Count];
        for (var index = 0; index < previous.Length; index++) previous[index] = style.Colors[index];
        try
        {
            ImGui.StyleColorsDark();
            var colors = new Vector4[previous.Length];
            for (var index = 0; index < colors.Length; index++) colors[index] = style.Colors[index];
            return darkColors = colors;
        }
        finally
        {
            for (var index = 0; index < previous.Length; index++) style.Colors[index] = previous[index];
        }
    }

    private static (ImGuiCol Color, Vector4 Value)[] Colors(SyncTheme theme) => theme switch
    {
        SyncTheme.DarkStyle =>
        [
        ],
        SyncTheme.ForestGreen =>
        [
            (ImGuiCol.Text, new Vector4(0.85f, 0.90f, 0.85f, 1.00f)),
            (ImGuiCol.TextDisabled, new Vector4(0.50f, 0.55f, 0.50f, 1.00f)),
            (ImGuiCol.WindowBg, new Vector4(0.06f, 0.09f, 0.06f, 1.00f)),
            (ImGuiCol.ChildBg, new Vector4(0.08f, 0.11f, 0.08f, 1.00f)),
            (ImGuiCol.PopupBg, new Vector4(0.07f, 0.10f, 0.07f, 0.96f)),
            (ImGuiCol.Border, new Vector4(0.18f, 0.28f, 0.18f, 0.80f)),
            (ImGuiCol.BorderShadow, new Vector4(0.00f, 0.00f, 0.00f, 0.00f)),
            (ImGuiCol.FrameBg, new Vector4(0.12f, 0.18f, 0.12f, 1.00f)),
            (ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.30f, 0.18f, 1.00f)),
            (ImGuiCol.FrameBgActive, new Vector4(0.24f, 0.42f, 0.24f, 1.00f)),
            (ImGuiCol.TitleBg, new Vector4(0.09f, 0.14f, 0.09f, 1.00f)),
            (ImGuiCol.TitleBgActive, new Vector4(0.14f, 0.26f, 0.14f, 1.00f)),
            (ImGuiCol.TitleBgCollapsed, new Vector4(0.05f, 0.08f, 0.05f, 1.00f)),
            (ImGuiCol.MenuBarBg, new Vector4(0.09f, 0.14f, 0.09f, 1.00f)),
            (ImGuiCol.ScrollbarBg, new Vector4(0.05f, 0.08f, 0.05f, 1.00f)),
            (ImGuiCol.ScrollbarGrab, new Vector4(0.18f, 0.28f, 0.18f, 1.00f)),
            (ImGuiCol.ScrollbarGrabHovered, new Vector4(0.25f, 0.38f, 0.25f, 1.00f)),
            (ImGuiCol.ScrollbarGrabActive, new Vector4(0.32f, 0.48f, 0.32f, 1.00f)),
            (ImGuiCol.CheckMark, new Vector4(0.45f, 0.75f, 0.45f, 1.00f)),
            (ImGuiCol.SliderGrab, new Vector4(0.35f, 0.55f, 0.35f, 1.00f)),
            (ImGuiCol.SliderGrabActive, new Vector4(0.45f, 0.70f, 0.45f, 1.00f)),
            (ImGuiCol.Button, new Vector4(0.18f, 0.35f, 0.18f, 1.00f)),
            (ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.45f, 0.25f, 1.00f)),
            (ImGuiCol.ButtonActive, new Vector4(0.32f, 0.55f, 0.32f, 1.00f)),
            (ImGuiCol.Header, new Vector4(0.18f, 0.35f, 0.18f, 1.00f)),
            (ImGuiCol.HeaderHovered, new Vector4(0.25f, 0.45f, 0.25f, 1.00f)),
            (ImGuiCol.HeaderActive, new Vector4(0.32f, 0.55f, 0.32f, 1.00f)),
            (ImGuiCol.Separator, new Vector4(0.18f, 0.28f, 0.18f, 1.00f)),
            (ImGuiCol.SeparatorHovered, new Vector4(0.25f, 0.45f, 0.25f, 1.00f)),
            (ImGuiCol.SeparatorActive, new Vector4(0.32f, 0.55f, 0.32f, 1.00f)),
            (ImGuiCol.ResizeGrip, new Vector4(0.18f, 0.35f, 0.18f, 0.80f)),
            (ImGuiCol.ResizeGripHovered, new Vector4(0.25f, 0.45f, 0.25f, 1.00f)),
            (ImGuiCol.ResizeGripActive, new Vector4(0.32f, 0.55f, 0.32f, 1.00f)),
            (ImGuiCol.Tab, new Vector4(0.12f, 0.22f, 0.12f, 1.00f)),
            (ImGuiCol.TabHovered, new Vector4(0.25f, 0.45f, 0.25f, 1.00f)),
            (ImGuiCol.TabActive, new Vector4(0.20f, 0.38f, 0.20f, 1.00f)),
            (ImGuiCol.TabUnfocused, new Vector4(0.08f, 0.15f, 0.08f, 1.00f)),
            (ImGuiCol.TabUnfocusedActive, new Vector4(0.12f, 0.22f, 0.12f, 1.00f)),
            (ImGuiCol.PlotLines, new Vector4(0.40f, 0.70f, 0.40f, 1.00f)),
            (ImGuiCol.PlotLinesHovered, new Vector4(0.50f, 0.85f, 0.50f, 1.00f)),
            (ImGuiCol.PlotHistogram, new Vector4(0.40f, 0.70f, 0.40f, 1.00f)),
            (ImGuiCol.PlotHistogramHovered, new Vector4(0.50f, 0.85f, 0.50f, 1.00f)),
            (ImGuiCol.TableHeaderBg, new Vector4(0.12f, 0.22f, 0.12f, 1.00f)),
            (ImGuiCol.TableBorderStrong, new Vector4(0.20f, 0.35f, 0.20f, 1.00f)),
            (ImGuiCol.TableBorderLight, new Vector4(0.15f, 0.25f, 0.15f, 1.00f)),
            (ImGuiCol.TableRowBg, new Vector4(0.00f, 0.00f, 0.00f, 0.00f)),
            (ImGuiCol.TableRowBgAlt, new Vector4(0.08f, 0.14f, 0.08f, 0.50f)),
            (ImGuiCol.TextSelectedBg, new Vector4(0.25f, 0.55f, 0.25f, 0.50f)),
            (ImGuiCol.DragDropTarget, new Vector4(0.60f, 0.90f, 0.60f, 1.00f)),
            (ImGuiCol.NavHighlight, new Vector4(0.40f, 0.80f, 0.40f, 1.00f)),
            (ImGuiCol.NavWindowingHighlight, new Vector4(0.85f, 0.90f, 0.85f, 0.70f)),
            (ImGuiCol.NavWindowingDimBg, new Vector4(0.10f, 0.15f, 0.10f, 0.50f)),
            (ImGuiCol.ModalWindowDimBg, new Vector4(0.05f, 0.08f, 0.05f, 0.60f)),
            (ImGuiCol.DockingPreview, new Vector4(0.25f, 0.55f, 0.25f, 0.50f)),
            (ImGuiCol.DockingEmptyBg, new Vector4(0.06f, 0.09f, 0.06f, 1.00f)),
        ],
        SyncTheme.Amethyst =>
        [
            (ImGuiCol.Text, new Vector4(0.92f, 0.90f, 0.95f, 1.00f)),
            (ImGuiCol.TextDisabled, new Vector4(0.55f, 0.50f, 0.60f, 1.00f)),
            (ImGuiCol.WindowBg, new Vector4(0.09f, 0.07f, 0.12f, 1.00f)),
            (ImGuiCol.ChildBg, new Vector4(0.11f, 0.09f, 0.14f, 1.00f)),
            (ImGuiCol.PopupBg, new Vector4(0.09f, 0.07f, 0.12f, 0.96f)),
            (ImGuiCol.Border, new Vector4(0.25f, 0.20f, 0.35f, 0.80f)),
            (ImGuiCol.BorderShadow, new Vector4(0.00f, 0.00f, 0.00f, 0.00f)),
            (ImGuiCol.FrameBg, new Vector4(0.15f, 0.12f, 0.22f, 1.00f)),
            (ImGuiCol.FrameBgHovered, new Vector4(0.25f, 0.20f, 0.38f, 1.00f)),
            (ImGuiCol.FrameBgActive, new Vector4(0.35f, 0.25f, 0.55f, 1.00f)),
            (ImGuiCol.TitleBg, new Vector4(0.12f, 0.09f, 0.18f, 1.00f)),
            (ImGuiCol.TitleBgActive, new Vector4(0.20f, 0.14f, 0.32f, 1.00f)),
            (ImGuiCol.TitleBgCollapsed, new Vector4(0.07f, 0.05f, 0.10f, 1.00f)),
            (ImGuiCol.MenuBarBg, new Vector4(0.12f, 0.09f, 0.18f, 1.00f)),
            (ImGuiCol.ScrollbarBg, new Vector4(0.07f, 0.05f, 0.10f, 1.00f)),
            (ImGuiCol.ScrollbarGrab, new Vector4(0.25f, 0.20f, 0.35f, 1.00f)),
            (ImGuiCol.ScrollbarGrabHovered, new Vector4(0.35f, 0.30f, 0.50f, 1.00f)),
            (ImGuiCol.ScrollbarGrabActive, new Vector4(0.45f, 0.40f, 0.65f, 1.00f)),
            (ImGuiCol.CheckMark, new Vector4(0.65f, 0.45f, 0.95f, 1.00f)),
            (ImGuiCol.SliderGrab, new Vector4(0.50f, 0.35f, 0.75f, 1.00f)),
            (ImGuiCol.SliderGrabActive, new Vector4(0.65f, 0.45f, 0.95f, 1.00f)),
            (ImGuiCol.Button, new Vector4(0.25f, 0.20f, 0.40f, 1.00f)),
            (ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.28f, 0.62f, 1.00f)),
            (ImGuiCol.ButtonActive, new Vector4(0.50f, 0.35f, 0.80f, 1.00f)),
            (ImGuiCol.Header, new Vector4(0.25f, 0.20f, 0.40f, 1.00f)),
            (ImGuiCol.HeaderHovered, new Vector4(0.38f, 0.28f, 0.62f, 1.00f)),
            (ImGuiCol.HeaderActive, new Vector4(0.50f, 0.35f, 0.80f, 1.00f)),
            (ImGuiCol.Tab, new Vector4(0.15f, 0.12f, 0.25f, 1.00f)),
            (ImGuiCol.TabHovered, new Vector4(0.38f, 0.28f, 0.62f, 1.00f)),
            (ImGuiCol.TabActive, new Vector4(0.28f, 0.20f, 0.45f, 1.00f)),
            (ImGuiCol.TabUnfocused, new Vector4(0.10f, 0.08f, 0.15f, 1.00f)),
            (ImGuiCol.TabUnfocusedActive, new Vector4(0.15f, 0.12f, 0.25f, 1.00f)),
            (ImGuiCol.TableHeaderBg, new Vector4(0.18f, 0.15f, 0.28f, 1.00f)),
            (ImGuiCol.TableBorderStrong, new Vector4(0.25f, 0.20f, 0.40f, 1.00f)),
            (ImGuiCol.TableBorderLight, new Vector4(0.20f, 0.15f, 0.30f, 1.00f)),
            (ImGuiCol.TableRowBgAlt, new Vector4(1.00f, 1.00f, 1.00f, 0.04f)),
            (ImGuiCol.TextSelectedBg, new Vector4(0.50f, 0.35f, 0.80f, 0.35f)),
            (ImGuiCol.DragDropTarget, new Vector4(0.80f, 0.65f, 1.00f, 0.95f)),
            (ImGuiCol.NavHighlight, new Vector4(0.60f, 0.45f, 0.90f, 1.00f)),
            (ImGuiCol.DockingPreview, new Vector4(0.50f, 0.35f, 0.80f, 0.50f)),
            (ImGuiCol.DockingEmptyBg, new Vector4(0.09f, 0.07f, 0.12f, 1.00f)),
        ],
        SyncTheme.Sapphire =>
        [
            (ImGuiCol.Text, new Vector4(0.90f, 0.93f, 0.97f, 1.00f)),
            (ImGuiCol.TextDisabled, new Vector4(0.40f, 0.50f, 0.65f, 1.00f)),
            (ImGuiCol.WindowBg, new Vector4(0.07f, 0.09f, 0.12f, 1.00f)),
            (ImGuiCol.ChildBg, new Vector4(0.09f, 0.12f, 0.16f, 1.00f)),
            (ImGuiCol.PopupBg, new Vector4(0.07f, 0.09f, 0.12f, 0.95f)),
            (ImGuiCol.Border, new Vector4(0.15f, 0.25f, 0.35f, 0.70f)),
            (ImGuiCol.BorderShadow, new Vector4(0.00f, 0.00f, 0.00f, 0.00f)),
            (ImGuiCol.FrameBg, new Vector4(0.12f, 0.18f, 0.26f, 1.00f)),
            (ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.28f, 0.40f, 1.00f)),
            (ImGuiCol.FrameBgActive, new Vector4(0.25f, 0.38f, 0.55f, 1.00f)),
            (ImGuiCol.TitleBg, new Vector4(0.09f, 0.12f, 0.18f, 1.00f)),
            (ImGuiCol.TitleBgActive, new Vector4(0.14f, 0.22f, 0.35f, 1.00f)),
            (ImGuiCol.TitleBgCollapsed, new Vector4(0.05f, 0.08f, 0.12f, 1.00f)),
            (ImGuiCol.MenuBarBg, new Vector4(0.12f, 0.16f, 0.22f, 1.00f)),
            (ImGuiCol.ScrollbarBg, new Vector4(0.06f, 0.08f, 0.11f, 1.00f)),
            (ImGuiCol.ScrollbarGrab, new Vector4(0.20f, 0.32f, 0.48f, 1.00f)),
            (ImGuiCol.ScrollbarGrabHovered, new Vector4(0.28f, 0.42f, 0.60f, 1.00f)),
            (ImGuiCol.ScrollbarGrabActive, new Vector4(0.35f, 0.50f, 0.75f, 1.00f)),
            (ImGuiCol.CheckMark, new Vector4(0.40f, 0.70f, 1.00f, 1.00f)),
            (ImGuiCol.SliderGrab, new Vector4(0.30f, 0.55f, 0.85f, 1.00f)),
            (ImGuiCol.SliderGrabActive, new Vector4(0.45f, 0.75f, 1.00f, 1.00f)),
            (ImGuiCol.Button, new Vector4(0.18f, 0.35f, 0.55f, 1.00f)),
            (ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.48f, 0.75f, 1.00f)),
            (ImGuiCol.ButtonActive, new Vector4(0.35f, 0.60f, 0.90f, 1.00f)),
            (ImGuiCol.Header, new Vector4(0.18f, 0.35f, 0.55f, 1.00f)),
            (ImGuiCol.HeaderHovered, new Vector4(0.25f, 0.48f, 0.75f, 1.00f)),
            (ImGuiCol.HeaderActive, new Vector4(0.35f, 0.60f, 0.90f, 1.00f)),
            (ImGuiCol.Tab, new Vector4(0.12f, 0.20f, 0.32f, 1.00f)),
            (ImGuiCol.TabHovered, new Vector4(0.25f, 0.45f, 0.70f, 1.00f)),
            (ImGuiCol.TabActive, new Vector4(0.18f, 0.35f, 0.55f, 1.00f)),
            (ImGuiCol.TabUnfocused, new Vector4(0.08f, 0.12f, 0.18f, 1.00f)),
            (ImGuiCol.TabUnfocusedActive, new Vector4(0.12f, 0.20f, 0.32f, 1.00f)),
            (ImGuiCol.TableHeaderBg, new Vector4(0.15f, 0.25f, 0.40f, 1.00f)),
            (ImGuiCol.TableBorderStrong, new Vector4(0.20f, 0.35f, 0.55f, 1.00f)),
            (ImGuiCol.TableBorderLight, new Vector4(0.15f, 0.25f, 0.40f, 1.00f)),
            (ImGuiCol.TableRowBgAlt, new Vector4(1.00f, 1.00f, 1.00f, 0.05f)),
            (ImGuiCol.TextSelectedBg, new Vector4(0.30f, 0.55f, 0.85f, 0.40f)),
            (ImGuiCol.DragDropTarget, new Vector4(0.50f, 0.80f, 1.00f, 0.90f)),
            (ImGuiCol.NavHighlight, new Vector4(0.40f, 0.70f, 1.00f, 1.00f)),
            (ImGuiCol.DockingPreview, new Vector4(0.25f, 0.50f, 0.80f, 0.50f)),
            (ImGuiCol.DockingEmptyBg, new Vector4(0.07f, 0.09f, 0.12f, 1.00f)),
        ],
        SyncTheme.AmberYellow =>
        [
            (ImGuiCol.Text, new Vector4(1.00f, 0.95f, 0.80f, 1.00f)),
            (ImGuiCol.TextDisabled, new Vector4(0.50f, 0.45f, 0.30f, 1.00f)),
            (ImGuiCol.WindowBg, new Vector4(0.07f, 0.07f, 0.06f, 1.00f)),
            (ImGuiCol.ChildBg, new Vector4(0.09f, 0.09f, 0.08f, 1.00f)),
            (ImGuiCol.PopupBg, new Vector4(0.07f, 0.07f, 0.06f, 0.96f)),
            (ImGuiCol.Border, new Vector4(0.30f, 0.25f, 0.10f, 0.80f)),
            (ImGuiCol.BorderShadow, new Vector4(0.00f, 0.00f, 0.00f, 0.00f)),
            (ImGuiCol.FrameBg, new Vector4(0.15f, 0.14f, 0.10f, 1.00f)),
            (ImGuiCol.FrameBgHovered, new Vector4(0.25f, 0.22f, 0.12f, 1.00f)),
            (ImGuiCol.FrameBgActive, new Vector4(0.35f, 0.30f, 0.15f, 1.00f)),
            (ImGuiCol.TitleBg, new Vector4(0.12f, 0.11f, 0.08f, 1.00f)),
            (ImGuiCol.TitleBgActive, new Vector4(0.20f, 0.18f, 0.10f, 1.00f)),
            (ImGuiCol.TitleBgCollapsed, new Vector4(0.05f, 0.05f, 0.04f, 1.00f)),
            (ImGuiCol.MenuBarBg, new Vector4(0.12f, 0.11f, 0.08f, 1.00f)),
            (ImGuiCol.ScrollbarBg, new Vector4(0.05f, 0.05f, 0.04f, 1.00f)),
            (ImGuiCol.ScrollbarGrab, new Vector4(0.35f, 0.30f, 0.10f, 1.00f)),
            (ImGuiCol.ScrollbarGrabHovered, new Vector4(0.45f, 0.40f, 0.15f, 1.00f)),
            (ImGuiCol.ScrollbarGrabActive, new Vector4(0.55f, 0.50f, 0.20f, 1.00f)),
            (ImGuiCol.CheckMark, new Vector4(0.95f, 0.80f, 0.10f, 1.00f)),
            (ImGuiCol.SliderGrab, new Vector4(0.70f, 0.60f, 0.10f, 1.00f)),
            (ImGuiCol.SliderGrabActive, new Vector4(0.95f, 0.80f, 0.10f, 1.00f)),
            (ImGuiCol.Button, new Vector4(0.30f, 0.25f, 0.05f, 1.00f)),
            (ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.38f, 0.10f, 1.00f)),
            (ImGuiCol.ButtonActive, new Vector4(0.60f, 0.50f, 0.15f, 1.00f)),
            (ImGuiCol.Header, new Vector4(0.30f, 0.25f, 0.05f, 1.00f)),
            (ImGuiCol.HeaderHovered, new Vector4(0.45f, 0.38f, 0.10f, 1.00f)),
            (ImGuiCol.HeaderActive, new Vector4(0.60f, 0.50f, 0.15f, 1.00f)),
            (ImGuiCol.Tab, new Vector4(0.15f, 0.14f, 0.10f, 1.00f)),
            (ImGuiCol.TabHovered, new Vector4(0.45f, 0.38f, 0.10f, 1.00f)),
            (ImGuiCol.TabActive, new Vector4(0.35f, 0.30f, 0.10f, 1.00f)),
            (ImGuiCol.TabUnfocused, new Vector4(0.08f, 0.08f, 0.07f, 1.00f)),
            (ImGuiCol.TabUnfocusedActive, new Vector4(0.15f, 0.14f, 0.10f, 1.00f)),
            (ImGuiCol.TableHeaderBg, new Vector4(0.18f, 0.16f, 0.10f, 1.00f)),
            (ImGuiCol.TableBorderStrong, new Vector4(0.35f, 0.30f, 0.15f, 1.00f)),
            (ImGuiCol.TableBorderLight, new Vector4(0.25f, 0.20f, 0.10f, 1.00f)),
            (ImGuiCol.TableRowBgAlt, new Vector4(1.00f, 1.00f, 1.00f, 0.03f)),
            (ImGuiCol.TextSelectedBg, new Vector4(0.95f, 0.80f, 0.10f, 0.25f)),
            (ImGuiCol.DragDropTarget, new Vector4(1.00f, 0.85f, 0.00f, 0.90f)),
            (ImGuiCol.NavHighlight, new Vector4(0.95f, 0.80f, 0.10f, 1.00f)),
            (ImGuiCol.DockingPreview, new Vector4(0.95f, 0.80f, 0.10f, 0.40f)),
            (ImGuiCol.DockingEmptyBg, new Vector4(0.07f, 0.07f, 0.06f, 1.00f)),
        ],
        SyncTheme.Dracula =>
        [
            (ImGuiCol.Text, new Vector4(0.97f, 0.97f, 0.95f, 1.00f)),
            (ImGuiCol.TextDisabled, new Vector4(0.38f, 0.45f, 0.64f, 1.00f)),
            (ImGuiCol.WindowBg, new Vector4(0.16f, 0.16f, 0.21f, 1.00f)),
            (ImGuiCol.ChildBg, new Vector4(0.16f, 0.16f, 0.21f, 0.00f)),
            (ImGuiCol.PopupBg, new Vector4(0.16f, 0.16f, 0.21f, 0.96f)),
            (ImGuiCol.Border, new Vector4(0.27f, 0.28f, 0.35f, 1.00f)),
            (ImGuiCol.BorderShadow, new Vector4(0.00f, 0.00f, 0.00f, 0.00f)),
            (ImGuiCol.FrameBg, new Vector4(0.27f, 0.28f, 0.35f, 1.00f)),
            (ImGuiCol.FrameBgHovered, new Vector4(0.38f, 0.45f, 0.64f, 1.00f)),
            (ImGuiCol.FrameBgActive, new Vector4(0.48f, 0.55f, 0.74f, 1.00f)),
            (ImGuiCol.TitleBg, new Vector4(0.13f, 0.14f, 0.18f, 1.00f)),
            (ImGuiCol.TitleBgActive, new Vector4(0.16f, 0.16f, 0.21f, 1.00f)),
            (ImGuiCol.TitleBgCollapsed, new Vector4(0.13f, 0.14f, 0.18f, 1.00f)),
            (ImGuiCol.MenuBarBg, new Vector4(0.13f, 0.14f, 0.18f, 1.00f)),
            (ImGuiCol.ScrollbarBg, new Vector4(0.16f, 0.16f, 0.21f, 1.00f)),
            (ImGuiCol.ScrollbarGrab, new Vector4(0.27f, 0.28f, 0.35f, 1.00f)),
            (ImGuiCol.ScrollbarGrabHovered, new Vector4(0.38f, 0.45f, 0.64f, 1.00f)),
            (ImGuiCol.ScrollbarGrabActive, new Vector4(0.48f, 0.55f, 0.74f, 1.00f)),
            (ImGuiCol.CheckMark, new Vector4(0.31f, 0.98f, 0.48f, 1.00f)),
            (ImGuiCol.SliderGrab, new Vector4(0.74f, 0.58f, 0.98f, 1.00f)),
            (ImGuiCol.SliderGrabActive, new Vector4(0.84f, 0.68f, 1.00f, 1.00f)),
            (ImGuiCol.Button, new Vector4(0.27f, 0.28f, 0.35f, 1.00f)),
            (ImGuiCol.ButtonHovered, new Vector4(1.00f, 0.47f, 0.78f, 1.00f)),
            (ImGuiCol.ButtonActive, new Vector4(0.80f, 0.37f, 0.62f, 1.00f)),
            (ImGuiCol.Header, new Vector4(0.27f, 0.28f, 0.35f, 1.00f)),
            (ImGuiCol.HeaderHovered, new Vector4(0.38f, 0.45f, 0.64f, 1.00f)),
            (ImGuiCol.HeaderActive, new Vector4(0.48f, 0.55f, 0.74f, 1.00f)),
            (ImGuiCol.Tab, new Vector4(0.16f, 0.16f, 0.21f, 1.00f)),
            (ImGuiCol.TabHovered, new Vector4(0.27f, 0.28f, 0.35f, 1.00f)),
            (ImGuiCol.TabActive, new Vector4(0.27f, 0.28f, 0.35f, 1.00f)),
            (ImGuiCol.TabUnfocused, new Vector4(0.13f, 0.14f, 0.18f, 1.00f)),
            (ImGuiCol.TabUnfocusedActive, new Vector4(0.16f, 0.16f, 0.21f, 1.00f)),
            (ImGuiCol.TableHeaderBg, new Vector4(0.27f, 0.28f, 0.35f, 1.00f)),
            (ImGuiCol.TableBorderStrong, new Vector4(0.38f, 0.45f, 0.64f, 1.00f)),
            (ImGuiCol.TableBorderLight, new Vector4(0.27f, 0.28f, 0.35f, 1.00f)),
            (ImGuiCol.PlotLines, new Vector4(0.55f, 0.91f, 0.99f, 1.00f)),
            (ImGuiCol.TextSelectedBg, new Vector4(0.27f, 0.28f, 0.35f, 1.00f)),
            (ImGuiCol.NavHighlight, new Vector4(0.74f, 0.58f, 0.98f, 1.00f)),
            (ImGuiCol.DockingPreview, new Vector4(0.74f, 0.58f, 0.98f, 0.50f)),
            (ImGuiCol.DockingEmptyBg, new Vector4(0.16f, 0.16f, 0.21f, 1.00f)),
        ],
        SyncTheme.CatppuccinMocha =>
        [
            (ImGuiCol.Text, new Vector4(0.80f, 0.84f, 0.96f, 1.00f)),
            (ImGuiCol.TextDisabled, new Vector4(0.42f, 0.45f, 0.55f, 1.00f)),
            (ImGuiCol.WindowBg, new Vector4(0.12f, 0.12f, 0.18f, 1.00f)),
            (ImGuiCol.ChildBg, new Vector4(0.09f, 0.09f, 0.15f, 1.00f)),
            (ImGuiCol.PopupBg, new Vector4(0.07f, 0.07f, 0.11f, 0.96f)),
            (ImGuiCol.Border, new Vector4(0.19f, 0.20f, 0.27f, 1.00f)),
            (ImGuiCol.BorderShadow, new Vector4(0.00f, 0.00f, 0.00f, 0.00f)),
            (ImGuiCol.FrameBg, new Vector4(0.19f, 0.20f, 0.27f, 1.00f)),
            (ImGuiCol.FrameBgHovered, new Vector4(0.25f, 0.26f, 0.35f, 1.00f)),
            (ImGuiCol.FrameBgActive, new Vector4(0.31f, 0.32f, 0.42f, 1.00f)),
            (ImGuiCol.TitleBg, new Vector4(0.09f, 0.09f, 0.15f, 1.00f)),
            (ImGuiCol.TitleBgActive, new Vector4(0.12f, 0.12f, 0.18f, 1.00f)),
            (ImGuiCol.TitleBgCollapsed, new Vector4(0.07f, 0.07f, 0.11f, 1.00f)),
            (ImGuiCol.MenuBarBg, new Vector4(0.09f, 0.09f, 0.15f, 1.00f)),
            (ImGuiCol.ScrollbarBg, new Vector4(0.09f, 0.09f, 0.15f, 1.00f)),
            (ImGuiCol.ScrollbarGrab, new Vector4(0.31f, 0.32f, 0.42f, 1.00f)),
            (ImGuiCol.ScrollbarGrabHovered, new Vector4(0.37f, 0.38f, 0.51f, 1.00f)),
            (ImGuiCol.ScrollbarGrabActive, new Vector4(0.42f, 0.45f, 0.55f, 1.00f)),
            (ImGuiCol.CheckMark, new Vector4(0.71f, 0.75f, 1.00f, 1.00f)),
            (ImGuiCol.SliderGrab, new Vector4(0.45f, 0.78f, 0.93f, 1.00f)),
            (ImGuiCol.SliderGrabActive, new Vector4(0.45f, 0.78f, 0.93f, 1.00f)),
            (ImGuiCol.Button, new Vector4(0.19f, 0.20f, 0.27f, 1.00f)),
            (ImGuiCol.ButtonHovered, new Vector4(0.80f, 0.65f, 0.97f, 1.00f)),
            (ImGuiCol.ButtonActive, new Vector4(0.70f, 0.55f, 0.87f, 1.00f)),
            (ImGuiCol.Header, new Vector4(0.19f, 0.20f, 0.27f, 1.00f)),
            (ImGuiCol.HeaderHovered, new Vector4(0.25f, 0.26f, 0.35f, 1.00f)),
            (ImGuiCol.HeaderActive, new Vector4(0.31f, 0.32f, 0.42f, 1.00f)),
            (ImGuiCol.Tab, new Vector4(0.12f, 0.12f, 0.18f, 1.00f)),
            (ImGuiCol.TabHovered, new Vector4(0.31f, 0.32f, 0.42f, 1.00f)),
            (ImGuiCol.TabActive, new Vector4(0.19f, 0.20f, 0.27f, 1.00f)),
            (ImGuiCol.TabUnfocused, new Vector4(0.09f, 0.09f, 0.15f, 1.00f)),
            (ImGuiCol.TabUnfocusedActive, new Vector4(0.12f, 0.12f, 0.18f, 1.00f)),
            (ImGuiCol.PlotLines, new Vector4(0.94f, 0.72f, 0.42f, 1.00f)),
            (ImGuiCol.TextSelectedBg, new Vector4(0.31f, 0.32f, 0.42f, 1.00f)),
            (ImGuiCol.NavHighlight, new Vector4(0.71f, 0.75f, 1.00f, 1.00f)),
            (ImGuiCol.DockingPreview, new Vector4(0.71f, 0.75f, 1.00f, 0.50f)),
            (ImGuiCol.DockingEmptyBg, new Vector4(0.12f, 0.12f, 0.18f, 1.00f)),
        ],
        SyncTheme.GruvboxHard =>
        [
            (ImGuiCol.Text, new Vector4(0.92f, 0.86f, 0.70f, 1.00f)),
            (ImGuiCol.TextDisabled, new Vector4(0.57f, 0.51f, 0.45f, 1.00f)),
            (ImGuiCol.WindowBg, new Vector4(0.11f, 0.13f, 0.13f, 1.00f)),
            (ImGuiCol.ChildBg, new Vector4(0.11f, 0.13f, 0.13f, 0.00f)),
            (ImGuiCol.PopupBg, new Vector4(0.11f, 0.13f, 0.13f, 0.95f)),
            (ImGuiCol.Border, new Vector4(0.31f, 0.29f, 0.27f, 1.00f)),
            (ImGuiCol.BorderShadow, new Vector4(0.00f, 0.00f, 0.00f, 0.00f)),
            (ImGuiCol.FrameBg, new Vector4(0.24f, 0.22f, 0.21f, 1.00f)),
            (ImGuiCol.FrameBgHovered, new Vector4(0.31f, 0.29f, 0.27f, 1.00f)),
            (ImGuiCol.FrameBgActive, new Vector4(0.40f, 0.36f, 0.33f, 1.00f)),
            (ImGuiCol.TitleBg, new Vector4(0.11f, 0.13f, 0.13f, 1.00f)),
            (ImGuiCol.TitleBgActive, new Vector4(0.11f, 0.13f, 0.13f, 1.00f)),
            (ImGuiCol.TitleBgCollapsed, new Vector4(0.11f, 0.13f, 0.13f, 1.00f)),
            (ImGuiCol.MenuBarBg, new Vector4(0.15f, 0.14f, 0.13f, 1.00f)),
            (ImGuiCol.ScrollbarBg, new Vector4(0.11f, 0.13f, 0.13f, 1.00f)),
            (ImGuiCol.ScrollbarGrab, new Vector4(0.31f, 0.29f, 0.27f, 1.00f)),
            (ImGuiCol.ScrollbarGrabHovered, new Vector4(0.40f, 0.36f, 0.33f, 1.00f)),
            (ImGuiCol.ScrollbarGrabActive, new Vector4(0.57f, 0.51f, 0.45f, 1.00f)),
            (ImGuiCol.CheckMark, new Vector4(0.72f, 0.73f, 0.15f, 1.00f)),
            (ImGuiCol.SliderGrab, new Vector4(0.51f, 0.65f, 0.60f, 1.00f)),
            (ImGuiCol.SliderGrabActive, new Vector4(0.55f, 0.73f, 0.67f, 1.00f)),
            (ImGuiCol.Button, new Vector4(0.31f, 0.29f, 0.27f, 1.00f)),
            (ImGuiCol.ButtonHovered, new Vector4(0.98f, 0.29f, 0.20f, 1.00f)),
            (ImGuiCol.ButtonActive, new Vector4(0.80f, 0.20f, 0.15f, 1.00f)),
            (ImGuiCol.Header, new Vector4(0.24f, 0.22f, 0.21f, 1.00f)),
            (ImGuiCol.HeaderHovered, new Vector4(0.31f, 0.29f, 0.27f, 1.00f)),
            (ImGuiCol.HeaderActive, new Vector4(0.40f, 0.36f, 0.33f, 1.00f)),
            (ImGuiCol.Tab, new Vector4(0.24f, 0.22f, 0.21f, 1.00f)),
            (ImGuiCol.TabHovered, new Vector4(0.31f, 0.29f, 0.27f, 1.00f)),
            (ImGuiCol.TabActive, new Vector4(0.31f, 0.29f, 0.27f, 1.00f)),
            (ImGuiCol.PlotLines, new Vector4(0.98f, 0.74f, 0.18f, 1.00f)),
            (ImGuiCol.TextSelectedBg, new Vector4(0.31f, 0.29f, 0.27f, 1.00f)),
            (ImGuiCol.NavHighlight, new Vector4(0.98f, 0.29f, 0.20f, 1.00f)),
            (ImGuiCol.DockingPreview, new Vector4(0.72f, 0.73f, 0.15f, 0.50f)),
            (ImGuiCol.DockingEmptyBg, new Vector4(0.11f, 0.13f, 0.13f, 1.00f)),
        ],
        SyncTheme.CrimsonVesuvius =>
        [
            (ImGuiCol.Text, new Vector4(1.00f, 0.90f, 0.90f, 1.00f)),
            (ImGuiCol.TextDisabled, new Vector4(0.50f, 0.40f, 0.40f, 1.00f)),
            (ImGuiCol.WindowBg, new Vector4(0.08f, 0.07f, 0.07f, 1.00f)),
            (ImGuiCol.ChildBg, new Vector4(0.10f, 0.09f, 0.09f, 1.00f)),
            (ImGuiCol.PopupBg, new Vector4(0.08f, 0.07f, 0.07f, 0.96f)),
            (ImGuiCol.Border, new Vector4(0.25f, 0.15f, 0.15f, 0.80f)),
            (ImGuiCol.BorderShadow, new Vector4(0.00f, 0.00f, 0.00f, 0.00f)),
            (ImGuiCol.FrameBg, new Vector4(0.15f, 0.10f, 0.10f, 1.00f)),
            (ImGuiCol.FrameBgHovered, new Vector4(0.25f, 0.15f, 0.15f, 1.00f)),
            (ImGuiCol.FrameBgActive, new Vector4(0.35f, 0.20f, 0.20f, 1.00f)),
            (ImGuiCol.TitleBg, new Vector4(0.12f, 0.08f, 0.08f, 1.00f)),
            (ImGuiCol.TitleBgActive, new Vector4(0.25f, 0.10f, 0.10f, 1.00f)),
            (ImGuiCol.TitleBgCollapsed, new Vector4(0.05f, 0.05f, 0.05f, 1.00f)),
            (ImGuiCol.MenuBarBg, new Vector4(0.12f, 0.08f, 0.08f, 1.00f)),
            (ImGuiCol.ScrollbarBg, new Vector4(0.05f, 0.05f, 0.05f, 1.00f)),
            (ImGuiCol.ScrollbarGrab, new Vector4(0.25f, 0.12f, 0.12f, 1.00f)),
            (ImGuiCol.ScrollbarGrabHovered, new Vector4(0.35f, 0.15f, 0.15f, 1.00f)),
            (ImGuiCol.ScrollbarGrabActive, new Vector4(0.45f, 0.20f, 0.20f, 1.00f)),
            (ImGuiCol.CheckMark, new Vector4(0.85f, 0.15f, 0.15f, 1.00f)),
            (ImGuiCol.SliderGrab, new Vector4(0.60f, 0.12f, 0.12f, 1.00f)),
            (ImGuiCol.SliderGrabActive, new Vector4(0.85f, 0.15f, 0.15f, 1.00f)),
            (ImGuiCol.Button, new Vector4(0.30f, 0.12f, 0.12f, 1.00f)),
            (ImGuiCol.ButtonHovered, new Vector4(0.50f, 0.18f, 0.18f, 1.00f)),
            (ImGuiCol.ButtonActive, new Vector4(0.70f, 0.25f, 0.25f, 1.00f)),
            (ImGuiCol.Header, new Vector4(0.30f, 0.12f, 0.12f, 1.00f)),
            (ImGuiCol.HeaderHovered, new Vector4(0.50f, 0.18f, 0.18f, 1.00f)),
            (ImGuiCol.HeaderActive, new Vector4(0.70f, 0.25f, 0.25f, 1.00f)),
            (ImGuiCol.Tab, new Vector4(0.15f, 0.10f, 0.10f, 1.00f)),
            (ImGuiCol.TabHovered, new Vector4(0.50f, 0.18f, 0.18f, 1.00f)),
            (ImGuiCol.TabActive, new Vector4(0.35f, 0.12f, 0.12f, 1.00f)),
            (ImGuiCol.PlotLines, new Vector4(0.85f, 0.20f, 0.20f, 1.00f)),
            (ImGuiCol.TextSelectedBg, new Vector4(0.85f, 0.15f, 0.15f, 0.35f)),
            (ImGuiCol.NavHighlight, new Vector4(0.85f, 0.15f, 0.15f, 1.00f)),
            (ImGuiCol.DockingPreview, new Vector4(0.85f, 0.15f, 0.15f, 0.40f)),
            (ImGuiCol.DockingEmptyBg, new Vector4(0.08f, 0.07f, 0.07f, 1.00f)),
        ],
        SyncTheme.RoseQuartz =>
        [
            (ImGuiCol.Text, new Vector4(0.95f, 0.90f, 0.95f, 1.00f)),
            (ImGuiCol.TextDisabled, new Vector4(0.55f, 0.45f, 0.55f, 1.00f)),
            (ImGuiCol.WindowBg, new Vector4(0.12f, 0.10f, 0.12f, 1.00f)),
            (ImGuiCol.ChildBg, new Vector4(0.14f, 0.12f, 0.14f, 1.00f)),
            (ImGuiCol.PopupBg, new Vector4(0.10f, 0.08f, 0.10f, 0.96f)),
            (ImGuiCol.Border, new Vector4(0.35f, 0.25f, 0.35f, 0.50f)),
            (ImGuiCol.BorderShadow, new Vector4(0.00f, 0.00f, 0.00f, 0.00f)),
            (ImGuiCol.FrameBg, new Vector4(0.20f, 0.15f, 0.20f, 1.00f)),
            (ImGuiCol.FrameBgHovered, new Vector4(0.30f, 0.22f, 0.30f, 1.00f)),
            (ImGuiCol.FrameBgActive, new Vector4(0.40f, 0.28f, 0.40f, 1.00f)),
            (ImGuiCol.TitleBg, new Vector4(0.15f, 0.10f, 0.15f, 1.00f)),
            (ImGuiCol.TitleBgActive, new Vector4(0.25f, 0.15f, 0.25f, 1.00f)),
            (ImGuiCol.TitleBgCollapsed, new Vector4(0.08f, 0.06f, 0.08f, 1.00f)),
            (ImGuiCol.MenuBarBg, new Vector4(0.15f, 0.10f, 0.15f, 1.00f)),
            (ImGuiCol.ScrollbarBg, new Vector4(0.08f, 0.06f, 0.08f, 1.00f)),
            (ImGuiCol.ScrollbarGrab, new Vector4(0.40f, 0.25f, 0.40f, 1.00f)),
            (ImGuiCol.ScrollbarGrabHovered, new Vector4(0.55f, 0.35f, 0.55f, 1.00f)),
            (ImGuiCol.ScrollbarGrabActive, new Vector4(0.70f, 0.45f, 0.70f, 1.00f)),
            (ImGuiCol.CheckMark, new Vector4(0.95f, 0.60f, 0.75f, 1.00f)),
            (ImGuiCol.SliderGrab, new Vector4(0.85f, 0.50f, 0.65f, 1.00f)),
            (ImGuiCol.SliderGrabActive, new Vector4(0.95f, 0.60f, 0.75f, 1.00f)),
            (ImGuiCol.Button, new Vector4(0.45f, 0.25f, 0.35f, 1.00f)),
            (ImGuiCol.ButtonHovered, new Vector4(0.65f, 0.35f, 0.50f, 1.00f)),
            (ImGuiCol.ButtonActive, new Vector4(0.85f, 0.45f, 0.65f, 1.00f)),
            (ImGuiCol.Header, new Vector4(0.45f, 0.25f, 0.35f, 1.00f)),
            (ImGuiCol.HeaderHovered, new Vector4(0.55f, 0.30f, 0.45f, 1.00f)),
            (ImGuiCol.HeaderActive, new Vector4(0.65f, 0.35f, 0.55f, 1.00f)),
            (ImGuiCol.Tab, new Vector4(0.20f, 0.15f, 0.20f, 1.00f)),
            (ImGuiCol.TabHovered, new Vector4(0.65f, 0.35f, 0.50f, 1.00f)),
            (ImGuiCol.TabActive, new Vector4(0.45f, 0.25f, 0.35f, 1.00f)),
            (ImGuiCol.TextSelectedBg, new Vector4(0.95f, 0.60f, 0.75f, 0.35f)),
            (ImGuiCol.NavHighlight, new Vector4(0.95f, 0.60f, 0.75f, 1.00f)),
            (ImGuiCol.DockingPreview, new Vector4(0.95f, 0.60f, 0.75f, 0.40f)),
            (ImGuiCol.DockingEmptyBg, new Vector4(0.12f, 0.10f, 0.12f, 1.00f)),
        ],
        SyncTheme.Cyberpunk =>
        [
            (ImGuiCol.Text, new Vector4(0.00f, 1.00f, 0.62f, 1.00f)),
            (ImGuiCol.TextDisabled, new Vector4(0.20f, 0.40f, 0.35f, 1.00f)),
            (ImGuiCol.WindowBg, new Vector4(0.02f, 0.02f, 0.04f, 1.00f)),
            (ImGuiCol.ChildBg, new Vector4(0.02f, 0.02f, 0.04f, 0.00f)),
            (ImGuiCol.PopupBg, new Vector4(0.02f, 0.02f, 0.04f, 0.98f)),
            (ImGuiCol.Border, new Vector4(1.00f, 0.00f, 0.25f, 0.60f)),
            (ImGuiCol.BorderShadow, new Vector4(1.00f, 0.00f, 0.25f, 0.20f)),
            (ImGuiCol.FrameBg, new Vector4(0.05f, 0.05f, 0.10f, 1.00f)),
            (ImGuiCol.FrameBgHovered, new Vector4(1.00f, 0.00f, 0.25f, 0.20f)),
            (ImGuiCol.FrameBgActive, new Vector4(1.00f, 0.00f, 0.25f, 0.40f)),
            (ImGuiCol.TitleBg, new Vector4(0.02f, 0.02f, 0.04f, 1.00f)),
            (ImGuiCol.TitleBgActive, new Vector4(0.05f, 0.05f, 0.10f, 1.00f)),
            (ImGuiCol.TitleBgCollapsed, new Vector4(0.02f, 0.02f, 0.04f, 1.00f)),
            (ImGuiCol.MenuBarBg, new Vector4(0.05f, 0.05f, 0.10f, 1.00f)),
            (ImGuiCol.ScrollbarBg, new Vector4(0.02f, 0.02f, 0.04f, 1.00f)),
            (ImGuiCol.ScrollbarGrab, new Vector4(1.00f, 0.93f, 0.04f, 0.60f)),
            (ImGuiCol.ScrollbarGrabHovered, new Vector4(1.00f, 0.93f, 0.04f, 0.80f)),
            (ImGuiCol.ScrollbarGrabActive, new Vector4(1.00f, 0.93f, 0.04f, 1.00f)),
            (ImGuiCol.CheckMark, new Vector4(1.00f, 0.93f, 0.04f, 1.00f)),
            (ImGuiCol.SliderGrab, new Vector4(1.00f, 0.00f, 0.25f, 0.80f)),
            (ImGuiCol.SliderGrabActive, new Vector4(1.00f, 0.00f, 0.25f, 1.00f)),
            (ImGuiCol.Button, new Vector4(0.00f, 1.00f, 0.62f, 0.20f)),
            (ImGuiCol.ButtonHovered, new Vector4(0.00f, 1.00f, 0.62f, 0.50f)),
            (ImGuiCol.ButtonActive, new Vector4(0.00f, 1.00f, 0.62f, 1.00f)),
            (ImGuiCol.Header, new Vector4(1.00f, 0.00f, 0.25f, 0.30f)),
            (ImGuiCol.HeaderHovered, new Vector4(1.00f, 0.00f, 0.25f, 0.50f)),
            (ImGuiCol.HeaderActive, new Vector4(1.00f, 0.00f, 0.25f, 1.00f)),
            (ImGuiCol.Tab, new Vector4(0.05f, 0.05f, 0.10f, 1.00f)),
            (ImGuiCol.TabHovered, new Vector4(1.00f, 0.00f, 0.25f, 0.80f)),
            (ImGuiCol.TabActive, new Vector4(0.80f, 0.00f, 0.20f, 1.00f)),
            (ImGuiCol.TextSelectedBg, new Vector4(1.00f, 0.93f, 0.04f, 0.30f)),
            (ImGuiCol.NavHighlight, new Vector4(1.00f, 0.00f, 0.25f, 1.00f)),
            (ImGuiCol.DockingPreview, new Vector4(0.00f, 1.00f, 0.62f, 0.40f)),
            (ImGuiCol.DockingEmptyBg, new Vector4(0.02f, 0.02f, 0.04f, 1.00f)),
        ],
        SyncTheme.PaperAndInk =>
        [
            (ImGuiCol.Text, new Vector4(0.12f, 0.12f, 0.12f, 1.00f)),
            (ImGuiCol.TextDisabled, new Vector4(0.55f, 0.55f, 0.55f, 1.00f)),
            (ImGuiCol.WindowBg, new Vector4(0.96f, 0.96f, 0.94f, 1.00f)),
            (ImGuiCol.ChildBg, new Vector4(0.00f, 0.00f, 0.00f, 0.03f)),
            (ImGuiCol.PopupBg, new Vector4(1.00f, 1.00f, 1.00f, 1.00f)),
            (ImGuiCol.Border, new Vector4(0.75f, 0.75f, 0.72f, 1.00f)),
            (ImGuiCol.BorderShadow, new Vector4(0.00f, 0.00f, 0.00f, 0.00f)),
            (ImGuiCol.Separator, new Vector4(0.80f, 0.80f, 0.78f, 1.00f)),
            (ImGuiCol.SeparatorHovered, new Vector4(0.17f, 0.34f, 0.59f, 0.78f)),
            (ImGuiCol.SeparatorActive, new Vector4(0.17f, 0.34f, 0.59f, 1.00f)),
            (ImGuiCol.FrameBg, new Vector4(1.00f, 1.00f, 1.00f, 1.00f)),
            (ImGuiCol.FrameBgHovered, new Vector4(0.90f, 0.92f, 0.95f, 1.00f)),
            (ImGuiCol.FrameBgActive, new Vector4(0.85f, 0.88f, 0.92f, 1.00f)),
            (ImGuiCol.TitleBg, new Vector4(0.92f, 0.92f, 0.90f, 1.00f)),
            (ImGuiCol.TitleBgActive, new Vector4(0.88f, 0.88f, 0.86f, 1.00f)),
            (ImGuiCol.TitleBgCollapsed, new Vector4(0.92f, 0.92f, 0.90f, 0.75f)),
            (ImGuiCol.MenuBarBg, new Vector4(0.92f, 0.92f, 0.90f, 1.00f)),
            (ImGuiCol.ScrollbarBg, new Vector4(0.96f, 0.96f, 0.94f, 1.00f)),
            (ImGuiCol.ScrollbarGrab, new Vector4(0.80f, 0.80f, 0.78f, 1.00f)),
            (ImGuiCol.ScrollbarGrabHovered, new Vector4(0.70f, 0.70f, 0.68f, 1.00f)),
            (ImGuiCol.ScrollbarGrabActive, new Vector4(0.60f, 0.60f, 0.58f, 1.00f)),
            (ImGuiCol.CheckMark, new Vector4(0.17f, 0.34f, 0.59f, 1.00f)),
            (ImGuiCol.SliderGrab, new Vector4(0.17f, 0.34f, 0.59f, 0.70f)),
            (ImGuiCol.SliderGrabActive, new Vector4(0.17f, 0.34f, 0.59f, 1.00f)),
            (ImGuiCol.Button, new Vector4(0.17f, 0.34f, 0.59f, 0.08f)),
            (ImGuiCol.ButtonHovered, new Vector4(0.17f, 0.34f, 0.59f, 0.20f)),
            (ImGuiCol.ButtonActive, new Vector4(0.17f, 0.34f, 0.59f, 0.35f)),
            (ImGuiCol.Header, new Vector4(0.17f, 0.34f, 0.59f, 0.12f)),
            (ImGuiCol.HeaderHovered, new Vector4(0.17f, 0.34f, 0.59f, 0.25f)),
            (ImGuiCol.HeaderActive, new Vector4(0.17f, 0.34f, 0.59f, 0.40f)),
            (ImGuiCol.TableHeaderBg, new Vector4(0.90f, 0.90f, 0.88f, 1.00f)),
            (ImGuiCol.TableBorderStrong, new Vector4(0.75f, 0.75f, 0.72f, 1.00f)),
            (ImGuiCol.TableBorderLight, new Vector4(0.85f, 0.85f, 0.82f, 1.00f)),
            (ImGuiCol.TableRowBgAlt, new Vector4(0.00f, 0.00f, 0.00f, 0.03f)),
            (ImGuiCol.Tab, new Vector4(0.92f, 0.92f, 0.90f, 1.00f)),
            (ImGuiCol.TabHovered, new Vector4(1.00f, 1.00f, 1.00f, 1.00f)),
            (ImGuiCol.TabActive, new Vector4(1.00f, 1.00f, 1.00f, 1.00f)),
            (ImGuiCol.TabUnfocused, new Vector4(0.92f, 0.92f, 0.90f, 1.00f)),
            (ImGuiCol.TabUnfocusedActive, new Vector4(0.96f, 0.96f, 0.94f, 1.00f)),
            (ImGuiCol.PlotLines, new Vector4(0.17f, 0.34f, 0.59f, 1.00f)),
            (ImGuiCol.PlotHistogram, new Vector4(0.17f, 0.34f, 0.59f, 1.00f)),
            (ImGuiCol.TextSelectedBg, new Vector4(0.17f, 0.34f, 0.59f, 0.25f)),
            (ImGuiCol.DragDropTarget, new Vector4(0.17f, 0.34f, 0.59f, 0.90f)),
            (ImGuiCol.NavHighlight, new Vector4(0.17f, 0.34f, 0.59f, 1.00f)),
            (ImGuiCol.DockingPreview, new Vector4(0.17f, 0.34f, 0.59f, 0.40f)),
            (ImGuiCol.DockingEmptyBg, new Vector4(0.96f, 0.96f, 0.94f, 1.00f)),
        ],
        _ => [],
    };

    private static void ApplyMetrics(ImGuiStylePtr style, SyncTheme theme)
    {
        var scale = ImGuiHelpers.GlobalScale;
        switch (theme)
        {
            case SyncTheme.DarkStyle:
                style.WindowPadding = new Vector2(8.0f, 8.0f) * scale;
                style.FramePadding = new Vector2(5.0f, 3.0f) * scale;
                style.CellPadding = new Vector2(6.0f, 4.0f) * scale;
                style.ItemSpacing = new Vector2(6.0f, 4.0f) * scale;
                style.ItemInnerSpacing = new Vector2(6.0f, 4.0f) * scale;
                style.ScrollbarSize = 13.0f * scale;
                style.GrabMinSize = 10.0f * scale;
                style.WindowBorderSize = 1.0f * scale;
                style.ChildBorderSize = 1.0f * scale;
                style.PopupBorderSize = 1.0f * scale;
                style.FrameBorderSize = 1.0f * scale;
                style.WindowRounding = 4.0f * scale;
                style.ChildRounding = 3.0f * scale;
                style.FrameRounding = 3.0f * scale;
                style.PopupRounding = 3.0f * scale;
                style.ScrollbarRounding = 9.0f * scale;
                style.GrabRounding = 3.0f * scale;
                style.TabRounding = 3.0f * scale;
                break;
            case SyncTheme.ForestGreen:
                style.WindowPadding = new Vector2(10.0f, 10.0f) * scale;
                style.FramePadding = new Vector2(6.0f, 4.0f) * scale;
                style.CellPadding = new Vector2(6.0f, 4.0f) * scale;
                style.ItemSpacing = new Vector2(8.0f, 6.0f) * scale;
                style.ItemInnerSpacing = new Vector2(6.0f, 4.0f) * scale;
                style.TouchExtraPadding = new Vector2(0.0f, 0.0f) * scale;
                style.IndentSpacing = 20.0f * scale;
                style.ScrollbarSize = 14.0f * scale;
                style.GrabMinSize = 12.0f * scale;
                style.WindowBorderSize = 1.0f * scale;
                style.ChildBorderSize = 1.0f * scale;
                style.PopupBorderSize = 1.0f * scale;
                style.FrameBorderSize = 1.0f * scale;
                style.TabBorderSize = 0.0f * scale;
                style.WindowRounding = 6.0f * scale;
                style.ChildRounding = 4.0f * scale;
                style.FrameRounding = 4.0f * scale;
                style.PopupRounding = 4.0f * scale;
                style.ScrollbarRounding = 4.0f * scale;
                style.GrabRounding = 4.0f * scale;
                style.LogSliderDeadzone = 4.0f * scale;
                style.TabRounding = 4.0f * scale;
                break;
            case SyncTheme.Amethyst:
                style.WindowPadding = new Vector2(8.0f, 8.0f) * scale;
                style.FramePadding = new Vector2(5.0f, 3.0f) * scale;
                style.CellPadding = new Vector2(6.0f, 4.0f) * scale;
                style.ItemSpacing = new Vector2(6.0f, 4.0f) * scale;
                style.ItemInnerSpacing = new Vector2(6.0f, 4.0f) * scale;
                style.ScrollbarSize = 13.0f * scale;
                style.GrabMinSize = 10.0f * scale;
                style.WindowBorderSize = 1.0f * scale;
                style.ChildBorderSize = 1.0f * scale;
                style.PopupBorderSize = 1.0f * scale;
                style.FrameBorderSize = 1.0f * scale;
                style.WindowRounding = 4.0f * scale;
                style.ChildRounding = 3.0f * scale;
                style.FrameRounding = 3.0f * scale;
                style.PopupRounding = 3.0f * scale;
                style.ScrollbarRounding = 9.0f * scale;
                style.GrabRounding = 3.0f * scale;
                style.TabRounding = 3.0f * scale;
                break;
            case SyncTheme.Sapphire:
                style.WindowPadding = new Vector2(10.0f, 10.0f) * scale;
                style.FramePadding = new Vector2(6.0f, 4.0f) * scale;
                style.ItemSpacing = new Vector2(8.0f, 4.0f) * scale;
                style.ScrollbarSize = 15.0f * scale;
                style.GrabMinSize = 10.0f * scale;
                style.WindowRounding = 5.0f * scale;
                style.FrameRounding = 4.0f * scale;
                style.PopupRounding = 4.0f * scale;
                style.ScrollbarRounding = 12.0f * scale;
                style.GrabRounding = 3.0f * scale;
                style.TabRounding = 4.0f * scale;
                style.WindowBorderSize = 1.0f * scale;
                style.FrameBorderSize = 1.0f * scale;
                break;
            case SyncTheme.AmberYellow:
                style.WindowPadding = new Vector2(8.0f, 8.0f) * scale;
                style.FramePadding = new Vector2(5.0f, 3.0f) * scale;
                style.CellPadding = new Vector2(6.0f, 4.0f) * scale;
                style.ItemSpacing = new Vector2(6.0f, 4.0f) * scale;
                style.ScrollbarSize = 12.0f * scale;
                style.GrabMinSize = 10.0f * scale;
                style.WindowRounding = 2.0f * scale;
                style.ChildRounding = 2.0f * scale;
                style.FrameRounding = 2.0f * scale;
                style.PopupRounding = 2.0f * scale;
                style.ScrollbarRounding = 2.0f * scale;
                style.GrabRounding = 2.0f * scale;
                style.TabRounding = 2.0f * scale;
                style.WindowBorderSize = 1.0f * scale;
                style.FrameBorderSize = 1.0f * scale;
                break;
            case SyncTheme.Dracula:
                style.WindowPadding = new Vector2(10.0f, 10.0f) * scale;
                style.FramePadding = new Vector2(6.0f, 4.0f) * scale;
                style.ItemSpacing = new Vector2(8.0f, 6.0f) * scale;
                style.ScrollbarSize = 14.0f * scale;
                style.GrabMinSize = 12.0f * scale;
                style.WindowRounding = 6.0f * scale;
                style.FrameRounding = 4.0f * scale;
                style.PopupRounding = 4.0f * scale;
                style.ScrollbarRounding = 12.0f * scale;
                style.GrabRounding = 4.0f * scale;
                style.TabRounding = 4.0f * scale;
                style.WindowBorderSize = 1.0f * scale;
                style.FrameBorderSize = 1.0f * scale;
                break;
            case SyncTheme.CatppuccinMocha:
                style.WindowPadding = new Vector2(12.0f, 12.0f) * scale;
                style.FramePadding = new Vector2(6.0f, 4.0f) * scale;
                style.ItemSpacing = new Vector2(8.0f, 6.0f) * scale;
                style.ScrollbarSize = 14.0f * scale;
                style.GrabMinSize = 12.0f * scale;
                style.WindowRounding = 8.0f * scale;
                style.FrameRounding = 5.0f * scale;
                style.PopupRounding = 5.0f * scale;
                style.ScrollbarRounding = 12.0f * scale;
                style.GrabRounding = 5.0f * scale;
                style.TabRounding = 5.0f * scale;
                style.WindowBorderSize = 1.0f * scale;
                style.FrameBorderSize = 0.0f * scale;
                style.PopupBorderSize = 1.0f * scale;
                break;
            case SyncTheme.GruvboxHard:
                style.WindowPadding = new Vector2(10.0f, 10.0f) * scale;
                style.FramePadding = new Vector2(6.0f, 4.0f) * scale;
                style.ItemSpacing = new Vector2(8.0f, 4.0f) * scale;
                style.ScrollbarSize = 14.0f * scale;
                style.GrabMinSize = 12.0f * scale;
                style.WindowRounding = 2.0f * scale;
                style.FrameRounding = 2.0f * scale;
                style.PopupRounding = 2.0f * scale;
                style.ScrollbarRounding = 2.0f * scale;
                style.GrabRounding = 2.0f * scale;
                style.TabRounding = 2.0f * scale;
                style.WindowBorderSize = 1.0f * scale;
                style.FrameBorderSize = 1.0f * scale;
                style.PopupBorderSize = 1.0f * scale;
                break;
            case SyncTheme.CrimsonVesuvius:
                style.WindowPadding = new Vector2(10.0f, 10.0f) * scale;
                style.FramePadding = new Vector2(5.0f, 3.0f) * scale;
                style.ItemSpacing = new Vector2(8.0f, 4.0f) * scale;
                style.ScrollbarSize = 13.0f * scale;
                style.GrabMinSize = 10.0f * scale;
                style.WindowRounding = 3.0f * scale;
                style.FrameRounding = 2.0f * scale;
                style.PopupRounding = 2.0f * scale;
                style.ScrollbarRounding = 12.0f * scale;
                style.GrabRounding = 2.0f * scale;
                style.TabRounding = 3.0f * scale;
                style.WindowBorderSize = 1.0f * scale;
                style.FrameBorderSize = 1.0f * scale;
                break;
            case SyncTheme.RoseQuartz:
                style.WindowPadding = new Vector2(10.0f, 10.0f) * scale;
                style.FramePadding = new Vector2(6.0f, 4.0f) * scale;
                style.ItemSpacing = new Vector2(8.0f, 5.0f) * scale;
                style.ScrollbarSize = 14.0f * scale;
                style.GrabMinSize = 12.0f * scale;
                style.WindowRounding = 10.0f * scale;
                style.ChildRounding = 6.0f * scale;
                style.FrameRounding = 6.0f * scale;
                style.PopupRounding = 6.0f * scale;
                style.ScrollbarRounding = 12.0f * scale;
                style.GrabRounding = 6.0f * scale;
                style.TabRounding = 6.0f * scale;
                style.WindowBorderSize = 1.0f * scale;
                style.FrameBorderSize = 0.0f * scale;
                break;
            case SyncTheme.Cyberpunk:
                style.WindowPadding = new Vector2(10.0f, 10.0f) * scale;
                style.FramePadding = new Vector2(6.0f, 4.0f) * scale;
                style.ItemSpacing = new Vector2(8.0f, 4.0f) * scale;
                style.ScrollbarSize = 13.0f * scale;
                style.GrabMinSize = 10.0f * scale;
                style.WindowRounding = 0.0f * scale;
                style.FrameRounding = 0.0f * scale;
                style.PopupRounding = 0.0f * scale;
                style.ScrollbarRounding = 0.0f * scale;
                style.GrabRounding = 0.0f * scale;
                style.TabRounding = 0.0f * scale;
                style.WindowBorderSize = 1.0f * scale;
                style.FrameBorderSize = 1.0f * scale;
                style.PopupBorderSize = 1.0f * scale;
                break;
            case SyncTheme.PaperAndInk:
                style.WindowPadding = new Vector2(12.0f, 12.0f) * scale;
                style.FramePadding = new Vector2(6.0f, 4.0f) * scale;
                style.CellPadding = new Vector2(6.0f, 4.0f) * scale;
                style.ItemSpacing = new Vector2(8.0f, 6.0f) * scale;
                style.ItemInnerSpacing = new Vector2(6.0f, 4.0f) * scale;
                style.ScrollbarSize = 14.0f * scale;
                style.GrabMinSize = 12.0f * scale;
                style.WindowRounding = 2.0f * scale;
                style.ChildRounding = 2.0f * scale;
                style.FrameRounding = 2.0f * scale;
                style.PopupRounding = 2.0f * scale;
                style.ScrollbarRounding = 12.0f * scale;
                style.GrabRounding = 2.0f * scale;
                style.TabRounding = 2.0f * scale;
                style.WindowBorderSize = 1.0f * scale;
                style.ChildBorderSize = 1.0f * scale;
                style.PopupBorderSize = 1.0f * scale;
                style.FrameBorderSize = 1.0f * scale;
                style.TabBorderSize = 1.0f * scale;
                break;
        }
    }

    private sealed class ThemeScope : IDisposable
    {
        private readonly ImGuiStylePtr style = ImGui.GetStyle();
        private readonly StyleMetrics previous;
        private int colorCount;

        internal ThemeScope(SyncTheme theme)
        {
            previous = StyleMetrics.Capture(style);
            try
            {
                // Every theme starts with the same palette; partial palettes cannot inherit the previous theme.
                var colors = (Vector4[])GetDarkColors(style).Clone();
                foreach (var (color, value) in Colors(theme)) colors[(int)color] = value;
                for (var index = 0; index < colors.Length; index++)
                {
                    ImGui.PushStyleColor((ImGuiCol)index, colors[index]);
                    colorCount++;
                }
                ApplyMetrics(style, theme);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            previous.Restore(style);
            if (colorCount > 0) ImGui.PopStyleColor(colorCount);
            colorCount = 0;
        }
    }

    private sealed class StyleMetrics
    {
        private Vector2 WindowPadding { get; init; }
        private Vector2 FramePadding { get; init; }
        private Vector2 CellPadding { get; init; }
        private Vector2 ItemSpacing { get; init; }
        private Vector2 ItemInnerSpacing { get; init; }
        private float ScrollbarSize { get; init; }
        private float GrabMinSize { get; init; }
        private float WindowBorderSize { get; init; }
        private float ChildBorderSize { get; init; }
        private float PopupBorderSize { get; init; }
        private float FrameBorderSize { get; init; }
        private float WindowRounding { get; init; }
        private float ChildRounding { get; init; }
        private float FrameRounding { get; init; }
        private float PopupRounding { get; init; }
        private float ScrollbarRounding { get; init; }
        private float GrabRounding { get; init; }
        private float TabRounding { get; init; }
        private Vector2 TouchExtraPadding { get; init; }
        private float IndentSpacing { get; init; }
        private float TabBorderSize { get; init; }
        private float LogSliderDeadzone { get; init; }

        internal static StyleMetrics Capture(ImGuiStylePtr style) => new()
        {
            WindowPadding = style.WindowPadding,
            FramePadding = style.FramePadding,
            CellPadding = style.CellPadding,
            ItemSpacing = style.ItemSpacing,
            ItemInnerSpacing = style.ItemInnerSpacing,
            ScrollbarSize = style.ScrollbarSize,
            GrabMinSize = style.GrabMinSize,
            WindowBorderSize = style.WindowBorderSize,
            ChildBorderSize = style.ChildBorderSize,
            PopupBorderSize = style.PopupBorderSize,
            FrameBorderSize = style.FrameBorderSize,
            WindowRounding = style.WindowRounding,
            ChildRounding = style.ChildRounding,
            FrameRounding = style.FrameRounding,
            PopupRounding = style.PopupRounding,
            ScrollbarRounding = style.ScrollbarRounding,
            GrabRounding = style.GrabRounding,
            TabRounding = style.TabRounding,
            TouchExtraPadding = style.TouchExtraPadding,
            IndentSpacing = style.IndentSpacing,
            TabBorderSize = style.TabBorderSize,
            LogSliderDeadzone = style.LogSliderDeadzone,
        };

        internal void Restore(ImGuiStylePtr style)
        {
            style.WindowPadding = WindowPadding;
            style.FramePadding = FramePadding;
            style.CellPadding = CellPadding;
            style.ItemSpacing = ItemSpacing;
            style.ItemInnerSpacing = ItemInnerSpacing;
            style.ScrollbarSize = ScrollbarSize;
            style.GrabMinSize = GrabMinSize;
            style.WindowBorderSize = WindowBorderSize;
            style.ChildBorderSize = ChildBorderSize;
            style.PopupBorderSize = PopupBorderSize;
            style.FrameBorderSize = FrameBorderSize;
            style.WindowRounding = WindowRounding;
            style.ChildRounding = ChildRounding;
            style.FrameRounding = FrameRounding;
            style.PopupRounding = PopupRounding;
            style.ScrollbarRounding = ScrollbarRounding;
            style.GrabRounding = GrabRounding;
            style.TabRounding = TabRounding;
            style.TouchExtraPadding = TouchExtraPadding;
            style.IndentSpacing = IndentSpacing;
            style.TabBorderSize = TabBorderSize;
            style.LogSliderDeadzone = LogSliderDeadzone;
        }
    }
}
