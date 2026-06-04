using System.Text;
using JinnDev.JCMU.Addon.JinnDocs.Models;
using JinnDev.JCMU.Addon.JinnDocs.Parsers;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.JinnDocs.Services;

public class AmalgamationService
{
    private readonly ParserFactory _parserFactory;
    private readonly IHostServices _host;

    public AmalgamationService(ParserFactory parserFactory, IHostServices host)
    {
        _parserFactory = parserFactory;
        _host = host;
    }

    /// <summary>
    /// Executes the generation process for a given directory and configuration.
    /// Supports recursive calls for child roll-ups.
    /// </summary>
    /// <returns>A monad containing the absolute path to the generated .jinndoc.md file.</returns>
    public async Task<Maybe<string>> RunAsync(string globalRootDirectory, string currentDirectory, DocConfig config, bool isChildRollup, CancellationToken token)
    {
        return await Maybe.TryAsync<string>(async () =>
        {
            _host.UI.WriteLine($"\n--- Generating Docs for: {config.ProjectName} ---", ConsoleColor.Cyan);

            // 1. Generate Timestamped Filename
            string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            string outputFileName = $"{config.ProjectName}_{timestamp}.jinndoc.md";
            string outputPath = Path.Combine(currentDirectory, outputFileName); // Save in the current directory being processed

            // 2. Discover Files and Child Rollups (Use currentDirectory for local rules)
            var discovery = FileDiscoveryService.DiscoverFiles(currentDirectory, config);

            // Open the file stream for writing
            using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(fileStream, Encoding.UTF8);

            if (isChildRollup)
            {
                await writer.WriteLineAsync($"\n## [Sub-Project] {config.ProjectName}").ConfigureAwait(false);
                await writer.WriteLineAsync($"---").ConfigureAwait(false);
            }
            else
            {
                await writer.WriteLineAsync($"# {config.ProjectName} Documentation").ConfigureAwait(false);
                await writer.WriteLineAsync($"> Generated on {DateTimeOffset.Now:f}\n").ConfigureAwait(false);
            }

            // 3. Process Child Rollups Recursively
            foreach (var childDir in discovery.ChildRollupDirectories)
            {
                if (token.IsCancellationRequested) break;

                var childConfigResult = ConfigService.LoadConfig(childDir);

                await childConfigResult.MatchAsync(
                    someAsync: async childConfig =>
                    {
                        _host.UI.WriteLine($"  [CHILD ROLLUP] Initiating: {childConfig.ProjectName}", ConsoleColor.Magenta);

                        // Recursive Call: Pass globalRootDirectory down, but update currentDirectory to childDir
                        var childResult = await RunAsync(globalRootDirectory, childDir, childConfig, true, token).ConfigureAwait(false);

                        await childResult.MatchAsync(
                            someAsync: async childFilePath =>
                            {
                                // Append Verbatim
                                string childContent = await File.ReadAllTextAsync(childFilePath, token).ConfigureAwait(false);
                                await writer.WriteLineAsync(childContent).ConfigureAwait(false);

                                // Cleanup Override: Delete the child's file after consuming it into the parent
                                File.Delete(childFilePath);
                                _host.UI.WriteLine($"  [CHILD CONSUMED] {childConfig.ProjectName}", ConsoleColor.DarkMagenta);
                            },
                            noneAsync: err =>
                            {
                                _host.UI.WriteLine($"  [WARN] Child generation failed: {err.Message}", ConsoleColor.Yellow);
                                return Task.CompletedTask;
                            }
                        ).ConfigureAwait(false);
                    },
                    noneAsync: err =>
                    {
                        _host.UI.WriteLine($"  [WARN] Failed to load config for child: {childDir}", ConsoleColor.Yellow);
                        return Task.CompletedTask;
                    }
                ).ConfigureAwait(false);
            }

            // 4. Process Local Files
            foreach (var filePath in discovery.TargetFiles.OrderBy(f => f))
            {
                if (token.IsCancellationRequested) break;

                // Create the display path relative to the GLOBAL root, not the child root.
                string displayPath = Path.GetRelativePath(globalRootDirectory, filePath).Replace('\\', '/');
                _host.UI.WriteLine($"  [+] Processing: {displayPath}", ConsoleColor.DarkGray);

                // We still use currentDirectory here, because WholeHog rules in the child's JSON apply to the child's domain.
                bool isWholeHog = FileDiscoveryService.IsIgnored(filePath, currentDirectory, config.WholeHogPaths);

                string content = await File.ReadAllTextAsync(filePath, token).ConfigureAwait(false);
                string parsedOutput;

                if (isWholeHog)
                {
                    var parser = _parserFactory.GetFallbackParser();
                    parsedOutput = parser.Parse(displayPath, content);
                }
                else
                {
                    string extension = Path.GetExtension(filePath);
                    var parser = _parserFactory.GetParser(extension);
                    parsedOutput = parser.Parse(displayPath, content);
                }

                await writer.WriteLineAsync(parsedOutput).ConfigureAwait(false);
            }

            if (token.IsCancellationRequested)
            {
                _host.UI.WriteLine("\n[CANCELLED] Operation aborted by user.", ConsoleColor.Red);
                throw new OperationCanceledException("Amalgamation cancelled via token.");
            }

            _host.UI.WriteLine($"\n[SUCCESS] Document saved: {outputFileName}", ConsoleColor.Green);

            return outputPath;
        }).ConfigureAwait(false);
    }
}