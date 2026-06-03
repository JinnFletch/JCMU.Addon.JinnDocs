namespace JinnDev.JCMU.Addon.JinnDocs.Parsers;

public class WholeHogParser : ILanguageParser
{
    // WholeHog acts as the universal fallback, so it doesn't strictly claim any extensions natively.
    public string[] SupportedExtensions => Array.Empty<string>();

    public string Parse(string filePath, string fileContent)
    {
        var extension = Path.GetExtension(filePath).TrimStart('.');
        var mdLang = MapToMarkdownLanguage(extension);

        // Using C# raw string literals to perfectly preserve markdown formatting
        return $"""
        ### {filePath}
        ```{mdLang}
        {fileContent}
        ```
        
        """;
    }

    private static string MapToMarkdownLanguage(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            "cs" => "csharp",
            "js" => "javascript",
            "ts" => "typescript",
            "json" => "json",
            "xml" => "xml",
            "html" => "html",
            "css" => "css",
            "sql" => "sql",
            "md" => "markdown",
            "ps1" => "powershell",
            "bat" => "bat",
            "cmd" => "cmd",
            "sh" => "bash",
            "yml" or "yaml" => "yaml",
            _ => "" // Fallback to no highlighting for unknown types
        };
    }
}