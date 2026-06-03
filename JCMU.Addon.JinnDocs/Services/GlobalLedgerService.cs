using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.JinnDocs.Services;

public static class GlobalLedgerService
{
    private const string LedgerKey = "JinnDocs_Ledger";

    /// <summary>
    /// Updates the global OS dictionary tracking all configured JinnDocs projects.
    /// </summary>
    public static async Task<Maybe> AddOrUpdateProjectAsync(string absolutePath, string projectName, IHostServices host)
    {
        var dictResult = await host.Settings.GetValueAsync<Dictionary<string, string>>(LedgerKey)
            .MatchAsync(
                someAsync: dict => Task.FromResult(dict),
                noneAsync: _ => Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            ).ConfigureAwait(false);

        // Standardize paths so we don't get duplicates due to trailing slashes
        var normalizedPath = absolutePath.TrimEnd('\\', '/');
        dictResult[normalizedPath] = projectName;

        return await host.Settings.SetValueAsync(LedgerKey, dictResult).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves all known JinnDocs projects across the user's machine.
    /// </summary>
    public static async Task<Maybe<Dictionary<string, string>>> GetAllProjectsAsync(IHostServices host)
    {
        return await host.Settings.GetValueAsync<Dictionary<string, string>>(LedgerKey)
            .MatchAsync(
                someAsync: dict => Task.FromResult(Maybe.Some(dict)),
                noneAsync: _ => Task.FromResult(Maybe.Some(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)))
            ).ConfigureAwait(false);
    }
}