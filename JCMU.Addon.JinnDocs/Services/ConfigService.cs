using System.Text.Json;
using JinnDev.JCMU.Addon.JinnDocs.Models;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.JinnDocs.Services;

public static class ConfigService
{
    public const string ConfigFileName = ".jcmu-docs.json";

    public static Maybe<DocConfig> LoadConfig(string targetDirectory)
    {
        return Maybe.Try<DocConfig>(() =>
        {
            var path = Path.Combine(targetDirectory, ConfigFileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Config not found.");

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<DocConfig>(json);

            return config ?? throw new Exception("Failed to deserialize config.");
        });
    }

    public static Maybe SaveConfig(string targetDirectory, DocConfig config)
    {
        return Maybe.Try(() =>
        {
            var path = Path.Combine(targetDirectory, ConfigFileName);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);

            File.WriteAllText(path, json);
        });
    }

    public static DocConfig CreateDefaultConfig(string projectName)
    {
        return new DocConfig
        {
            ProjectName = projectName,
            IncludeExtensions = new List<string> { ".cs", ".sql", ".json", ".md", ".ts", ".js", ".html", ".css" },
            IgnorePaths = new List<string> { "bin", "obj", ".git", ".vs", "node_modules", "Publish", "TestResults" },
            IncludePaths = new List<string>(), // Empty by default
            WholeHogPaths = new List<string> { "AppSettings.json", "manifest.json", "Program.cs" }
        };
    }
}