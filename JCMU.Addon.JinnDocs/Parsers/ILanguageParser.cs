namespace JinnDev.JCMU.Addon.JinnDocs.Parsers;

public interface ILanguageParser
{
    /// <summary>
    /// The file extensions this parser knows how to handle (e.g., ".cs").
    /// </summary>
    string[] SupportedExtensions { get; }

    /// <summary>
    /// Parses the file content and returns an optimized Markdown representation.
    /// </summary>
    /// <param name="filePath">The relative path of the file (used for header context).</param>
    /// <param name="fileContent">The raw text content of the file.</param>
    string Parse(string filePath, string fileContent);
}