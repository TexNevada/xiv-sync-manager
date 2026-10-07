using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using XivSyncManager.Windows;

namespace XivSyncManager;

public sealed class Plugin : IAsyncDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/syncmanager";
    private readonly WindowSystem windows = new("XIV Sync Manager");
    private readonly MainWindow mainWindow;
    private readonly SettingsWindow settingsWindow;
    public Configuration Configuration { get; }
    public SyncCoordinator Coordinator { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new();
        Configuration.Priority = [.. (Configuration.Priority ?? []).Where(p => Enum.IsDefined(p)).Distinct()];
        Configuration.PreferredProviders ??= new(StringComparer.Ordinal);
        Configuration.SectionExpanded ??= new(StringComparer.Ordinal);
        Configuration.ManualPauses ??= new(StringComparer.Ordinal);
        Configuration.AutomaticExceptions ??= new(StringComparer.Ordinal);
        Configuration.ObservedCharacters ??= new(StringComparer.Ordinal);
        Configuration.OwnedPauses ??= new(StringComparer.Ordinal);
        Configuration.DuplicateCharacters ??= new(StringComparer.Ordinal);
        Configuration.CharacterPauses ??= new(StringComparer.Ordinal);
        // Move the previous default to RoseQuartz once; keep other saved theme choices.
        if (!Enum.IsDefined(Configuration.Theme)
            || (Configuration.Version < 3 && Configuration.Theme == SyncTheme.ForestGreen))
            Configuration.Theme = SyncTheme.RoseQuartz;
        Configuration.Version = 3;
        foreach (var provider in Enum.GetValues<SyncProvider>())
            if (!Configuration.Priority.Contains(provider)) Configuration.Priority.Add(provider);

        Coordinator = new(Configuration);
        mainWindow = new(this);
        settingsWindow = new(this);
        windows.AddWindow(mainWindow);
        windows.AddWindow(settingsWindow);
        PluginInterface.UiBuilder.Draw += DrawWindows;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsUi;
        Framework.Update += Coordinator.Update;
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open XIV Sync Manager to manage duplicate characters, service priority, and themes.",
        });
    }

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await Framework.RunOnFrameworkThread(() =>
        {
            Framework.Update -= Coordinator.Update;
            PluginInterface.UiBuilder.Draw -= DrawWindows;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettingsUi;
            CommandManager.RemoveHandler(CommandName);
            windows.RemoveAllWindows();
        }).ConfigureAwait(false);
        await Coordinator.StopAsync().ConfigureAwait(false);
    }

    private void DrawWindows()
    {
        if (!mainWindow.IsOpen && !settingsWindow.IsOpen) return;
        lock (Coordinator.SyncRoot)
        {
            using var theme = ThemeCatalog.Push(Configuration.Theme);
            windows.Draw();
        }
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("config", StringComparison.OrdinalIgnoreCase)) ToggleSettingsUi();
        else ToggleMainUi();
    }

    public void ToggleMainUi() => mainWindow.Toggle();

    public void ToggleSettingsUi() => settingsWindow.Toggle();
}
