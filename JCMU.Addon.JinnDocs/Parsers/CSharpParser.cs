using System.Text;
using System.Text.RegularExpressions;

namespace JinnDev.JCMU.Addon.JinnDocs.Parsers;

public class CSharpParser : ILanguageParser
{
    public string[] SupportedExtensions => new[] { ".cs" };

    public string Parse(string filePath, string fileContent)
    {
        var sb = new StringBuilder();
        var lines = fileContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        string currentScope = string.Empty;
        bool hasContent = false;

        sb.AppendLine($"### {filePath}");
        sb.AppendLine("```csharp");

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 1. Scope Detection (class, struct, record, interface)
            if (line.StartsWith("public") && TryExtractScope(line, out string detectedScope, out string scopeType))
            {
                currentScope = detectedScope;
                if (hasContent) sb.AppendLine();

                // Look backwards for a summary on the scope itself
                string scopeSummary = ExtractSummaryLookbehind(lines, i);
                if (!string.IsNullOrWhiteSpace(scopeSummary))
                {
                    sb.AppendLine($"// Summary: {scopeSummary}");
                }

                sb.AppendLine($"// --- Scope: {currentScope} ---");

                // If it's a DTO-style type, grab the whole thing. Otherwise, just the signature.
                if (scopeType == "record" || scopeType == "struct")
                {
                    sb.AppendLine(ExtractWholeBlock(lines, ref i));
                }
                else
                {
                    sb.AppendLine(ExtractSignature(lines, i) ?? line);
                }

                hasContent = true;
                continue;
            }

            // 2. General Public API Detection (Methods, Properties, Fields)
            if (line.StartsWith("public ") && !line.Contains("class ") && !line.Contains("record ") && !line.Contains("interface ") && !line.Contains("struct "))
            {
                string cleanSummary = ExtractSummaryLookbehind(lines, i);
                string? signature = ExtractSignature(lines, i);

                if (!string.IsNullOrWhiteSpace(signature))
                {
                    if (hasContent) sb.AppendLine();
                    if (!string.IsNullOrWhiteSpace(cleanSummary))
                    {
                        sb.AppendLine($"// Summary: {cleanSummary}");
                    }
                    sb.AppendLine(signature);
                    hasContent = true;
                }
            }
        }

        if (!hasContent) sb.AppendLine("// No public API found in this file.");
        sb.AppendLine("```\n");
        return sb.ToString();
    }

    /// <summary>
    /// Slurps lines until the brace count balances to 0, or it hits a trailing semicolon (for positional records).
    /// </summary>
    private static string ExtractWholeBlock(string[] lines, ref int currentIndex)
    {
        var sb = new StringBuilder();
        int braceCount = 0;
        bool startedBraces = false;

        for (int j = currentIndex; j < lines.Length; j++)
        {
            string line = lines[j];
            sb.AppendLine(line);

            braceCount += line.Count(c => c == '{');
            braceCount -= line.Count(c => c == '}');

            if (line.Contains('{')) startedBraces = true;

            // Stop Condition 1: Positional record ending with ';' (e.g. public record Foo(int A);)
            if (!startedBraces && line.TrimEnd().EndsWith(";"))
            {
                currentIndex = j; // Advance outer loop
                break;
            }

            // Stop Condition 2: Braces balanced and closed
            if (startedBraces && braceCount <= 0)
            {
                currentIndex = j; // Advance outer loop
                break;
            }
        }

        return sb.ToString().TrimEnd();
    }

    // Add this helper method to look backwards for XML comments
    private static string ExtractSummaryLookbehind(string[] lines, int currentIndex)
    {
        var summaryLines = new List<string>();

        // Walk backwards from the line just above 'public ...'
        for (int j = currentIndex - 1; j >= 0; j--)
        {
            string prevLine = lines[j].Trim();

            // Skip attributes like [JsonIgnore] that might be between the comment and the signature
            if (prevLine.StartsWith("[") && prevLine.EndsWith("]")) continue;

            if (!prevLine.StartsWith("///")) break;

            summaryLines.Insert(0, prevLine);
        }

        if (summaryLines.Count == 0) return string.Empty;

        var fullBlock = string.Join(Environment.NewLine, summaryLines);
        var match = Regex.Match(fullBlock, @"<summary>(.*?)</summary>", RegexOptions.Singleline);

        return match.Success ? CleanSummary(match.Groups[1].Value) : string.Empty;
    }

    private static bool TryExtractScope(string line, out string scopeName, out string scopeType)
    {
        scopeName = string.Empty;
        scopeType = string.Empty;

        // Matches: public [modifiers] class/struct/record/interface [Name<T>]
        var match = Regex.Match(line, @"public.*?\b(class|struct|record|interface)\s+(?<name>\w+(?:<[^>]+>)?)");
        if (match.Success)
        {
            scopeType = match.Groups[1].Value; // Extracts 'class', 'record', etc.
            scopeName = match.Groups["name"].Value;
            return true;
        }

        return false;
    }

    private static string? ExtractSignature(string[] lines, int startIndex)
    {
        var sb = new StringBuilder();
        bool foundContent = false;

        for (int k = startIndex; k < lines.Length; k++)
        {
            string codeLine = lines[k].Trim();

            if (string.IsNullOrWhiteSpace(codeLine)) continue;

            // Skip Attributes (Simple heuristic: starts with [ and ends with ])
            if (codeLine.StartsWith("[") && codeLine.EndsWith("]")) continue;

            // Stop if we hit something that isn't code (like another comment block)
            if (codeLine.StartsWith("///") || codeLine.StartsWith("//")) break;

            foundContent = true;

            // Look for signature terminators
            int braceIndex = codeLine.IndexOf('{');
            int arrowIndex = codeLine.IndexOf("=>");
            int semiIndex = codeLine.IndexOf(';');

            int cutOffIndex = -1;
            if (braceIndex != -1) cutOffIndex = braceIndex;
            if (arrowIndex != -1 && (cutOffIndex == -1 || arrowIndex < cutOffIndex)) cutOffIndex = arrowIndex;
            if (semiIndex != -1 && (cutOffIndex == -1 || semiIndex < cutOffIndex)) cutOffIndex = semiIndex;

            if (cutOffIndex != -1)
            {
                // Terminator found, append up to the cutoff point and break
                sb.Append(codeLine.Substring(0, cutOffIndex));
                break;
            }
            else
            {
                // Multiline signature, keep appending
                sb.Append(codeLine + " ");
            }
        }

        if (!foundContent) return null;

        // Cleanup multiple spaces and format
        string cleanSig = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        cleanSig = cleanSig.Replace("( ", "(");

        // Ensure it ends cleanly (we stripped the terminators for uniformity)
        return cleanSig + ";";
    }

    private static string CleanSummary(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "[No Summary Found]";

        // Remove '///' residues
        var noSlashes = raw.Replace("///", "");

        // Normalize whitespace: replace line breaks, tabs, and multiple spaces with a single space
        return Regex.Replace(noSlashes, @"\s+", " ").Trim();
    }
}