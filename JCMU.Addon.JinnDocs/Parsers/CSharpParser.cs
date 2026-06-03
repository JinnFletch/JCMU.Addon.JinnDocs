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
            if (line.StartsWith("public") && TryExtractScope(line, out string detectedScope))
            {
                currentScope = detectedScope;

                // Print the scope declaration itself so the LLM knows the container
                if (hasContent) sb.AppendLine();
                sb.AppendLine($"// --- Scope: {currentScope} ---");
                sb.AppendLine(ExtractSignature(lines, i) ?? line);
                hasContent = true;
                continue;
            }

            // 2. Summary & Signature Extraction
            if (line.StartsWith("///") && line.Contains("<summary>"))
            {
                // Extract and clean the summary
                string rawSummary = ExtractSummaryBlock(lines, ref i);
                string cleanSummary = CleanSummary(rawSummary);

                // Look ahead to find the actual code signature
                int signatureStartIndex = i + 1;
                string? signature = ExtractSignature(lines, signatureStartIndex);

                if (!string.IsNullOrWhiteSpace(signature) && signature.Contains("public "))
                {
                    if (hasContent) sb.AppendLine();
                    sb.AppendLine($"// Summary: {cleanSummary}");
                    sb.AppendLine(signature);
                    hasContent = true;
                }
            }
        }

        if (!hasContent)
        {
            sb.AppendLine("// No public documented API found in this file.");
        }

        sb.AppendLine("```\n");
        return sb.ToString();
    }

    private static bool TryExtractScope(string line, out string scopeName)
    {
        scopeName = string.Empty;

        // Matches: public [modifiers] class/struct/record/interface [Name<T>]
        var match = Regex.Match(line, @"public.*?\b(class|struct|record|interface)\s+(?<name>\w+(?:<[^>]+>)?)");
        if (match.Success)
        {
            scopeName = match.Groups["name"].Value;
            return true;
        }

        return false;
    }

    private static string ExtractSummaryBlock(string[] lines, ref int currentIndex)
    {
        var sb = new StringBuilder();

        for (int j = currentIndex; j < lines.Length; j++)
        {
            string currentLine = lines[j].Trim();

            if (!currentLine.StartsWith("///"))
                break; // Stop when we hit code or empty space

            sb.AppendLine(currentLine);
            currentIndex = j; // Advance the outer loop counter so we don't re-process these lines
        }

        var fullBlock = sb.ToString();
        var match = Regex.Match(fullBlock, @"<summary>(.*?)</summary>", RegexOptions.Singleline);

        return match.Success ? match.Groups[1].Value : string.Empty;
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