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
        
        ### {filePath}
        ======================================
        BEGIN MARKDOWN FOR {filePath}
        This type of manual descriptive delimiter is used because markdown
        can include code block ticks, xml, and other delimiter usage itself
        which makes it confusing when the markdown file starts and ends.
        So the end of this file is where it says "END MARKDOWN FOR"
        ======================================

        {fileContent}
        
        ======================================
        END MARKDOWN FOR {filePath}
        This manual descriptive delimiter ends the markdown file
        ======================================

        
        """;
    }
}