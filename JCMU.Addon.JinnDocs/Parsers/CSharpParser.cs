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

        bool hasContent = false;
        bool isInsideClass = false;

        sb.AppendLine($"### {filePath}");
        sb.AppendLine("```csharp");

        // Extract namespace and place it at the top
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("namespace "))
            {
                sb.AppendLine(trimmed);
                sb.AppendLine();
                break;
            }
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("namespace ")) continue;

            // 1. Scope Detection (class, struct, record, interface, enum)
            if (line.StartsWith("public") && TryExtractScope(line, out string detectedScope, out string scopeType))
            {
                // If we were already inside a class, close it before starting a new scope
                if (isInsideClass)
                {
                    sb.AppendLine("}");
                    sb.AppendLine();
                    isInsideClass = false;
                }
                else if (hasContent)
                {
                    sb.AppendLine();
                }

                // Extract summary
                string scopeSummary = ExtractSummaryLookbehind(lines, i);
                if (!string.IsNullOrWhiteSpace(scopeSummary))
                {
                    sb.AppendLine($"// Summary: {scopeSummary}");
                }

                // If it's a class, open a curly brace block. Otherwise, extract the whole thing.
                if (scopeType == "class")
                {
                    string sig = ExtractSignature(lines, i)?.TrimEnd(';') ?? line;
                    sb.AppendLine(sig);
                    sb.AppendLine("{");
                    isInsideClass = true;
                }
                else
                {
                    sb.AppendLine(ExtractWholeBlock(lines, ref i));
                }

                hasContent = true;
                continue;
            }

            // 2. General Public API Detection (Methods, Properties, Fields)
            if (line.StartsWith("public ") && !line.Contains("class ") && !line.Contains("record ") && !line.Contains("interface ") && !line.Contains("struct ") && !line.Contains("enum "))
            {
                string cleanSummary = ExtractSummaryLookbehind(lines, i);
                string? signature = ExtractSignature(lines, i);

                if (!string.IsNullOrWhiteSpace(signature))
                {
                    string indent = isInsideClass ? "    " : "";

                    if (!string.IsNullOrWhiteSpace(cleanSummary))
                    {
                        sb.AppendLine($"{indent}// Summary: {cleanSummary}");
                    }

                    sb.AppendLine($"{indent}{signature}");
                    hasContent = true;
                }
            }
        }

        // Ensure we close the class if the file ended while inside one
        if (isInsideClass)
        {
            sb.AppendLine("}");
        }

        if (!hasContent) sb.AppendLine("// No public API found in this file.");
        sb.AppendLine("```\n");
        return sb.ToString();
    }

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

            if (!startedBraces && line.TrimEnd().EndsWith(";"))
            {
                currentIndex = j;
                break;
            }

            if (startedBraces && braceCount <= 0)
            {
                currentIndex = j;
                break;
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string ExtractSummaryLookbehind(string[] lines, int currentIndex)
    {
        var summaryLines = new List<string>();

        for (int j = currentIndex - 1; j >= 0; j--)
        {
            string prevLine = lines[j].Trim();
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

        var match = Regex.Match(line, @"public.*?\b(class|struct|record|interface|enum)\s+(?<name>\w+(?:<[^>]+>)?)");
        if (match.Success)
        {
            scopeType = match.Groups[1].Value;
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
            if (codeLine.StartsWith("[") && codeLine.EndsWith("]")) continue;
            if (codeLine.StartsWith("///") || codeLine.StartsWith("//")) break;

            foundContent = true;

            var propMatch = Regex.Match(codeLine, @"\{[^}]*\b(get|set|init)\b[^}]*\}");
            if (propMatch.Success)
            {
                sb.Append(codeLine.Substring(0, propMatch.Index + propMatch.Length));
                break;
            }

            int braceIndex = codeLine.IndexOf('{');
            int arrowIndex = codeLine.IndexOf("=>");
            int semiIndex = codeLine.IndexOf(';');

            int cutOffIndex = -1;
            if (braceIndex != -1) cutOffIndex = braceIndex;
            if (arrowIndex != -1 && (cutOffIndex == -1 || arrowIndex < cutOffIndex)) cutOffIndex = arrowIndex;
            if (semiIndex != -1 && (cutOffIndex == -1 || semiIndex < cutOffIndex)) cutOffIndex = semiIndex;

            if (cutOffIndex != -1)
            {
                sb.Append(codeLine.Substring(0, cutOffIndex));

                // Inject { get; set; } for properties, ensuring we DON'T do it for class declarations
                if (cutOffIndex == braceIndex && !sb.ToString().Contains("(") && !sb.ToString().Contains("class "))
                {
                    sb.Append(" { get; set; }");
                    return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
                }

                break;
            }
            else
            {
                sb.Append(codeLine + " ");
            }
        }

        if (!foundContent) return null;

        string cleanSig = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        cleanSig = cleanSig.Replace("( ", "(");

        if (!cleanSig.EndsWith("}") && !cleanSig.EndsWith(";"))
        {
            cleanSig += ";";
        }

        return cleanSig;
    }

    private static string CleanSummary(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "[No Summary Found]";
        var noSlashes = raw.Replace("///", "");
        return Regex.Replace(noSlashes, @"\s+", " ").Trim();
    }
}