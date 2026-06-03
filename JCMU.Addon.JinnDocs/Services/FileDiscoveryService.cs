using JinnDev.JCMU.Addon.JinnDocs.Models;

namespace JinnDev.JCMU.Addon.JinnDocs.Services;

public record DiscoveryResult(
    HashSet<string> TargetFiles,
    HashSet<string> ChildRollupDirectories
);

public static class FileDiscoveryService
{
    /// <summary>
    /// Executes the Two-Pass resolution strategy to find all target files and child sub-projects.
    /// </summary>
    public static DiscoveryResult DiscoverFiles(string targetDirectory, DocConfig config)
    {
        var finalFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var childRollups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Normalize configured extensions to ensure they start with "."
        var validExtensions = config.IncludeExtensions
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.StartsWith(".") ? e : "." + e)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // PASS 1: Broad OS Sweep & Child Discovery
        ExecutePass1_BroadSweep(targetDirectory, config.IgnorePaths, validExtensions, finalFiles, childRollups);

        // PASS 2: Surgical Configuration-Based Inclusion (Implemented in Part 2)
        ExecutePass2_SurgicalInclusion(targetDirectory, config.IncludePaths, validExtensions, finalFiles);

        return new DiscoveryResult(finalFiles, childRollups);
    }

    /// <summary>
    /// Traverses the directory tree using a DFS stack. Physically prunes ignored folders and child projects.
    /// </summary>
    private static void ExecutePass1_BroadSweep(
        string rootDirectory,
        List<string> ignorePaths,
        HashSet<string> validExtensions,
        HashSet<string> targetFiles,
        HashSet<string> childRollups)
    {
        var directoriesToScan = new Stack<string>();
        directoriesToScan.Push(rootDirectory);

        while (directoriesToScan.Count > 0)
        {
            var currentDir = directoriesToScan.Pop();

            try
            {
                // 1. Process Files in current directory
                foreach (var filePath in Directory.EnumerateFiles(currentDir))
                {
                    if (IsImplicitlyIgnored(filePath))
                        continue;

                    if (IsIgnored(filePath, rootDirectory, ignorePaths))
                        continue;

                    string extension = Path.GetExtension(filePath);
                    if (!validExtensions.Contains(extension))
                        continue;

                    targetFiles.Add(filePath);
                }

                // 2. Process Sub-Directories (Pruning & Child Detection)
                foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                {
                    // Prune: Is this directory explicitly ignored by the user?
                    if (IsIgnored(subDir, rootDirectory, ignorePaths))
                        continue;

                    // Prune: Is this a child project? (Has its own .jcmu-docs.json)
                    var childConfigPath = Path.Combine(subDir, ConfigService.ConfigFileName);
                    if (File.Exists(childConfigPath))
                    {
                        childRollups.Add(subDir);
                        continue; // Do not push to stack; let the child context handle this branch later
                    }

                    // Safe to scan, add to stack
                    directoriesToScan.Push(subDir);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Soft skip folders we don't have OS permissions to read
            }
            catch (PathTooLongException)
            {
                // Soft skip abnormally long paths
            }
        }
    }

    /// <summary>
    /// Explicitly resolves specific inclusions, overriding exclusion rules.
    /// </summary>
    private static void ExecutePass2_SurgicalInclusion(
        string rootDirectory,
        List<string> includePaths,
        HashSet<string> validExtensions,
        HashSet<string> targetFiles)
    {
        if (includePaths == null || includePaths.Count == 0) return;

        foreach (var rawPath in includePaths)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) continue;

            // Resolve to absolute path securely
            string fullPath = Path.IsPathRooted(rawPath)
                ? Path.GetFullPath(rawPath)
                : Path.GetFullPath(Path.Combine(rootDirectory, rawPath.TrimStart('\\', '/')));

            // Edge Case A: Explicit File Included
            if (File.Exists(fullPath))
            {
                if (!IsImplicitlyIgnored(fullPath))
                {
                    // Bypass extension filter. If they explicitly asked for "init.sql", they get it.
                    targetFiles.Add(fullPath);
                }
            }
            // Edge Case B: Explicit Directory Included
            else if (Directory.Exists(fullPath))
            {
                // Spin up a mini-sweep just for this included directory.
                // We DO NOT pass 'IgnorePaths' here, because Explicit Include trumps Exclude.
                // We DO pass an empty HashSet for childRollups, as we don't need to recursively 
                // manage child sub-projects inside an explicit path inclusion context for now.
                var dummyChildRollups = new HashSet<string>();
                ExecutePass1_BroadSweep(fullPath, new List<string>(), validExtensions, targetFiles, dummyChildRollups);
            }
        }
    }

    /// <summary>
    /// Evaluates if a file or directory matches any of the user's ignore rules.
    /// </summary>
    public static bool IsIgnored(string absolutePath, string rootDirectory, List<string> ignorePaths)
    {
        if (ignorePaths == null || ignorePaths.Count == 0) return false;

        string itemName = Path.GetFileName(absolutePath);
        string relativePath = Path.GetRelativePath(rootDirectory, absolutePath);

        // Normalize slashes for consistent comparison
        relativePath = relativePath.Replace('\\', '/');

        foreach (var rule in ignorePaths)
        {
            if (string.IsNullOrWhiteSpace(rule)) continue;
            var normalizedRule = rule.Replace('\\', '/').Trim('/');

            // Match 1: Exact name match (e.g., "bin", "node_modules", "Program.cs")
            if (itemName.Equals(normalizedRule, StringComparison.OrdinalIgnoreCase))
                return true;

            // Match 2: Relative path exact match (e.g., "src/tests" or "src/tests/File.cs")
            if (relativePath.Equals(normalizedRule, StringComparison.OrdinalIgnoreCase))
                return true;

            // Match 3: Wildcard/Prefix path match (e.g., rule "src/tests" matches "src/tests/SubFolder")
            if (relativePath.StartsWith(normalizedRule + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Protects the engine from ingesting its own output files or configurations.
    /// </summary>
    private static bool IsImplicitlyIgnored(string filePath)
    {
        string fileName = Path.GetFileName(filePath);

        if (fileName.EndsWith(".jinndoc.md", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fileName.Equals(ConfigService.ConfigFileName, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}