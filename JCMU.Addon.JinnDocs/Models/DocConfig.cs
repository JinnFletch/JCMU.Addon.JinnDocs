using System.Text.Json.Serialization;

namespace JinnDev.JCMU.Addon.JinnDocs.Models;

public record DocConfig
{
    [JsonPropertyName("ProjectName")]
    public string ProjectName { get; init; } = string.Empty;

    [JsonPropertyName("IncludeExtensions")]
    public List<string> IncludeExtensions { get; init; } = new();

    [JsonPropertyName("IgnorePaths")]
    public List<string> IgnorePaths { get; init; } = new();

    [JsonPropertyName("IncludePaths")]
    public List<string> IncludePaths { get; init; } = new();

    [JsonPropertyName("WholeHogPaths")]
    public List<string> WholeHogPaths { get; init; } = new();
}