using System.Text.Json;
using System.Text.Json.Nodes;

namespace OotSceneViewer;

static class ViewerConfig
{
    internal static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    internal static string? ReadNupPath(string configPath)
    {
        if (!File.Exists(configPath))
            return null;
        var root = ReadObject(configPath);
        if (!root.TryGetPropertyValue("nupPath", out var value) || value is null)
            return null;
        if (value is not JsonValue item || !item.TryGetValue<string>(out var path) || string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("The saved NUP path in config.json is invalid.");
        return Path.GetFullPath(path);
    }

    internal static void SaveNupPath(string configPath, string nupPath)
    {
        if (!Path.IsPathFullyQualified(nupPath))
            throw new ArgumentException("NUP path must be absolute.", nameof(nupPath));
        var root = File.Exists(configPath) ? ReadObject(configPath) : new JsonObject();
        root["nupPath"] = Path.GetFullPath(nupPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        var temp = Path.Combine(directory, $".config-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            File.Move(temp, configPath, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static JsonObject ReadObject(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("config.json must contain an object.");
        }
        catch (JsonException ex) { throw new InvalidDataException("config.json is not valid JSON.", ex); }
    }
}
