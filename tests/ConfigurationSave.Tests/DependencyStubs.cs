// Only the game services and serialization attribute declarations are substituted.
// The snapshot copying, save ordering, and worker implementation are the production source.
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

namespace XivSyncManager
{
    public enum SyncTheme { RoseQuartz }

    public sealed class PairSnapshot
    {
        public string Key { get; set; } = string.Empty;
        public string? CharacterIdentity { get; set; }
    }

    public static class Plugin
    {
        public static TestPluginInterface PluginInterface { get; } = new();
        public static TestLog Log { get; } = new();
    }

    public sealed class TestPluginInterface
    {
        public Action<Configuration>? Write { get; set; }
        public void SavePluginConfig(Configuration config) => Write?.Invoke(config);
    }

    public sealed class TestLog
    {
        public void Error(Exception exception, string message) { }
        public void Warning(string message, params object[] values) { }
    }
}
