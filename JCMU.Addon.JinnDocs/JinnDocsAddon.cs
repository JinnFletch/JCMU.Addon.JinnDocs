using JinnDev.JCMU.Addon.JinnDocs.Models;
using JinnDev.JCMU.Addon.JinnDocs.Parsers;
using JinnDev.JCMU.Addon.JinnDocs.Services;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.JCMU.SDK.Models;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.JinnDocs;

public class JinnDocsAddon : IJcmuAddon
{
    public async Task<Maybe<int>> ExecuteAsync(ActionContext context)
    {
        var host = context.HostServices;
        var token = context.Token;

        host.UI.WriteLine("==================================================", ConsoleColor.Cyan);
        host.UI.WriteLine("              JinnDocs LLM Packager               ", ConsoleColor.Cyan);
        host.UI.WriteLine("==================================================\n", ConsoleColor.Cyan);

        // 1. Resolve Config: Try to load it. If that fails, run the Virgin Setup.
        var resolvedConfigResult = await ConfigService.LoadConfig(context.TargetDirectory)
            .MatchAsync(
                someAsync: config => Task.FromResult(Maybe.Some(config)),
                noneAsync: async _ => await RunVirginSetupAsync(context.TargetDirectory, host, token).ConfigureAwait(false)
            ).ConfigureAwait(false);

        // 2. Main Pipeline: If we successfully got a config (loaded OR newly setup), run the Menu.
        return await resolvedConfigResult.BindAsync<DocConfig, int>(async config =>
        {
            // Ensure Ledger is up to date
            await GlobalLedgerService.AddOrUpdateProjectAsync(context.TargetDirectory, config.ProjectName, host).ConfigureAwait(false);

            while (!token.IsCancellationRequested)
            {
                host.UI.WriteLine($"\n--- JinnDocs Menu: {config.ProjectName} ---", ConsoleColor.Yellow);
                host.UI.WriteLine("  1. Generate Documentation");
                host.UI.WriteLine("  2. Edit Configuration");
                host.UI.WriteLine("  3. Copy Configuration From Elsewhere");
                host.UI.WriteLine("  4. Delete all JinnDocs");
                host.UI.WriteLine("  5. Exit");

                var shouldBreak = await host.PromptUserAsync("\nSelect Option [1]:")
                    .TapAsync(
                        someActionAsync: async input =>
                        {
                            if (input == "1") await RunAmalgamationAsync(context.TargetDirectory, config, host, token).ConfigureAwait(false);
                            else if (input == "2") config = await ConfigEditorUi.RunEditorLoopAsync(context.TargetDirectory, config, host, token).ConfigureAwait(false);
                            else if (input == "3") config = await CopyConfigurationAsync(context.TargetDirectory, config, host, token).ConfigureAwait(false);
                            else if (input == "4") await DeleteAllJinnDocsAsync(context.TargetDirectory, host).ConfigureAwait(false);
                            else if (input == "5") return; // Let loop end naturally
                            else host.UI.WriteLine("Invalid option.", ConsoleColor.Red);
                        },
                        noneActionAsync: async err =>
                        {
                            if (token.IsCancellationRequested) return; // Ctrl+C aborted
                            // Default action on Enter
                            await RunAmalgamationAsync(context.TargetDirectory, config, host, token).ConfigureAwait(false);
                        }
                    )
                    .MatchAsync(
                        // Update this to break the loop if they choose 1 (Run) or 5 (Exit)
                        someAsync: val => Task.FromResult(val == "1" || val == "5"),
                        noneAsync: _ => Task.FromResult(!token.IsCancellationRequested)
                    )
                    .ConfigureAwait(false);

                if (shouldBreak) break;
            }

            return Maybe.Some(-1); // Pause before closing window
        }).ConfigureAwait(false);
    }

    private static async Task<Maybe<DocConfig>> RunVirginSetupAsync(string targetDirectory, IHostServices host, CancellationToken token)
    {
        host.UI.WriteLine("No configuration found. Initializing new JinnDocs project...", ConsoleColor.DarkGray);

        var promptResult = await host.PromptUserAsync("Enter Project Name:").ConfigureAwait(false);

        return await promptResult.MatchAsync(
            someAsync: async projectName =>
            {
                var config = ConfigService.CreateDefaultConfig(projectName);

                var saveResult = ConfigService.SaveConfig(targetDirectory, config);
                if (!saveResult.HasValue)
                {
                    host.UI.WriteLine($"Failed to save config: {saveResult.Message}", ConsoleColor.Red);
                    return Maybe.None<DocConfig>(saveResult.Message);
                }

                await GlobalLedgerService.AddOrUpdateProjectAsync(targetDirectory, projectName, host).ConfigureAwait(false);
                GitignoreService.InjectIdempotently(targetDirectory);

                host.UI.WriteLine("\n[SUCCESS] Initialization complete.", ConsoleColor.Green);
                return Maybe.Some(config);
            },
            noneAsync: _ => Task.FromResult(Maybe.None<DocConfig>("Initialization cancelled."))
        ).ConfigureAwait(false);
    }

