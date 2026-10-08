// Game services and API declarations only. Coordinator, policy, adapter, reflection,
// save queue, and diagnostics are linked directly from production source.
namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}
namespace Newtonsoft.Json
{
    public enum ObjectCreationHandling { Replace }
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class JsonPropertyAttribute : Attribute
    {
        public ObjectCreationHandling ObjectCreationHandling { get; set; }
    }
}
namespace Dalamud.Plugin.Services
{
    public interface IFramework { }
}
namespace Dalamud.Plugin
{
    public interface IExposedPlugin
    {
        string InternalName { get; }
        string Name { get; }
        bool IsLoaded { get; }
    }
}
namespace Dalamud.Plugin.Internal.Types
{
    // Mirror the instance wrapper contract that production ReflectionAccess discovers.
    public sealed class LocalPlugin(object plugin)
    {
        public object instance = plugin;
    }
}
namespace Dalamud.Game.ClientState.Objects.SubKinds
{
    public interface IPlayerCharacter
    {
        nint Address { get; }
        TestText Name { get; }
        TestWorldRef HomeWorld { get; }
    }
    public sealed record TestText(string TextValue);
    public sealed record TestWorld(string Name);
    public sealed record TestWorldRef(uint RowId, TestWorld Value);
    public sealed class TestPlayer(nint address, string name, uint world) : IPlayerCharacter
    {
        public nint Address { get; } = address;
        public TestText Name { get; } = new(name);
        public TestWorldRef HomeWorld { get; } = new(world, new("World"));
    }
}
namespace XivSyncManager
{
    public enum SyncTheme { RoseQuartz }
    public static class Plugin
    {
        public static TestPluginInterface PluginInterface { get; } = new();
        public static TestLog Log { get; } = new();
        public static TestFramework Framework { get; } = new();
        public static List<object> ObjectTable { get; } = [];
    }
    public sealed class TestPluginInterface
    {
        public Action<Configuration>? Write { get; set; }
        public List<Dalamud.Plugin.IExposedPlugin> InstalledPlugins { get; } = [];
        public void SavePluginConfig(Configuration config) => Write?.Invoke(config);
    }
    public sealed class TestLog
    {
        public void Info(string message, params object[] values) { }
        public void Warning(string message, params object[] values) { }
        public void Warning(Exception exception, string message, params object[] values) { }
        public void Error(Exception exception, string message) { }
    }
    public sealed class TestFramework : Dalamud.Plugin.Services.IFramework
    {
        public Task RunOnFrameworkThread(Action action) { action(); return Task.CompletedTask; }
    }
}
