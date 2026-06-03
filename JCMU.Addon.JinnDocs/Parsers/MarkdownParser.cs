namespace JinnDev.JCMU.Addon.JinnDocs.Parsers;

public class MarkdownParser : ILanguageParser
{
    public string[] SupportedExtensions => new[] { ".md", ".mdx" };

    public string Parse(string filePath, string fileContent)
    {
        // Using XML tags. This is the industry standard for feeding raw text 
        // documents into LLM context windows (like Claude or GPT-4) without 
        // colliding with markdown formatting.
        return $"""
        
        <markdown_doc path="{filePath}">
        {fileContent}
        </markdown_doc>
        
        """;
    }
}