    private static async Task<Maybe> RunAmalgamationAsync(string targetDirectory, DocConfig config, IHostServices host, CancellationToken token)
    {
        var factory = new ParserFactory();
        factory.Register(new CSharpParser());
        // Register future parsers here

        var engine = new AmalgamationService(factory, host);
        var result = await engine.RunAsync(targetDirectory, targetDirectory, config, false, token).ConfigureAwait(false);

        return result.HasValue ? Maybe.SUCCESS : Maybe.Fail(result.Message);
    }

    private static async Task<DocConfig> CopyConfigurationAsync(string targetDirectory, DocConfig localConfig, IHostServices host, CancellationToken token)
    {
        var projectsResult = await GlobalLedgerService.GetAllProjectsAsync(host).ConfigureAwait(false);
        var projects = projectsResult.Match(some: p => p, none: _ => new Dictionary<string, string>());

        if (projects.Count == 0)
        {
            host.UI.WriteLine("No other projects found in the global ledger.", ConsoleColor.Yellow);
            return localConfig;
        }

        var projectList = projects.ToList();
        host.UI.WriteLine("\nAvailable Projects:", ConsoleColor.Cyan);

        for (int i = 0; i < projectList.Count; i++)
        {
            host.UI.WriteLine($"  {i + 1}. {projectList[i].Value} ({projectList[i].Key})");
        }

        var inputResult = await host.PromptUserAsync($"\nSelect project to copy from (1-{projectList.Count}):").ConfigureAwait(false);

        return await inputResult.MatchAsync(
            someAsync: async userInput => CopyFromProject(targetDirectory, localConfig, host, userInput, projectList),
            noneAsync: _ => Task.FromResult(localConfig)
        ).ConfigureAwait(false);
    }

    private static DocConfig CopyFromProject(string targetDirectory, DocConfig localConfig, IHostServices host, string input, List<KeyValuePair<string, string>> projectList)
    {
        if (int.TryParse(input, out int choice) && choice > 0 && choice <= projectList.Count)
        {
            var sourcePath = projectList[choice - 1].Key;
            var sourceConfigResult = ConfigService.LoadConfig(sourcePath);

            return sourceConfigResult.Match(
                some: sourceConfig =>
                {
                    var mergedConfig = localConfig with
                    {
                        IncludeExtensions = MergeArrays(localConfig.IncludeExtensions, sourceConfig.IncludeExtensions),
                        IgnorePaths = MergeArrays(localConfig.IgnorePaths, sourceConfig.IgnorePaths),
                        IncludePaths = MergeArrays(localConfig.IncludePaths, sourceConfig.IncludePaths),
                        WholeHogPaths = MergeArrays(localConfig.WholeHogPaths, sourceConfig.WholeHogPaths)
                    };

                    ConfigService.SaveConfig(targetDirectory, mergedConfig);
                    host.UI.WriteLine($"\n[SUCCESS] Configuration merged from {sourceConfig.ProjectName}.", ConsoleColor.Green);

                    return mergedConfig;
                },
                none: fail =>
                {
                    host.UI.WriteLine($"Failed to load config from {sourcePath}", ConsoleColor.Red);
                    return localConfig;
                }
            );
        }

        host.UI.WriteLine("Invalid selection.", ConsoleColor.Red);
        return localConfig;
    }

    /// <summary>
    /// Helper to merge two string lists using a case-insensitive HashSet to prevent duplicates.
    /// </summary>
    private static List<string> MergeArrays(List<string> local, List<string> source)
    {
        var set = new HashSet<string>(local, StringComparer.OrdinalIgnoreCase);
        foreach (var item in source)
        {
            set.Add(item); // HashSet automatically prevents duplicates
        }
        return set.ToList();
    }

    private static Task DeleteAllJinnDocsAsync(string targetDirectory, IHostServices host)
    {
        host.UI.WriteLine("\nScanning for generated JinnDocs...", ConsoleColor.DarkGray);

        try
        {
            // The C# equivalent of a recursive Windows search
            var files = Directory.GetFiles(targetDirectory, "*.jinndoc.md", SearchOption.AllDirectories);

            if (files.Length == 0)
            {
                host.UI.WriteLine("No JinnDocs found to delete.", ConsoleColor.Yellow);
                return Task.CompletedTask;
            }

            foreach (var file in files)
            {
                File.Delete(file);

                // Optional: print out what is being deleted relative to the root for a cleaner UI
                var relativePath = Path.GetRelativePath(targetDirectory, file);
                host.UI.WriteLine($"  [-] Deleted: {relativePath}", ConsoleColor.DarkGray);
            }

            host.UI.WriteLine($"\n[SUCCESS] Cleaned up {files.Length} document(s).", ConsoleColor.Green);
        }
        catch (Exception ex)
        {
            host.UI.WriteLine($"\n[ERROR] Failed to delete files: {ex.Message}", ConsoleColor.Red);
        }

        return Task.CompletedTask;
    }
}