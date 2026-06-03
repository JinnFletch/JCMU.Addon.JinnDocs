namespace JinnDev.JCMU.Addon.JinnDocs.Parsers;

public class ParserFactory
{
    private readonly Dictionary<string, ILanguageParser> _parsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILanguageParser _fallbackParser = new WholeHogParser();

    public ParserFactory()
    {
        Register(new CSharpParser());
        Register(new MarkdownParser());
    }

    public void Register(ILanguageParser parser)
    {
        foreach (var ext in parser.SupportedExtensions)
        {
            var normalizedExt = ext.StartsWith('.') ? ext : "." + ext;
            _parsers[normalizedExt] = parser;
        }
    }

    /// <summary>
    /// Retrieves the semantic parser for the given extension, or the WholeHog fallback if none exists.
    /// </summary>
    public ILanguageParser GetParser(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return _fallbackParser;

        var normalizedExt = extension.StartsWith('.') ? extension : "." + extension;

        if (_parsers.TryGetValue(normalizedExt, out var parser))
        {
            return parser;
        }

        return _fallbackParser;
    }

    /// <summary>
    /// Forces the retrieval of the WholeHog parser (used when a file hits a 'WholeHogPaths' rule).
    /// </summary>
    public ILanguageParser GetFallbackParser() => _fallbackParser;
}