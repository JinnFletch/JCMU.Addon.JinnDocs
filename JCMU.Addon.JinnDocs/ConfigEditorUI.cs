using JinnDev.JCMU.Addon.JinnDocs.Models;
using JinnDev.JCMU.Addon.JinnDocs.Services;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.JinnDocs;

public static class ConfigEditorUi
{
    public static async Task<DocConfig> RunEditorLoopAsync(string targetDirectory, DocConfig config, IHostServices host, CancellationToken token)
    {
        string? errorMessage = null;

        while (!token.IsCancellationRequested)
        {
            PrintInterface(config, host, errorMessage);
            errorMessage = null; // Reset error after displaying

            var inputResult = await host.PromptUserAsync("Edit command:").ConfigureAwait(false);

            var shouldExit = await inputResult.MatchAsync(
                someAsync: async input =>
                {
                    if (string.IsNullOrWhiteSpace(input)) return false;

                    var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var cmd = parts[0].ToLowerInvariant();

                    if (cmd == "exit" || cmd == "run")
                    {
                        if (cmd == "run")
                            host.UI.WriteLine("Exiting editor. Please select Option 1 from the Main Menu to run.", ConsoleColor.Yellow);

                        return true; // Break the loop
                    }

                    if (cmd == "setname")
                    {
                        if (parts.Length < 2) { errorMessage = "Usage: setname <Project Name>"; return false; }
                        var newName = string.Join(" ", parts.Skip(1));
                        config = config with { ProjectName = newName };
                        ConfigService.SaveConfig(targetDirectory, config);
                        return false;
                    }

                    if (cmd == "add" || cmd == "remove")
                    {
                        if (parts.Length < 3)
                        {
                            errorMessage = $"Usage: {cmd} <ext|ignore|include|wholehog> <value/index>";
                            return false;
                        }

                        var type = parts[1].ToLowerInvariant();
                        var targetList = GetTargetList(config, type);

                        if (targetList == null)
                        {
                            errorMessage = $"Unknown list type '{type}'. Valid types: ext, ignore, include, wholehog.";
                            return false;
                        }

                        if (cmd == "add")
                        {
                            var value = string.Join(" ", parts.Skip(2));
                            if (type == "ext" && !value.StartsWith(".")) value = "." + value;

                            if (!targetList.Contains(value, StringComparer.OrdinalIgnoreCase))
                            {
                                targetList.Add(value);
                                ConfigService.SaveConfig(targetDirectory, config);
                            }
                        }
                        else // remove
                        {
                            if (int.TryParse(parts[2], out int index) && index > 0 && index <= targetList.Count)
                            {
                                targetList.RemoveAt(index - 1);
                                ConfigService.SaveConfig(targetDirectory, config);
                            }
                            else
                            {
                                errorMessage = $"Invalid index. Please provide a number between 1 and {targetList.Count}.";
                            }
                        }

                        return false;
                    }

                    errorMessage = "Unknown command.";
                    return false;
                },
                noneAsync: async err =>
                {
                    // If cancellation requested (CTRL+C), return true to break the loop
                    return token.IsCancellationRequested;
                }
            ).ConfigureAwait(false);

            if (shouldExit) break;
        }

        return config;
    }

    private static void PrintInterface(DocConfig config, IHostServices host, string? errorMessage)
    {
        host.UI.WriteLine("\n==================================================", ConsoleColor.DarkGray);
        host.UI.WriteLine($"--- Configuration Editor: {config.ProjectName} ---", ConsoleColor.Yellow);

        host.UI.WriteLine("\n[ext] Include Extensions:", ConsoleColor.Cyan);
        PrintList(config.IncludeExtensions, host);

        host.UI.WriteLine("\n[ignore] Ignore Paths (Pruned entirely):", ConsoleColor.Red);
        PrintList(config.IgnorePaths, host);

        host.UI.WriteLine("\n[include] Explicit Include Paths (Overrides ignore):", ConsoleColor.Green);
        PrintList(config.IncludePaths, host);

        host.UI.WriteLine("\n[wholehog] Whole Hog Paths (Dumped verbatim):", ConsoleColor.Magenta);
        PrintList(config.WholeHogPaths, host);

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            host.UI.WriteLine($"\n[ERROR] {errorMessage}", ConsoleColor.Red);
        }

        host.UI.WriteLine("\nCommands:", ConsoleColor.DarkGray);
        host.UI.WriteLine("  setname <name>            - Changes the project name", ConsoleColor.DarkGray);
        host.UI.WriteLine("  add <type> <value>        - Adds an item (e.g. 'add ignore bin')", ConsoleColor.DarkGray);
        host.UI.WriteLine("  remove <type> <index>     - Removes an item (e.g. 'remove ext 1')", ConsoleColor.DarkGray);
        host.UI.WriteLine("  exit                      - Return to Main Menu", ConsoleColor.DarkGray);
        host.UI.WriteLine("--------------------------------------------------", ConsoleColor.DarkGray);
    }

    private static void PrintList(List<string> list, IHostServices host)
    {
        if (list.Count == 0)
        {
            host.UI.WriteLine("  (empty)", ConsoleColor.DarkGray);
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            host.UI.WriteLine($"  {i + 1}. {list[i]}");
        }
    }

    private static List<string>? GetTargetList(DocConfig config, string type)
    {
        return type switch
        {
            "ext" => config.IncludeExtensions,
            "ignore" => config.IgnorePaths,
            "include" => config.IncludePaths,
            "wholehog" => config.WholeHogPaths,
            _ => null
        };
    }
}