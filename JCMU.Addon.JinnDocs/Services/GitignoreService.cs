using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.JinnDocs.Services;

public static class GitignoreService
{
    private const string IgnorePattern = "*.jinndoc.md";

    /// <summary>
    /// Traverses upward from the target directory looking for the .git root.
    /// If an adjacent .gitignore is found, injects the wildcard idempotently.
    /// </summary>
    public static Maybe InjectIdempotently(string targetDirectory)
    {
        return Maybe.Try(() =>
        {
            var currentDir = new DirectoryInfo(targetDirectory);

            while (currentDir != null)
            {
                var gitFolderPath = Path.Combine(currentDir.FullName, ".git");

                if (Directory.Exists(gitFolderPath))
                {
                    // We found the git root. Look for the .gitignore file.
                    var gitignorePath = Path.Combine(currentDir.FullName, ".gitignore");

                    if (File.Exists(gitignorePath))
                    {
                        var content = File.ReadAllText(gitignorePath);

                        // Idempotency check: don't inject if it's already there
                        if (!content.Contains(IgnorePattern, StringComparison.OrdinalIgnoreCase))
                        {
                            // Ensure clean newline padding
                            var padding = content.EndsWith("\n") ? "" : Environment.NewLine;
                            File.AppendAllText(gitignorePath, $"{padding}{IgnorePattern}{Environment.NewLine}");
                        }
                    }

                    // Stop traversing upward once we hit the .git root, 
                    // whether the .gitignore existed or not.
                    break;
                }

                currentDir = currentDir.Parent;
            }
        });
    }
}